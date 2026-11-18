
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CoreSwap.Models;
using CoreSwap.Services;

namespace CoreSwap.Pages;

public class ActivityDisplayRow
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string RelatedId { get; set; } = "";

    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Status { get; set; } = "";

    public string FirstAction { get; set; } = "";
    public string SecondAction { get; set; } = "";
    public string ThirdAction { get; set; } = "";

    public bool CanView { get; set; } = true;

    public bool HasFirstAction =>
        !string.IsNullOrWhiteSpace(FirstAction);

    public bool HasSecondAction =>
        !string.IsNullOrWhiteSpace(SecondAction);

    public bool HasThirdAction =>
        !string.IsNullOrWhiteSpace(ThirdAction);

    public bool HasActions =>
        HasFirstAction || HasSecondAction || HasThirdAction;
}

public partial class MyActivityPage : ContentPage
{
    private readonly ObservableCollection<ActivityDisplayRow>
        _rows = new();

    private List<RequestItem> _requests = new();
    private List<OfferItem> _offers = new();
    private List<ServiceItem> _services = new();
    private List<ServiceOfferItem> _serviceOffers = new();

    private string _uid = "";
    private string _tab = "Requests";
    private string _negotiationTab = "Sent";
    private bool _busy;

    public MyActivityPage()
    {
        InitializeComponent();
        ActivityCollection.ItemsSource = _rows;
        ShowTab("Requests");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy)
            return;

        try
        {
            SetBusy(true);

            _uid = await AppServices.Session.GetUserIdAsync()
                   ?? "";

            if (string.IsNullOrWhiteSpace(_uid))
                throw new Exception("Please sign in again.");

            _requests =
                (await AppServices.Db.GetRequestsAsync())
                .ToList();

            _offers = (await AppServices.Db
                .GetAllAsync<OfferItem>("offers"))
                .ToList();

            _services = (await AppServices.Db
                .GetAllAsync<ServiceItem>("services"))
                .ToList();

            _serviceOffers = (await AppServices.Db
                .GetAllAsync<ServiceOfferItem>("serviceOffers"))
                .ToList();

            RenderRows();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to load activity",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderRows()
    {
        _rows.Clear();

        List<ActivityDisplayRow> results = new();

        if (_tab == "Requests")
        {
            results = _requests
                .Where(x => x.OwnerId == _uid)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => new ActivityDisplayRow
                {
                    Kind = "Request",
                    Id = x.RequestId,
                    Title = x.Title,
                    Subtitle = x.BudgetText,
                    Detail = x.Category,
                    Status = x.Status
                }).ToList();
        }
        else if (_tab == "Offers")
        {
            results = _offers
                .Where(x => x.OfferedById == _uid)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => new ActivityDisplayRow
                {
                    Kind = "RequestOffer",
                    Id = x.RequestId,
                    Title = "Request Offer",
                    Subtitle = x.Summary,
                    Detail = "Offer on a student's request",
                    Status = x.Status
                }).ToList();
        }
        else if (_tab == "Services")
        {
            results = _services
                .Where(x =>
                    x.OwnerId == _uid &&
                    x.Status != "Deleted")
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => new ActivityDisplayRow
                {
                    Kind = "Service",
                    Id = x.ServiceId,
                    Title = x.Title,
                    Subtitle = x.PriceText,
                    Detail = x.Availability,
                    Status = x.Status
                }).ToList();
        }
        else if (_tab == "Matches")
        {
            var acceptedRequestIds = _offers
                .Where(x =>
                    x.OfferedById == _uid &&
                    x.Status == "Accepted")
                .Select(x => x.RequestId)
                .ToHashSet();

            results = _requests
                .Where(x =>
                    (x.OwnerId == _uid &&
                     (x.Status == "Accepted" ||
                      x.Status == "Completed")) ||
                    acceptedRequestIds.Contains(x.RequestId))
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => new ActivityDisplayRow
                {
                    Kind = "Request",
                    Id = x.RequestId,
                    Title = x.Title,
                    Subtitle = x.BudgetText,
                    Detail = "Matched request",
                    Status = x.Status
                }).ToList();
        }
        else if (_tab == "Negotiations")
        {
            var myServiceIds = _services
                .Where(x => x.OwnerId == _uid)
                .Select(x => x.ServiceId)
                .ToHashSet();

            IEnumerable<ServiceOfferItem> filtered =
                _negotiationTab == "Received"
                    ? _serviceOffers.Where(
                        x => myServiceIds.Contains(x.ServiceId))
                    : _serviceOffers.Where(
                        x => x.OfferedById == _uid);

            results = filtered
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(CreateNegotiationRow)
                .ToList();
        }

        foreach (var row in results)
            _rows.Add(row);
    }

    private ActivityDisplayRow CreateNegotiationRow(
        ServiceOfferItem offer)
    {
        var service = _services.FirstOrDefault(
            x => x.ServiceId == offer.ServiceId);

        var row = new ActivityDisplayRow
        {
            Kind = "ServiceOffer",
            Id = offer.OfferId,
            RelatedId = offer.ServiceId,
            Title = service?.Title ?? "Service",
            Subtitle =
                $"RM{offer.ProposedPrice:0.##} • {offer.ProposedDate} {offer.ProposedTime}",
            Detail = _negotiationTab == "Received"
                ? $"From {offer.OfferedByName} • {offer.Message}"
                : offer.Message,
            Status = offer.Status
        };

        if (offer.Status == "Accepted")
        {
            row.FirstAction = "Chat";
        }
        else if (_negotiationTab == "Received" &&
                 offer.Status == "Pending" &&
                 service?.Status == "Active")
        {
            row.FirstAction = "Accept";
            row.SecondAction = "Counter";
            row.ThirdAction = "Decline";
        }
        else if (_negotiationTab == "Sent" &&
                 offer.Status == "Countered" &&
                 service?.Status == "Active")
        {
            row.FirstAction = "Confirm Counter";
            row.SecondAction = "Withdraw";
        }
        else if (_negotiationTab == "Sent" &&
                 offer.Status == "Pending")
        {
            row.SecondAction = "Withdraw";
        }

        return row;
    }

    private void ShowTab(string tab)
    {
        _tab = tab;

        NegotiationTabs.IsVisible = tab == "Negotiations";

        StyleTab(RequestsButton, tab == "Requests");
        StyleTab(OffersButton, tab == "Offers");
        StyleTab(ServicesButton, tab == "Services");
        StyleTab(MatchesButton, tab == "Matches");
        StyleTab(NegotiationsButton, tab == "Negotiations");

        StyleTab(SentButton, _negotiationTab == "Sent");
        StyleTab(ReceivedButton, _negotiationTab == "Received");

        RenderRows();
    }

    private static void StyleTab(Button button, bool active)
    {
        button.BackgroundColor = active
            ? Color.FromArgb("#176BFF")
            : Colors.White;

        button.TextColor = active
            ? Colors.White
            : Color.FromArgb("#102C57");

        button.BorderColor = Color.FromArgb("#DFE8F4");
        button.BorderWidth = active ? 0 : 1;
        button.CornerRadius = 11;
        button.FontSize = 12;
        button.HeightRequest = 43;
        button.Padding = new Thickness(13, 0);
    }

    private void OnRequestsClicked(object sender, EventArgs e)
        => ShowTab("Requests");

    private void OnOffersClicked(object sender, EventArgs e)
        => ShowTab("Offers");

    private void OnServicesClicked(object sender, EventArgs e)
        => ShowTab("Services");

    private void OnMatchesClicked(object sender, EventArgs e)
        => ShowTab("Matches");

    private void OnNegotiationsClicked(
        object sender, EventArgs e)
        => ShowTab("Negotiations");

    private void OnSentClicked(object sender, EventArgs e)
    {
        _negotiationTab = "Sent";
        ShowTab("Negotiations");
    }

    private void OnReceivedClicked(object sender, EventArgs e)
    {
        _negotiationTab = "Received";
        ShowTab("Negotiations");
    }

    private async void OnRefreshClicked(
        object sender, EventArgs e)
    {
        await LoadAsync();
    }

    private async void OnViewClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ActivityDisplayRow row })
            return;

        try
        {
            if (row.Kind == "Service" ||
                row.Kind == "ServiceOffer")
            {
                var id = row.Kind == "Service"
                    ? row.Id
                    : row.RelatedId;

                var service = await AppServices.Db
                    .GetAsync<ServiceItem>($"services/{id}");

                if (service is null)
                    throw new Exception("Service unavailable.");

                await Shell.Current.GoToAsync(
                    nameof(ServiceDetailsPage),
                    new Dictionary<string, object>
                    {
                        ["Service"] = service
                    });
            }
            else
            {
                var request = await AppServices.Db
                    .GetAsync<RequestItem>(
                        $"requests/{row.Id}");

                if (request is null)
                    throw new Exception("Request unavailable.");

                await Shell.Current.GoToAsync(
                    nameof(RequestDetailsPage),
                    new Dictionary<string, object>
                    {
                        ["Request"] = request
                    });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnFirstActionClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ActivityDisplayRow row })
            return;

        if (row.FirstAction == "Chat")
        {
            await OpenChatAsync(row.Id);
        }
        else if (row.FirstAction == "Accept")
        {
            await UpdateOfferAsync(
                row, "Pending", "Accepted", true);
        }
        else if (row.FirstAction == "Confirm Counter")
        {
            await UpdateOfferAsync(
                row, "Countered", "Pending", false);
        }
    }

    private async Task OpenChatAsync(string offerId)
    {
        try
        {
            var offer = await AppServices.Db
                .GetAsync<ServiceOfferItem>(
                    $"serviceOffers/{offerId}");

            if (offer is null ||
                offer.Status != "Accepted")
            {
                throw new Exception(
                    "Chat requires an accepted offer.");
            }

            var uid =
                await AppServices.Session.GetUserIdAsync();

            if (uid != offer.OfferedById &&
                uid != offer.ServiceOwnerId)
            {
                throw new Exception(
                    "You cannot open this conversation.");
            }

            await Shell.Current.GoToAsync(
                nameof(ChatPage),
                new Dictionary<string, object>
                {
                    ["OfferId"] = offerId
                });
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to open chat",
                ex.Message,
                "OK");
        }
    }

    private async void OnSecondActionClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ActivityDisplayRow row })
            return;

        if (row.SecondAction == "Counter")
        {
            await CounterOfferAsync(row);
        }
        else if (row.SecondAction == "Withdraw")
        {
            await UpdateOfferAsync(
                row, row.Status, "Withdrawn", false);
        }
    }

    private async void OnThirdActionClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: ActivityDisplayRow row })
            return;

        if (row.ThirdAction == "Decline")
        {
            await UpdateOfferAsync(
                row, "Pending", "Declined", true);
        }
    }

    private async Task UpdateOfferAsync(
        ActivityDisplayRow row,
        string expectedStatus,
        string nextStatus,
        bool providerAction)
    {
        if (_busy)
            return;

        bool confirmed = await DisplayAlert(
            "Confirm Action",
            $"Change offer to {nextStatus}?",
            "Confirm",
            "Cancel");

        if (!confirmed)
            return;

        try
        {
            SetBusy(true);

            var uid =
                await AppServices.Session.GetUserIdAsync();

            var offer = await AppServices.Db
                .GetAsync<ServiceOfferItem>(
                    $"serviceOffers/{row.Id}");

            if (offer is null ||
                offer.Status != expectedStatus ||
                offer.ServiceId != row.RelatedId)
            {
                throw new Exception(
                    "The offer has changed. Refresh and try again.");
            }

            var service = await AppServices.Db
                .GetAsync<ServiceItem>(
                    $"services/{offer.ServiceId}");

            if (service is null)
                throw new Exception("Service not found.");

            bool allowed = providerAction
                ? uid == service.OwnerId
                : uid == offer.OfferedById;

            if (!allowed)
                throw new Exception("Access denied.");

            if (nextStatus != "Withdrawn" &&
                service.Status != "Active")
            {
                throw new Exception("Service is not active.");
            }

            if (nextStatus == "Accepted")
            {
                var latest = await AppServices.Db
                    .GetAllAsync<ServiceOfferItem>(
                        "serviceOffers");

                if (latest.Any(x =>
                    x.ServiceId == offer.ServiceId &&
                    x.OfferId != offer.OfferId &&
                    x.Status == "Accepted"))
                {
                    throw new Exception(
                        "Another offer is already accepted.");
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
                $"Offer: {nextStatus}",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to update",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }

        await LoadAsync();
    }

    private async Task CounterOfferAsync(
        ActivityDisplayRow row)
    {
        if (_busy)
            return;

        var result = await DisplayPromptAsync(
            "Counter Offer",
            "Enter your new price in RM:",
            "Send Counter",
            "Cancel",
            "e.g. 30",
            keyboard: Keyboard.Numeric);

        if (result is null)
            return;

        if (!decimal.TryParse(
            result,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out var price) ||
            price < 0)
        {
            await DisplayAlert(
                "Invalid Price",
                "Enter a valid price.",
                "OK");
            return;
        }

        try
        {
            SetBusy(true);

            var uid =
                await AppServices.Session.GetUserIdAsync();

            var offer = await AppServices.Db
                .GetAsync<ServiceOfferItem>(
                    $"serviceOffers/{row.Id}");

            if (offer is null ||
                offer.Status != "Pending" ||
                offer.ServiceId != row.RelatedId)
            {
                throw new Exception(
                    "This offer is no longer pending.");
            }

            var service = await AppServices.Db
                .GetAsync<ServiceItem>(
                    $"services/{offer.ServiceId}");

            if (service is null ||
                service.OwnerId != uid ||
                service.Status != "Active")
            {
                throw new Exception(
                    "You cannot counter this offer.");
            }

            await AppServices.Db.PatchAsync(
                $"serviceOffers/{offer.OfferId}",
                new Dictionary<string, object?>
                {
                    ["ProposedPrice"] = price,
                    ["Status"] = "Countered",
                    ["UpdatedAtUtc"] = DateTime.UtcNow
                });

            await DisplayAlert(
                "Counter Sent",
                "Your counter offer was sent.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to send counter",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }

        await LoadAsync();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
        ActivityCollection.IsEnabled = !busy;
    }
}
