
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreSwap.Models;
using CoreSwap.Services;

namespace CoreSwap.Pages;

public partial class AboutPage : ContentPage
{
    private bool _isBusy;
    private bool _isLoaded;
    private string _userId = string.Empty;
    private string _savedName = string.Empty;
    private string _savedPhone = string.Empty;

    public AboutPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadProfileAsync();
    }

    private async Task LoadProfileAsync()
    {
        if (_isBusy)
            return;

        try
        {
            SetBusy(true);

            _userId =
                await AppServices.Session.GetUserIdAsync()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_userId))
            {
                await DisplayAlert(
                    "Session expired",
                    "Please sign in again.",
                    "OK");
                return;
            }

            var user = await AppServices.Db.GetAsync<AppUser>(
                $"users/{_userId}");

            if (user is null || user.UserId != _userId)
                throw new Exception(
                    "Your user profile could not be found.");

            var privateProfile =
                await AppServices.Db.GetAsync<PrivateUserProfile>(
                    $"privateProfiles/{_userId}");

            _savedName = user.FullName;
            _savedPhone = privateProfile?.PhoneNumber
                          ?? string.Empty;

            FullNameEntry.Text = _savedName;
            PhoneEntry.Text = _savedPhone;

            // The email is displayed only, never edited.
            EmailLabel.Text = user.StudentEmail;

            HeaderNameLabel.Text = _savedName;

            _isLoaded = true;
        }
        catch (Exception ex)
        {
            _isLoaded = false;

            await DisplayAlert(
                "Unable to load profile",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSaveClicked(
        object sender, EventArgs e)
    {
        if (_isBusy || !_isLoaded)
            return;

        var fullName =
            FullNameEntry.Text?.Trim() ?? string.Empty;

        var phone =
            PhoneEntry.Text?.Trim() ?? string.Empty;

        if (fullName.Length < 2 || fullName.Length > 80)
        {
            await DisplayAlert(
                "Full Name",
                "Please enter a name between 2 and 80 characters.",
                "OK");
            return;
        }

        if (!IsValidPhone(phone))
        {
            await DisplayAlert(
                "Phone Number",
                "Enter a valid phone number, or leave it empty.",
                "OK");
            return;
        }

        try
        {
            SetBusy(true);

            var currentUid =
                await AppServices.Session.GetUserIdAsync();

            if (string.IsNullOrWhiteSpace(currentUid) ||
                currentUid != _userId)
            {
                throw new Exception(
                    "Your session has changed. Please sign in again.");
            }

            var user = await AppServices.Db.GetAsync<AppUser>(
                $"users/{_userId}");

            if (user is null || user.UserId != currentUid)
                throw new Exception(
                    "Your profile is no longer available.");

            // Phone is stored under its separate private path.
            // Firebase security rules must restrict this path.
            if (phone != _savedPhone)
            {
                await AppServices.Db.PutAsync(
                    $"privateProfiles/{_userId}",
                    new PrivateUserProfile
                    {
                        UserId = _userId,
                        PhoneNumber = phone,
                        UpdatedAtUtc = DateTime.UtcNow
                    });

                _savedPhone = phone;
            }

            if (fullName != _savedName)
            {
                // Do not overwrite email or other account fields.
                await AppServices.Db.PatchAsync(
                    $"users/{_userId}",
                    new Dictionary<string, object?>
                    {
                        ["FullName"] = fullName
                    });

                await AppServices.Session
                    .UpdateFullNameAsync(fullName);

                _savedName = fullName;
            }

            HeaderNameLabel.Text = _savedName;

            await DisplayAlert(
                "Profile Updated",
                "Your changes were saved successfully.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to save profile",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static bool IsValidPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return true;

        if (phone.Length > 25)
            return false;

        if (phone.Any(c =>
            !char.IsDigit(c) &&
            c != '+' &&
            c != ' ' &&
            c != '-' &&
            c != '(' &&
            c != ')'))
            return false;

        if (phone.Count(c => c == '+') > 1 ||
            (phone.Contains('+') && phone[0] != '+'))
            return false;

        int digits = phone.Count(char.IsDigit);

        return digits >= 7 && digits <= 15;
    }

    private async void OnLogoutClicked(
        object sender, EventArgs e)
    {
        if (_isBusy)
            return;

        var confirm = await DisplayAlert(
            "Log out?",
            "Do you want to sign out of CoreSwap?",
            "Log Out",
            "Cancel");

        if (!confirm)
            return;

        AppServices.Auth.Logout();

        Application.Current!.MainPage =
            new NavigationPage(new LoginPage());
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        SaveButton.IsEnabled = !busy;
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
    }
}
