
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CoreSwap.Models;
using CoreSwap.Services;

namespace CoreSwap.Pages;

public partial class ServiceDetailsPage : ContentPage, IQueryAttributable
{
    private ServiceItem? _service;
    private bool _isOwner;
    private bool _isBusy;
    private string _currentUserId = string.Empty;

    public ObservableCollection<ServiceOfferItem> ServiceOffers
    { get; } = new();

    public ServiceDetailsPage()
    {
        InitializeComponent();

        BindingContext = this;

        OfferDatePicker.MinimumDate = DateTime.Today;

        var suggested = DateTime.Now.AddHours(1);
        OfferDatePicker.Date = suggested.Date;
        OfferTimePicker.Time = suggested.TimeOfDay;
    }

    public async void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (!query.TryGetValue("Service", out var value) ||
            value is not ServiceItem service)
            return;

        _service = service;
        await SafeRenderAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_service is not null)
            await SafeRenderAsync();
    }

    private async Task SafeRenderAsync()
    {
        if (_isBusy)
            return;

        try
        {
            SetBusy(true);
            await RenderAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to load service",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RenderAsync()
    {
        if (_service is null)
            return;

        var fresh = await AppServices.Db.GetAsync<ServiceItem>(
            $"services/{_service.ServiceId}");

        if (fresh is null)
        {
            await DisplayAlert(
                "Service unavailable",
                "This service no longer exists.",
                "OK");
            return;
        }

        _service = fresh;

        _currentUserId =
            await AppServices.Session.GetUserIdAsync()
            ?? string.Empty;

        _isOwner =
            !string.IsNullOrWhiteSpace(_currentUserId) &&
            _service.OwnerId == _currentUserId;

        // Uploaded photo from Firebase.
        ServiceImage.Source = await FirebasePhotoLoader.LoadAsync(
            $"servicePhotos/{_service.ServiceId}",
            _service.ImageSource,
            "laptop_repair.jpg");

        CategoryLabel.Text = _service.Category;
        TitleLabel.Text = _service.Title;

        ProviderLabel.Text = _isOwner
            ? "Your service"
            : $"Offered by {_service.OwnerName}";

        PriceLabel.Text = _service.PriceText;

        AvailabilityLabel.Text =
            $"Availability: {_service.Availability}";

        DescriptionLabel.Text = _service.Description;

        OwnerPanel.IsVisible = _isOwner;

        // Read offers associated with this service.
        var allOffers = await GetOffersAsync();

        var visibleOffers = _isOwner
            ? allOffers
            : allOffers.Where(x =>
                x.OfferedById == _currentUserId).ToList();

        ServiceOffers.Clear();

        foreach (var offer in visibleOffers
                     .OrderByDescending(x => x.UpdatedAtUtc))
        {
            offer.CanOwnerRespond =
                _isOwner &&
                _service.Status == "Active" &&
                offer.Status == "Pending";

            offer.CanCustomerAcceptCounter =
                !_isOwner &&
                _service.Status == "Active" &&
                offer.OfferedById == _currentUserId &&
                offer.Status == "Countered";

            offer.CanCustomerWithdraw =
                !_isOwner &&
                offer.OfferedById == _currentUserId &&
                (offer.Status == "Pending" ||
                 offer.Status == "Countered");

            offer.CanChat =
                offer.Status == "Accepted" &&
                (_isOwner ||
                 offer.OfferedById == _currentUserId);

            ServiceOffers.Add(offer);
        }

        NoOffersLabel.IsVisible = ServiceOffers.Count == 0;

        OffersHeading.Text = _isOwner
            ? "Received Offers"
            : "Your Offers";

        bool alreadyHasActiveOffer = allOffers.Any(x =>
            x.OfferedById == _currentUserId &&
            (x.Status == "Pending" ||
             x.Status == "Countered" ||
             x.Status == "Accepted"));

        bool alreadyAccepted = allOffers.Any(
            x => x.Status == "Accepted");

        ViewerPanel.IsVisible =
            !_isOwner &&
            !string.IsNullOrWhiteSpace(_currentUserId) &&
            _service.Status == "Active" &&
            !alreadyHasActiveOffer &&
            !alreadyAccepted;
    }

    private async Task<List<ServiceOfferItem>> GetOffersAsync()
    {
        if (_service is null)
            return new List<ServiceOfferItem>();

        var offers = await AppServices.Db
            .GetAllAsync<ServiceOfferItem>("serviceOffers");

        return offers
            .Where(x => x.ServiceId == _service.ServiceId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (OfferOverlay.IsVisible)
        {
            OfferOverlay.IsVisible = false;
            return;
        }

        if (_isBusy)
            return;

        await Shell.Current.GoToAsync("..");
    }

    private async void OnRequestServiceClicked(
        object sender, EventArgs e)
    {
        if (_service is null || _isOwner || _isBusy)
            return;

        try
        {
            var fresh = await AppServices.Db
                .GetAsync<ServiceItem>(
                    $"services/{_service.ServiceId}");

            if (fresh is null || fresh.Status != "Active")
            {
                await DisplayAlert(
                    "Unavailable",
                    "This service is no longer active.",
                    "OK");

                await SafeRenderAsync();
                return;
            }

            var offers = await GetOffersAsync();

            if (offers.Any(x =>
                    x.Status == "Accepted" ||
                    (x.OfferedById == _currentUserId &&
                     (x.Status == "Pending" ||
                      x.Status == "Countered"))))
            {
                await DisplayAlert(
                    "Offer unavailable",
                    "An active or accepted offer already exists.",
                    "OK");

                await SafeRenderAsync();
                return;
            }

            OfferPriceEntry.Text =
                (fresh.Price ?? 0m).ToString(
                    "0.##", CultureInfo.CurrentCulture);

            var suggested = DateTime.Now.AddHours(1);

            OfferDatePicker.MinimumDate = DateTime.Today;
            OfferDatePicker.Date = suggested.Date;
            OfferTimePicker.Time = suggested.TimeOfDay;

            OfferMessageEditor.Text = string.Empty;
            OfferOverlay.IsVisible = true;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to start negotiation",
                ex.Message,
                "OK");
        }
    }

    private void OnOfferCancelClicked(
        object sender, EventArgs e)
    {
        if (!_isBusy)
            OfferOverlay.IsVisible = false;
    }

    private async void OnSubmitServiceOfferClicked(
        object sender, EventArgs e)
    {
        if (_isBusy || _service is null)
            return;

        if (!decimal.TryParse(
                OfferPriceEntry.Text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var price) || price < 0)
        {
            await DisplayAlert(
                "Price",
                "Please enter a valid price.",
                "OK");
            return;
        }

        if (!ScheduleValidator.IsFuture(
                OfferDatePicker.Date,
                OfferTimePicker.Time))
        {
            await DisplayAlert(
                "Date",
                "Choose a future date and time.",
                "OK");
            return;
        }

        var message =
            OfferMessageEditor.Text?.Trim() ?? string.Empty;

        try
        {
            SetBusy(true);

            var uid =
                await AppServices.Session.GetUserIdAsync();

            if (string.IsNullOrWhiteSpace(uid))
                throw new Exception("Please sign in first.");

            var fresh = await AppServices.Db
                .GetAsync<ServiceItem>(
                    $"services/{_service.ServiceId}");

            if (fresh is null || fresh.Status != "Active")
                throw new Exception(
                    "This service is no longer active.");

            if (fresh.OwnerId == uid)
                throw new Exception(
                    "You cannot request your own service.");

            var existing = await GetOffersAsync();

            if (existing.Any(x =>
                    x.Status == "Accepted" ||
                    (x.OfferedById == uid &&
                     (x.Status == "Pending" ||
                      x.Status == "Countered"))))
            {
                throw new Exception(
                    "An active or accepted offer already exists.");
            }

            var offerId = Guid.NewGuid().ToString("N");

            var offer = new ServiceOfferItem
            {
                OfferId = offerId,
                ServiceId = fresh.ServiceId,
                ServiceOwnerId = fresh.OwnerId,
                OfferedById = uid,
                OfferedByName =
                    await AppServices.Session.GetFullNameAsync()
                    ?? "Student",
                ProposedPrice = price,
                ProposedDate = ScheduleValidator.ToDateString(
                    OfferDatePicker.Date),
                ProposedTime = ScheduleValidator.ToTimeString(
                    OfferTimePicker.Time),
                Message = message,
                Status = "Pending",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await AppServices.Db.PutAsync(
                $"serviceOffers/{offerId}",
                offer);

            OfferOverlay.IsVisible = false;

            await DisplayAlert(
                "Offer sent",
                "Your offer was sent to the service provider.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to send offer",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }

        await SafeRenderAsync();
    }

    private async Task ChangeOfferStatusAsync(
        ServiceOfferItem offer,
        string requiredStatus,
        string nextStatus,
        bool providerAction)
    {
        if (_service is null || _isBusy)
            return;

        try
        {
            SetBusy(true);

            var uid =
                await AppServices.Session.GetUserIdAsync();

            var service = await AppServices.Db
                .GetAsync<ServiceItem>(
                    $"services/{_service.ServiceId}");

            var latest = await AppServices.Db
                .GetAsync<ServiceOfferItem>(
                    $"serviceOffers/{offer.OfferId}");

            if (service is null || latest is null ||
                latest.ServiceId != _service.ServiceId ||
                latest.Status != requiredStatus)
            {
                throw new Exception(
                    "This offer has changed. Please refresh.");
            }

            bool allowed = providerAction
                ? uid == service.OwnerId
                : uid == latest.OfferedById;

            if (!allowed)
                throw new Exception(
                    "You cannot change this offer.");

            if (providerAction && service.Status != "Active")
                throw new Exception(
                    "The service is not active.");

            if (nextStatus == "Accepted")
            {
                var allOffers = await GetOffersAsync();

                if (allOffers.Any(x =>
                    x.OfferId != offer.OfferId &&
                    x.Status == "Accepted"))
                {
                    throw new Exception(
                        "Another offer has already been accepted.");
                }
            }

            await AppServices.Db.PatchAsync(
                $"serviceOffers/{offer.OfferId}",
                new Dictionary<string, object?>
                {
                    ["Status"] = nextStatus,
                    ["UpdatedAtUtc"] = DateTime.UtcNow
                });

            await DisplayAlert(
                "Updated",
                $"Offer status: {nextStatus}",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to update offer",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }

        await SafeRenderAsync();
    }

    private async void OnAcceptServiceOfferClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ServiceOfferItem offer })
            return;

        var confirmed = await DisplayAlert(
            "Accept offer?",
            $"Accept {offer.OfferedByName}'s offer for RM{offer.ProposedPrice:0.##}?",
            "Accept",
            "Cancel");

        if (!confirmed)
            return;

        await ChangeOfferStatusAsync(
            offer, "Pending", "Accepted", true);
    }

    private async void OnDeclineServiceOfferClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ServiceOfferItem offer })
            return;

        await ChangeOfferStatusAsync(
            offer, "Pending", "Declined", true);
    }

    private async void OnWithdrawServiceOfferClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ServiceOfferItem offer })
            return;

        if (offer.Status != "Pending" &&
            offer.Status != "Countered")
            return;

        await ChangeOfferStatusAsync(
            offer, offer.Status, "Withdrawn", false);
    }

    private async void OnAcceptCounterClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ServiceOfferItem offer })
            return;

        // The provider confirms the final match afterwards.
        await ChangeOfferStatusAsync(
            offer, "Countered", "Pending", false);
    }

    private async void OnCounterServiceOfferClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ServiceOfferItem offer })
            return;

        if (_isBusy || !_isOwner || _service is null)
            return;

        var result = await DisplayPromptAsync(
            "Counter Offer",
            "Enter your proposed price in RM:",
            "Send Counter",
            "Cancel",
            "e.g. 30",
            keyboard: Keyboard.Numeric,
            initialValue: offer.ProposedPrice.ToString(
                "0.##", CultureInfo.CurrentCulture));

        if (result is null)
            return;

        if (!decimal.TryParse(
                result, NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var counterPrice) || counterPrice < 0)
        {
            await DisplayAlert(
                "Invalid price",
                "Enter a valid non-negative amount.",
                "OK");
            return;
        }

        try
        {
            SetBusy(true);

            var latest = await AppServices.Db
                .GetAsync<ServiceOfferItem>(
                    $"serviceOffers/{offer.OfferId}");

            var service = await AppServices.Db
                .GetAsync<ServiceItem>(
                    $"services/{_service.ServiceId}");

            var uid =
                await AppServices.Session.GetUserIdAsync();

            if (service is null ||
                service.Status != "Active" ||
                uid != service.OwnerId ||
                latest is null ||
                latest.ServiceId != service.ServiceId ||
                latest.Status != "Pending")
            {
                throw new Exception(
                    "This offer can no longer be countered.");
            }

            await AppServices.Db.PatchAsync(
                $"serviceOffers/{offer.OfferId}",
                new Dictionary<string, object?>
                {
                    ["ProposedPrice"] = counterPrice,
                    ["Status"] = "Countered",
                    ["UpdatedAtUtc"] = DateTime.UtcNow
                });

            await DisplayAlert(
                "Counter sent",
                "The student can now accept your counter price.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to counter",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }

        await SafeRenderAsync();
    }

    private async void OnEditClicked(
        object sender, EventArgs e)
    {
        if (_service is null || !_isOwner)
            return;

        await Shell.Current.GoToAsync(
            nameof(CreateServicePage),
            new Dictionary<string, object>
            {
                ["EditService"] = _service
            });
    }

    private async void OnDisableClicked(
        object sender, EventArgs e)
    {
        if (_service is null || !_isOwner)
            return;

        try
        {
            SetBusy(true);

            await AppServices.Db.PatchAsync(
                $"services/{_service.ServiceId}",
                new Dictionary<string, object?>
                {
                    ["Status"] = "Disabled",
                    ["UpdatedAtUtc"] = DateTime.UtcNow
                });

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to disable service",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDeleteClicked(
        object sender, EventArgs e)
    {
        if (_service is null || !_isOwner)
            return;

        var confirm = await DisplayAlert(
            "Delete service?",
            "The service will be hidden from public browsing.",
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            SetBusy(true);

            await AppServices.Db.PatchAsync(
                $"services/{_service.ServiceId}",
                new Dictionary<string, object?>
                {
                    ["Status"] = "Deleted",
                    ["UpdatedAtUtc"] = DateTime.UtcNow
                });

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to delete service",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
        SubmitOfferButton.IsEnabled = !busy;
    }
}
