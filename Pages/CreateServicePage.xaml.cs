
using CoreSwap.Models;
using CoreSwap.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;


namespace CoreSwap.Pages;

public partial class CreateServicePage : ContentPage, IQueryAttributable
{
    private ServiceItem? _editingService;
    private bool _isSaving;
    private byte[]? _selectedPhoto;
    private bool _removeStoredPhoto;

    public CreateServicePage()
    {
        InitializeComponent();

        StartTimePicker.Time = new TimeSpan(9, 0, 0);
        EndTimePicker.Time = new TimeSpan(17, 0, 0);

        UpdatePriceVisibility();
    }

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (!query.TryGetValue("EditService", out var value) ||
            value is not ServiceItem service)
            return;

        _editingService = service;

        Title = "Edit Service";
        PageHeading.Text = "Edit Service";
        SaveButton.Text = "Save Changes";

        TitleEntry.Text = service.Title;
        SetCategory(service.Category);
        DescriptionEditor.Text = service.Description;

        PricingPicker.SelectedIndex =
            PricingPicker.Items.IndexOf(service.PricingType);

        PriceEntry.Text = service.Price?.ToString(
            "0.##",
            CultureInfo.CurrentCulture) ?? string.Empty;

        // Keep legacy availability untouched unless the user
        // chooses a new days/time schedule.
        LoadAvailability(service.Availability);

        UpdatePriceVisibility();

        _ = LoadExistingPhotoAsync(service.ServiceId);
    }

    private void LoadAvailability(string availability)
    {
        DaysPicker.SelectedIndex = -1;

        if (string.IsNullOrWhiteSpace(availability))
            return;

        // New format: Weekdays | 09:00 - 17:00
        var parts = availability.Split('|');

        if (parts.Length != 2)
            return;

        var days = parts[0].Trim();
        var index = DaysPicker.Items.IndexOf(days);

        if (index < 0)
            return;

        var times = parts[1].Split(
            " - ",
            StringSplitOptions.TrimEntries);

        if (times.Length != 2 ||
            !TimeSpan.TryParseExact(
                times[0], @"hh\:mm",
                CultureInfo.InvariantCulture, out var start) ||
            !TimeSpan.TryParseExact(
                times[1], @"hh\:mm",
                CultureInfo.InvariantCulture, out var end))
            return;

        DaysPicker.SelectedIndex = index;
        StartTimePicker.Time = start;
        EndTimePicker.Time = end;
    }

    private async Task LoadExistingPhotoAsync(string id)
    {
        try
        {
            var record = await AppServices.Db
                .GetAsync<CoreSwapPhotoRecord>(
                    $"servicePhotos/{id}");

            if (record is null ||
                _selectedPhoto is not null ||
                _removeStoredPhoto ||
                string.IsNullOrWhiteSpace(record.Base64))
                return;

            var bytes = Convert.FromBase64String(record.Base64);
            ShowPreview(bytes);
        }
        catch
        {
            // The form can still be edited without the preview.
        }
    }

    private void SetCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return;

        var index = CategoryPicker.Items.IndexOf(category);

        if (index >= 0)
        {
            CategoryPicker.SelectedIndex = index;
        }
        else
        {
            CategoryPicker.SelectedIndex =
                CategoryPicker.Items.IndexOf("Other");

            OtherCategoryEntry.Text = category;
        }

        OnCategoryChanged(this, EventArgs.Empty);
    }

    private void OnCategoryChanged(
        object sender, EventArgs e)
    {
        if (OtherCategoryPanel is null)
            return;

        OtherCategoryPanel.IsVisible =
            CategoryPicker.SelectedItem?.ToString() == "Other";
    }

    private void OnPricingChanged(
        object sender, EventArgs e)
    {
        UpdatePriceVisibility();
    }

    private void UpdatePriceVisibility()
    {
        if (PricePanel is null || PricingPicker is null)
            return;

        PricePanel.IsVisible =
            PricingPicker.SelectedItem?.ToString() != "Free";
    }

    private async void OnChoosePhotoClicked(
        object sender, EventArgs e)
    {
        if (_isSaving)
            return;

        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();

            if (photo is null)
                return;

            var extension =
                Path.GetExtension(photo.FileName).ToLowerInvariant();

            if (extension != ".jpg" &&
                extension != ".jpeg")
            {
                await DisplayAlert(
                    "Photo format",
                    "Please choose a JPG or JPEG photo.",
                    "OK");
                return;
            }

            await using var input = await photo.OpenReadAsync();
            using var output = new MemoryStream();

            await input.CopyToAsync(output);

            var bytes = output.ToArray();

            if (bytes.Length == 0 ||
                bytes.Length > 4 * 1024 * 1024)
            {
                await DisplayAlert(
                    "Photo size",
                    "Please choose a JPEG photo smaller than 4 MB.",
                    "OK");
                return;
            }

            _selectedPhoto = bytes;
            _removeStoredPhoto = false;

            ShowPreview(bytes);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to choose photo",
                ex.Message,
                "OK");
        }
    }

    private void ShowPreview(byte[] bytes)
    {
        PhotoPreview.Source = ImageSource.FromStream(
            () => new MemoryStream(bytes, writable: false));

        PhotoPreview.IsVisible = true;
        PhotoPlaceholder.IsVisible = false;
        RemovePhotoButton.IsEnabled = true;
    }

    private void OnRemovePhotoClicked(
        object sender, EventArgs e)
    {
        if (_isSaving)
            return;

        _selectedPhoto = null;
        _removeStoredPhoto = true;

        PhotoPreview.Source = null;
        PhotoPreview.IsVisible = false;
        PhotoPlaceholder.IsVisible = true;

        RemovePhotoButton.IsEnabled = false;
    }

    private async void OnSaveClicked(
        object sender, EventArgs e)
    {
        if (_isSaving)
            return;

        var title = TitleEntry.Text?.Trim() ?? string.Empty;

        var selectedCategory =
            CategoryPicker.SelectedItem?.ToString() ?? string.Empty;

        var category = selectedCategory == "Other"
            ? OtherCategoryEntry.Text?.Trim() ?? string.Empty
            : selectedCategory;

        var description =
            DescriptionEditor.Text?.Trim() ?? string.Empty;

        var pricingType =
            PricingPicker.SelectedItem?.ToString() ?? "Negotiable";

        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(category) ||
            string.IsNullOrWhiteSpace(description))
        {
            await DisplayAlert(
                "Required fields",
                "Title, category and description are required.",
                "OK");
            return;
        }

        if (category.Contains('/') || category.Contains('.'))
        {
            await DisplayAlert(
                "Category",
                "Please avoid / and . in custom category names.",
                "OK");
            return;
        }

        decimal? price = null;

        if (pricingType != "Free" &&
            !string.IsNullOrWhiteSpace(PriceEntry.Text))
        {
            if (!decimal.TryParse(
                PriceEntry.Text, out var parsed) ||
                parsed < 0)
            {
                await DisplayAlert(
                    "Price",
                    "Enter a valid price.",
                    "OK");
                return;
            }

            price = parsed;
        }

        if (pricingType == "Fixed Price" &&
            (!price.HasValue || price.Value <= 0))
        {
            await DisplayAlert(
                "Price",
                "Enter a positive fixed price.",
                "OK");
            return;
        }

        string availability;

        // Existing services with older free-text availability
        // retain that value unless a new day is selected.
        if (DaysPicker.SelectedIndex < 0 &&
            _editingService is not null &&
            !string.IsNullOrWhiteSpace(
                _editingService.Availability))
        {
            availability = _editingService.Availability;
        }
        else
        {
            var days =
                DaysPicker.SelectedItem?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(days))
            {
                await DisplayAlert(
                    "Availability",
                    "Please choose the available days.",
                    "OK");
                return;
            }

            var start = StartTimePicker.Time;
            var end = EndTimePicker.Time;

            if (end <= start)
            {
                await DisplayAlert(
                    "Availability",
                    "The end time must be later than the start time.",
                    "OK");
                return;
            }

            availability =
                $"{days} | {start:hh\\:mm} - {end:hh\\:mm}";
        }

        try
        {
            SetBusy(true);

            var uid = await AppServices.Session.GetUserIdAsync();

            if (string.IsNullOrWhiteSpace(uid))
            {
                throw new InvalidOperationException(
                    "Please sign in before saving a service.");
            }

            var name =
                await AppServices.Session.GetFullNameAsync()
                ?? "Student";

            string serviceId;

            if (_editingService is not null)
            {
                serviceId = _editingService.ServiceId;

                var latest = await AppServices.Db
                    .GetAsync<ServiceItem>(
                        $"services/{serviceId}");

                if (latest is null ||
                    latest.OwnerId != uid ||
                    latest.Status == "Deleted")
                {
                    throw new InvalidOperationException(
                        "This service is no longer editable.");
                }

                await AppServices.Db.PatchAsync(
                    $"services/{serviceId}",
                    new Dictionary<string, object?>
                    {
                        ["Title"] = title,
                        ["Category"] = category,
                        ["Description"] = description,
                        ["PricingType"] = pricingType,
                        ["Price"] = price,
                        ["Availability"] = availability,
                        ["ImageSource"] = ImageForCategory(category),
                        ["UpdatedAtUtc"] = DateTime.UtcNow
                    });
            }
            else
            {
                serviceId = Guid.NewGuid().ToString("N");

                var item = new ServiceItem
                {
                    ServiceId = serviceId,
                    OwnerId = uid,
                    OwnerName = name,
                    Title = title,
                    Category = category,
                    Description = description,
                    PricingType = pricingType,
                    Price = price,
                    Availability = availability,
                    Status = "Active",
                    ImageSource = ImageForCategory(category),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await AppServices.Db.PutAsync(
                    $"services/{serviceId}",
                    item);
            }

            string? photoWarning = null;

            try
            {
                if (_selectedPhoto is not null)
                {
                    await AppServices.Db.PutAsync(
                        $"servicePhotos/{serviceId}",
                        new CoreSwapPhotoRecord
                        {
                            Base64 = Convert.ToBase64String(
                                _selectedPhoto),
                            ContentType = "image/jpeg",
                            UpdatedAtUtc = DateTime.UtcNow
                        });
                }
                else if (_removeStoredPhoto &&
                         _editingService is not null)
                {
                    await AppServices.Db.DeleteAsync(
                        $"servicePhotos/{serviceId}");
                }
            }
            catch (Exception ex)
            {
                photoWarning =
                    $"The service was saved, but its photo was not saved: {ex.Message}";
            }

            await DisplayAlert(
                photoWarning is null ? "Saved" : "Photo warning",
                photoWarning ??
                    (_editingService is null
                        ? "Service published successfully."
                        : "Service updated successfully."),
                "OK");

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to save service",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string ImageForCategory(string category)
        => category switch
        {
            "Component Swap" => "ram_ddr4.jpg",
            "Tool Loan" => "tool_kit.jpg",
            _ => "laptop_repair.jpg"
        };

    private async void OnBackClicked(
        object sender, EventArgs e)
    {
        if (_isSaving)
            return;

        var edited =
            _editingService is not null ||
            _selectedPhoto is not null ||
            _removeStoredPhoto ||
            !string.IsNullOrWhiteSpace(TitleEntry.Text) ||
            !string.IsNullOrWhiteSpace(DescriptionEditor.Text) ||
            CategoryPicker.SelectedIndex >= 0 ||
            DaysPicker.SelectedIndex >= 0;

        if (edited)
        {
            var leave = await DisplayAlert(
                "Leave this page?",
                "Any unsaved changes will be lost.",
                "Leave",
                "Stay");

            if (!leave)
                return;
        }

        await Shell.Current.GoToAsync("..");
    }

    private void SetBusy(bool busy)
    {
        _isSaving = busy;
        SaveButton.IsEnabled = !busy;
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
    }
}
