using CoreSwap.Pages;

namespace CoreSwap;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new NavigationPage(new SplashPage());
    }
}
