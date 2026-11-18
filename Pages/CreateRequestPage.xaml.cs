using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CoreSwap.Models;
using CoreSwap.Services;

namespace CoreSwap.Pages;

public partial class CreateRequestPage : ContentPage, IQueryAttributable
{
    private string _targetProviderId = string.Empty;
    private RequestItem? _editingRequest;
    private bool _isSubmitting;
    private bool _removeStoredPhoto;
    private byte[]? _selectedPhoto;

    public CreateRequestPage()
    {
        InitializeComponent();
        PreferredDatePicker.MinimumDate = DateTime.Today;
        var suggested = DateTime.Now.AddHours(1);
        PreferredDatePicker.Date = suggested.Date;
        PreferredTimePicker.Time = suggested.TimeOfDay;
        OnPricingChanged(this, EventArgs.Empty);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("TargetProviderId", out var provider))
            _targetProviderId = provider?.ToString() ?? string.Empty;

        if (query.TryGetValue("Category", out var category))
            SetCategory(category?.ToString() ?? string.Empty);

        if (query.TryGetValue("EditRequest", out var value) && value is RequestItem request)
        {
            _editingRequest = request;
            Title = "Edit Request";
            PageHeading.Text = "Edit Request";
            SubmitButton.Text = "Save Changes";
            TitleEntry.Text = request.Title;
            SetCategory(request.Category);
            DescriptionEditor.Text = request.Description;
            PricingPicker.SelectedIndex = PricingPicker.Items.IndexOf(request.PricingType);
            BudgetEntry.Text = request.Budget?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
            if (ScheduleValidator.TryParseDate(request.PreferredDate, out var date)
                && date.Date >= DateTime.Today)
                PreferredDatePicker.Date = date;
            if (TimeSpan.TryParse(request.PreferredTime, out var time))
                PreferredTimePicker.Time = time;
            _targetProviderId = request.TargetProviderId;
            OnPricingChanged(this, EventArgs.Empty);
            _ = LoadExistingPhotoAsync(request.RequestId);
        }
    }

    private async Task LoadExistingPhotoAsync(string id)
    {
        try
        {
            var record = await AppServices.Db.GetAsync<CoreSwapPhotoRecord>($"requestPhotos/{id}");
            if (record is null || _selectedPhoto is not null || _removeStoredPhoto) return;
            if (string.IsNullOrWhiteSpace(record.Base64)) return;
            var bytes = Convert.FromBase64String(record.Base64);
            ShowPreview(bytes);
        }
        catch
        {
            // Keep the form editable if an old photo cannot be loaded.
        }
    }

    private void SetCategory(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var index = CategoryPicker.Items.IndexOf(value);
        if (index >= 0)
        {
            CategoryPicker.SelectedIndex = index;
        }
        else
        {
            CategoryPicker.SelectedIndex = CategoryPicker.Items.IndexOf("Other");
            OtherCategoryEntry.Text = value;
        }
        OnCategoryChanged(this, EventArgs.Empty);
    }

    private void OnCategoryChanged(object sender, EventArgs e)
    {
        OtherCategoryPanel.IsVisible = CategoryPicker.SelectedItem?.ToString() == "Other";
    }

    private void OnPricingChanged(object sender, EventArgs e)
    {
        if (BudgetPanel is null) return;
        BudgetPanel.IsVisible = PricingPicker.SelectedItem?.ToString() != "Free";
    }

    private async void OnChoosePhotoClicked(object sender, EventArgs e)
    {
        if (_isSubmitting) return;
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();
            if (photo is null) return;
            await using var input = await photo.OpenReadAsync();
            using var output = new MemoryStream();
            await input.CopyToAsync(output);
            var bytes = output.ToArray();
            if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024)
            {
                await DisplayAlert("Photo size", "Please choose a photo smaller than 4 MB.", "OK");
                return;
            }
            _selectedPhoto = bytes;
            _removeStoredPhoto = false;
            ShowPreview(bytes);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Unable to choose photo", ex.Message, "OK");
        }
    }

    private void ShowPreview(byte[] bytes)
    {
        PhotoPreview.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        PhotoPreview.IsVisible = true;
        PhotoPlaceholder.IsVisible = false;
        RemovePhotoButton.IsEnabled = true;
    }

    private void OnRemovePhotoClicked(object sender, EventArgs e)
    {
        if (_isSubmitting) return;
        _selectedPhoto = null;
        _removeStoredPhoto = true;
        PhotoPreview.Source = null;
        PhotoPreview.IsVisible = false;
        PhotoPlaceholder.IsVisible = true;
        RemovePhotoButton.IsEnabled = false;
    }

    private async void OnSubmitClicked(object sender, EventArgs e)
    {
        if (_isSubmitting) return;
        var title = TitleEntry.Text?.Trim() ?? string.Empty;
        var selectedCategory = CategoryPicker.SelectedItem?.ToString() ?? string.Empty;
        var category = selectedCategory == "Other"
            ? OtherCategoryEntry.Text?.Trim() ?? string.Empty
            : selectedCategory;
        var description = DescriptionEditor.Text?.Trim() ?? string.Empty;
        var pricingType = PricingPicker.SelectedItem?.ToString() ?? "Negotiable";
        var date = PreferredDatePicker.Date;
        var time = PreferredTimePicker.Time;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(category) ||
            string.IsNullOrWhiteSpace(description))
        {
            await DisplayAlert("Required fields", "Title, category and description are required.", "OK");
            return;
        }
        if (category.Contains('/') || category.Contains('.'))
        {
            await DisplayAlert("Category", "Please avoid / and . in custom category names.", "OK");
            return;
        }
        if (!ScheduleValidator.IsFuture(date, time))
        {
            await DisplayAlert("Date", "Choose a future date and time.", "OK");
            return;
        }

        decimal? budget = null;
        if (pricingType != "Free" && !string.IsNullOrWhiteSpace(BudgetEntry.Text))
        {
            if (!decimal.TryParse(BudgetEntry.Text, out var parsed) || parsed < 0)
            {
                await DisplayAlert("Budget", "Enter a valid budget amount.", "OK");
                return;
            }
            budget = parsed;
        }
        if (pricingType == "Fixed Budget" && (!budget.HasValue || budget.Value <= 0))
        {
            await DisplayAlert("Budget", "Enter a positive amount for a fixed budget.", "OK");
            return;
        }

        try
        {
            SetBusy(true);
            var uid = await AppServices.Session.GetUserIdAsync();
            if (string.IsNullOrWhiteSpace(uid))
                throw new InvalidOperationException("Please sign in before submitting a request.");
            var name = await AppServices.Session.GetFullNameAsync() ?? "Student";
            string requestId;

            if (_editingRequest is not null)
            {
                requestId = _editingRequest.RequestId;
                var latest = await AppServices.Db.GetAsync<RequestItem>($"requests/{requestId}");
                if (latest is null || latest.OwnerId != uid ||
                    (latest.Status != "Available" && latest.Status != "Negotiating"))
                    throw new InvalidOperationException("This request has changed and can no longer be edited.");

                await AppServices.Db.PatchAsync($"requests/{requestId}", new Dictionary<string, object?>
                {
                    ["Title"] = title,
                    ["Category"] = category,
                    ["Description"] = description,
                    ["PricingType"] = pricingType,
                    ["Budget"] = budget,
                    ["PreferredDate"] = ScheduleValidator.ToDateString(date),
                    ["PreferredTime"] = ScheduleValidator.ToTimeString(time),
                    ["ImageSource"] = ImageForCategory(category),
                    ["UpdatedAtUtc"] = DateTime.UtcNow
                });
            }
            else
            {
                requestId = Guid.NewGuid().ToString("N");
                var item = new RequestItem
                {
                    RequestId = requestId,
                    OwnerId = uid,
                    OwnerName = name,
                    Title = title,
                    Category = category,
                    Description = description,
                    PricingType = pricingType,
                    Budget = budget,
                    PreferredDate = ScheduleValidator.ToDateString(date),
                    PreferredTime = ScheduleValidator.ToTimeString(time),
                    TargetProviderId = _targetProviderId,
                    Status = "Available",
                    ImageSource = ImageForCategory(category),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                await AppServices.Db.PutAsync($"requests/{requestId}", item);
            }

            string? photoWarning = null;
            try
            {
                if (_selectedPhoto is not null)
                {
                    // Store selected photo in Firebase Realtime Database.
                    // Home/Details image bindings still need a follow-up change to display it.
                    await AppServices.Db.PutAsync($"requestPhotos/{requestId}", new CoreSwapPhotoRecord
                    {
                        Base64 = Convert.ToBase64String(_selectedPhoto),
                        ContentType = "image/jpeg",
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                }
                else if (_removeStoredPhoto && _editingRequest is not null)
                {
                    await AppServices.Db.DeleteAsync($"requestPhotos/{requestId}");
                }
            }
            catch
            {
                photoWarning = "Your request was saved, but the photo could not be saved. Check Firebase permissions.";
            }

            await DisplayAlert(photoWarning is null ? "Saved" : "Photo warning",
                photoWarning ?? (_editingRequest is null ? "Request posted successfully." : "Request updated successfully."), "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Unable to save request", ex.Message, "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string ImageForCategory(string category) => category switch
    {
        "Component Swap" => "ram_ddr4.jpg",
        "Tool Loan" => "tool_kit.jpg",
        _ => "laptop_request.jpg"
    };

    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (_isSubmitting) return;
        bool edited = _editingRequest is not null || _selectedPhoto is not null ||
            _removeStoredPhoto || !string.IsNullOrWhiteSpace(TitleEntry.Text) ||
            !string.IsNullOrWhiteSpace(DescriptionEditor.Text) || CategoryPicker.SelectedIndex >= 0;
        if (edited && !await DisplayAlert("Leave this page?",
                "Any unsaved changes will be lost.", "Leave", "Stay")) return;
        await Shell.Current.GoToAsync("..");
    }

    private void SetBusy(bool busy)
    {
        _isSubmitting = busy;
        SubmitButton.IsEnabled = !busy;
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
    }
}

// Shared photo record for Create Request and Create Service.
// Photos are stored separately, without changing the existing model classes.
public sealed class CoreSwapPhotoRecord
{
    public string Base64 { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public DateTime UpdatedAtUtc { get; set; }
}
