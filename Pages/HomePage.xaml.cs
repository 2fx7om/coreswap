
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreSwap.Models;
using CoreSwap.Services;

namespace CoreSwap.Pages;

// Display models for Home cards.
// These do not change the Firebase data models.
public sealed class RequestCard
{
    public RequestItem Request { get; set; } = new();
    public ImageSource ImageSource { get; set; } =
        ImageSource.FromFile("laptop_request.jpg");

    public string Title => Request.Title;
    public string OwnerName => Request.OwnerName;
    public string BudgetText => Request.BudgetText;
    public string Category => Request.Category;
}

public sealed class ServiceCard
{
    public ServiceItem Service { get; set; } = new();
    public ImageSource ImageSource { get; set; } =
        ImageSource.FromFile("laptop_repair.jpg");

    public string Title => Service.Title;
    public string OwnerName => Service.OwnerName;
    public string PriceText => Service.PriceText;
}

public partial class HomePage : ContentPage
{
    private readonly ObservableCollection<RequestCard> _requests = new();
    private readonly ObservableCollection<ServiceCard> _services = new();

    private List<RequestCard> _allRequests = new();

    private bool _showingRequests = true;
    private string _selectedCategory = "All";
    private bool _isLoading;

    public HomePage()
    {
        InitializeComponent();

        RequestsCollection.ItemsSource = _requests;
        ServicesCollection.ItemsSource = _services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadWelcomeNameAsync();
        await LoadCurrentModeAsync();
    }

    private async Task LoadWelcomeNameAsync()
    {
        try
        {
            string? fullName =
                await AppServices.Session.GetFullNameAsync();

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                string firstName = fullName.Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

                WelcomeNameLabel.Text = $"Hello, {firstName}";
            }
            else
            {
                WelcomeNameLabel.Text = "Hello";
            }
        }
        catch
        {
            WelcomeNameLabel.Text = "Hello";
        }
    }

    private async Task LoadCurrentModeAsync()
    {
        if (_showingRequests)
            await LoadRequestsAsync();
        else
            await LoadServicesAsync();
    }

    private async Task LoadRequestsAsync()
    {
        if (_isLoading)
        {
            RequestsRefreshView.IsRefreshing = false;
            return;
        }

        try
        {
            SetBusy(true);

            var items = (await AppServices.Db.GetRequestsAsync())
                .Where(x =>
                    (x.Status == "Available" ||
                     x.Status == "Negotiating") &&
                    IsUpcoming(x))
                .ToList();

            // Load uploaded photos and fall back to local images.
            var cards = await Task.WhenAll(
                items.Select(async item => new RequestCard
                {
                    Request = item,
                    ImageSource = await LoadPhotoAsync(
                        $"requestPhotos/{item.RequestId}",
                        item.ImageSource,
                        "laptop_request.jpg")
                }));

            _allRequests = cards.ToList();

            ApplyRequestFilter(_selectedCategory);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to load requests",
                ex.Message,
                "OK");
        }
        finally
        {
            RequestsRefreshView.IsRefreshing = false;
            SetBusy(false);
        }
    }

    private async Task LoadServicesAsync()
    {
        if (_isLoading)
        {
            ServicesRefreshView.IsRefreshing = false;
            return;
        }

        try
        {
            SetBusy(true);

            var items = await AppServices.Db.GetServicesAsync();

            var cards = await Task.WhenAll(
                items.Select(async item => new ServiceCard
                {
                    Service = item,
                    ImageSource = await LoadPhotoAsync(
                        $"servicePhotos/{item.ServiceId}",
                        item.ImageSource,
                        "laptop_repair.jpg")
                }));

            Replace(_services, cards);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to load services",
                ex.Message,
                "OK");
        }
        finally
        {
            ServicesRefreshView.IsRefreshing = false;
            SetBusy(false);
        }
    }

    // Load a photo stored separately in Firebase Realtime Database.
    // If no uploaded photo exists, return the original local image.
    private static async Task<ImageSource> LoadPhotoAsync(
        string databasePath,
        string? fallbackFile,
        string defaultFile)
    {
        ImageSource fallback = ImageSource.FromFile(
            string.IsNullOrWhiteSpace(fallbackFile)
                ? defaultFile
                : fallbackFile);

        try
        {
            var photo = await AppServices.Db
                .GetAsync<CoreSwapPhotoRecord>(databasePath);

            if (photo is null ||
                string.IsNullOrWhiteSpace(photo.Base64))
            {
                return fallback;
            }

            if (!string.Equals(
                photo.ContentType,
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            // Avoid decoding unexpectedly large database records.
            if (photo.Base64.Length > 5_600_000)
                return fallback;

            byte[] bytes = Convert.FromBase64String(photo.Base64);

            if (bytes.Length == 0)
                return fallback;

            return ImageSource.FromStream(
                () => new MemoryStream(bytes, writable: false));
        }
        catch
        {
            // A missing or inaccessible photo should not hide the card.
            return fallback;
        }
    }

    private static bool IsUpcoming(RequestItem item)
    {
        if (string.IsNullOrWhiteSpace(item.PreferredDate))
            return true;

        if (!ScheduleValidator.TryParseDate(
            item.PreferredDate, out var day))
            return false;

        if (day.Date < DateTime.Today)
            return false;

        if (TimeSpan.TryParse(item.PreferredTime, out var time))
            return ScheduleValidator.IsFuture(day, time);

        return day.Date > DateTime.Today;
    }

    private void ApplyRequestFilter(string category)
    {
        _selectedCategory = category;

        var items = category == "All"
            ? _allRequests
            : _allRequests
                .Where(x => x.Category == category)
                .ToList();

        Replace(_requests, items);
    }

    private async void OnRequestsTabClicked(
        object sender, EventArgs e)
    {
        _showingRequests = true;

        RequestsRefreshView.IsVisible = true;
        ServicesRefreshView.IsVisible = false;
        FilterBar.IsVisible = true;

        RequestsTabButton.Style =
            (Style)Application.Current!
                .Resources["PrimaryButton"];

        ServicesTabButton.Style =
            (Style)Application.Current!
                .Resources["OutlineButton"];

        await LoadRequestsAsync();
    }

    private async void OnServicesTabClicked(
        object sender, EventArgs e)
    {
        _showingRequests = false;

        RequestsRefreshView.IsVisible = false;
        ServicesRefreshView.IsVisible = true;
        FilterBar.IsVisible = false;

        RequestsTabButton.Style =
            (Style)Application.Current!
                .Resources["OutlineButton"];

        ServicesTabButton.Style =
            (Style)Application.Current!
                .Resources["PrimaryButton"];

        await LoadServicesAsync();
    }

    private async void OnCreateClicked(
        object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(
            _showingRequests
                ? nameof(CreateRequestPage)
                : nameof(CreateServicePage));
    }

    private async void OnRequestViewClicked(
        object sender, EventArgs e)
    {
        if (sender is Button
            { CommandParameter: RequestItem request })
        {
            await Shell.Current.GoToAsync(
                nameof(RequestDetailsPage),
                new Dictionary<string, object>
                {
                    ["Request"] = request
                });
        }
    }

    private async void OnServiceViewClicked(
        object sender, EventArgs e)
    {
        if (sender is Button
            { CommandParameter: ServiceItem service })
        {
            await Shell.Current.GoToAsync(
                nameof(ServiceDetailsPage),
                new Dictionary<string, object>
                {
                    ["Service"] = service
                });
        }
    }

    private async void OnRequestsRefreshing(
        object sender, EventArgs e)
    {
        await LoadRequestsAsync();
    }

    private async void OnServicesRefreshing(
        object sender, EventArgs e)
    {
        await LoadServicesAsync();
    }

    private void OnAllFilterClicked(
        object sender, EventArgs e)
        => ApplyRequestFilter("All");

    private void OnHardwareFilterClicked(
        object sender, EventArgs e)
        => ApplyRequestFilter("Hardware Repair");

    private void OnSwapFilterClicked(
        object sender, EventArgs e)
        => ApplyRequestFilter("Component Swap");

    private void OnToolFilterClicked(
        object sender, EventArgs e)
        => ApplyRequestFilter("Tool Loan");

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source)
    {
        target.Clear();

        foreach (var item in source)
            target.Add(item);
    }

    private void SetBusy(bool busy)
    {
        _isLoading = busy;
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
    }


    private async void OnNotificationsTapped(
        object sender, TappedEventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(
                "//NotificationsPage");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to open notifications",
                ex.Message,
                "OK");
        }
    }

}
