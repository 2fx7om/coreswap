using CoreSwap.Services;

namespace CoreSwap.Pages;

public partial class LoginPage : ContentPage
{
    private bool _isBusy;
    public LoginPage()
    {
        InitializeComponent();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        if (_isBusy) return;
        var email = EmailEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Missing information", "Enter your email and password.", "OK");
            return;
        }

        try
        {
            SetBusy(true);
            await AppServices.Auth.LoginAsync(email, password);
            Application.Current!.MainPage = new AppShell();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Login failed", ex.Message, "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnForgotPasswordClicked(object sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email))
        {
            await DisplayAlert("Email required", "Enter your email first.", "OK");
            return;
        }

        try
        {
            await AppServices.Auth.SendPasswordResetAsync(email);
            await DisplayAlert("Email sent", "Check your email for the password reset link.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Unable to send email", ex.Message, "OK");
        }
    }

    private async void OnRegisterTapped(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new RegisterPage());
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        LoginButton.IsEnabled = !busy;
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
    }
}
