using CoreSwap.Services;

namespace CoreSwap.Pages;

public partial class RegisterPage : ContentPage
{
    private bool _isBusy;
    public RegisterPage()
    {
        InitializeComponent();
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        if (_isBusy) return;
        var name = NameEntry.Text?.Trim() ?? string.Empty;
        var email = EmailEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;
        var confirm = ConfirmEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Missing information", "Complete all required fields.", "OK");
            return;
        }

        if (password.Length < 6)
        {
            await DisplayAlert("Password", "Password must be at least 6 characters.", "OK");
            return;
        }

        if (password != confirm)
        {
            await DisplayAlert("Password", "The two passwords do not match.", "OK");
            return;
        }

        try
        {
            SetBusy(true);
            await AppServices.Auth.RegisterAsync(name, email, password);
            Application.Current!.MainPage = new AppShell();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Registration failed", ex.Message, "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RegisterButton.IsEnabled = !busy;
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
    }
}
