
using CoreSwap.Pages;

namespace CoreSwap;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(
            nameof(CreateRequestPage),
            typeof(CreateRequestPage));

        Routing.RegisterRoute(
            nameof(RequestDetailsPage),
            typeof(RequestDetailsPage));

        Routing.RegisterRoute(
            nameof(CreateServicePage),
            typeof(CreateServicePage));

        Routing.RegisterRoute(
            nameof(ServiceDetailsPage),
            typeof(ServiceDetailsPage));

        Routing.RegisterRoute(
            nameof(ChatPage),
            typeof(ChatPage));
    }
}
