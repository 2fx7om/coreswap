using CoreSwap.Services;

namespace CoreSwap.Pages;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await Task.Delay(900);
        var signedIn = await AppServices.Auth.EnsureValidSessionAsync();

        Application.Current!.MainPage = signedIn
            ? new AppShell()
            : new NavigationPage(new LoginPage());
    }
}
