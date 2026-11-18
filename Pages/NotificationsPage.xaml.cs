
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CoreSwap.Models;
using CoreSwap.Services;

namespace CoreSwap.Pages;

public partial class NotificationsPage : ContentPage
{
    private readonly ObservableCollection<NotificationItem>
        _notifications = new();

    private bool _busy;
    private string _uid = "";
    private DateTime _lastReadUtc;

    public NotificationsPage()
    {
        InitializeComponent();
        NotificationsCollection.ItemsSource = _notifications;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private string ReadKey => $"coreswap_alerts_read_{_uid}";

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

            var savedReadTime =
                Preferences.Default.Get(ReadKey, "");

            _lastReadUtc =
                DateTime.TryParse(savedReadTime, out var parsed)
                    ? parsed.ToUniversalTime()
                    : DateTime.MinValue;

            var services = (await AppServices.Db
                .GetAllAsync<ServiceItem>("services"))
                .ToList();

            var offers = (await AppServices.Db
                .GetAllAsync<ServiceOfferItem>("serviceOffers"))
                .ToList();

            var myServices = services
                .Where(x => x.OwnerId == _uid)
                .ToDictionary(x => x.ServiceId);

            var results = new List<NotificationItem>();

            foreach (var offer in offers)
            {
                bool isProvider =
                    myServices.ContainsKey(offer.ServiceId);

                bool isCustomer = offer.OfferedById == _uid;

                if (!isProvider && !isCustomer)
                    continue;

                var service = services.FirstOrDefault(
                    x => x.ServiceId == offer.ServiceId);

                var name = service?.Title ?? "Service";

                // Incoming service offer.
                if (isProvider)
                {
                    results.Add(new NotificationItem
                    {
                        Id = $"offer-{offer.OfferId}",
                        Title = "Service Offer",
                        Message =
                            $"{offer.OfferedByName} sent an offer for {name}: RM{offer.ProposedPrice:0.##}. Current status: {offer.Status}.",
                        OfferId = offer.OfferId,
                        ServiceId = offer.ServiceId,
                        Type = "Offer",
                        CreatedAtUtc = offer.CreatedAtUtc
                    });
                }

                // Status updates relevant to the customer.
                if (isCustomer &&
                    (offer.Status == "Countered" ||
                     offer.Status == "Accepted" ||
                     offer.Status == "Declined"))
                {
                    var title = offer.Status switch
                    {
                        "Countered" => "Counter Offer Received",
                        "Accepted" => "Your Offer Was Accepted",
                        _ => "Offer Declined"
                    };

                    results.Add(new NotificationItem
                    {
                        Id = $"status-{offer.OfferId}-{offer.Status}",
                        Title = title,
                        Message =
                            $"{name} • RM{offer.ProposedPrice:0.##}.",
                        OfferId = offer.OfferId,
                        ServiceId = offer.ServiceId,
                        Type = offer.Status == "Accepted"
                            ? "Chat"
                            : "Offer",
                        CreatedAtUtc = offer.UpdatedAtUtc
                    });
                }

                // Chat messages for accepted service offers.
                if (offer.Status != "Accepted")
                    continue;

                var messages = await AppServices.Db
                    .GetAllAsync<ChatMessage>(
                        $"serviceChats/{offer.OfferId}/messages");

                foreach (var message in messages)
                {
                    if (message.SenderId == _uid)
                        continue;

                    // Only the two participants get these alerts.
                    if (!isProvider && !isCustomer)
                        continue;

                    results.Add(new NotificationItem
                    {
                        Id = $"message-{offer.OfferId}-{message.MessageId}",
                        Title = $"New Message • {name}",
                        Message = message.Text,
                        OfferId = offer.OfferId,
                        ServiceId = offer.ServiceId,
                        Type = "Chat",
                        CreatedAtUtc = message.SentAtUtc
                    });
                }
            }

            var ordered = results
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(100)
                .ToList();

            _notifications.Clear();

            foreach (var item in ordered)
            {
                item.IsUnread =
                    item.CreatedAtUtc > _lastReadUtc;

                _notifications.Add(item);
            }

            UpdateCount();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to load notifications",
                ex.Message,
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateCount()
    {
        int unread = _notifications.Count(x => x.IsUnread);

        CountLabel.Text = unread == 0
            ? "No unread notifications"
            : $"{unread} unread notification(s)";
    }

    private async void OnRefreshClicked(
        object sender, EventArgs e)
    {
        await LoadAsync();
    }

    private async void OnMarkReadClicked(
        object sender, EventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(_uid))
            return;

        var latest = _notifications
            .Select(x => x.CreatedAtUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        if (latest == DateTime.MinValue)
            return;

        // Mark only the notifications currently loaded.
        _lastReadUtc = latest;

        Preferences.Default.Set(
            ReadKey,
            _lastReadUtc.ToString("O"));

        foreach (var item in _notifications)
            item.IsUnread = false;

        // Rebuild the collection because NotificationItem
        // doesn't implement property change notifications.
        var copy = _notifications.ToList();

        _notifications.Clear();

        foreach (var item in copy)
            _notifications.Add(item);

        UpdateCount();

        await Task.CompletedTask;
    }

    private async void OnNotificationClicked(
        object sender, EventArgs e)
    {
        if (sender is not Button
            { CommandParameter: NotificationItem item })
            return;

        try
        {
            if (item.Type == "Chat")
            {
                var offer = await AppServices.Db
                    .GetAsync<ServiceOfferItem>(
                        $"serviceOffers/{item.OfferId}");

                if (offer?.Status == "Accepted")
                {
                    await Shell.Current.GoToAsync(
                        nameof(ChatPage),
                        new Dictionary<string, object>
                        {
                            ["OfferId"] = item.OfferId
                        });
                    return;
                }
            }

            var service = await AppServices.Db
                .GetAsync<ServiceItem>(
                    $"services/{item.ServiceId}");

            if (service is null)
                throw new Exception("Service unavailable.");

            await Shell.Current.GoToAsync(
                nameof(ServiceDetailsPage),
                new Dictionary<string, object>
                {
                    ["Service"] = service
                });
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Unable to open notification",
                ex.Message,
                "OK");
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;

        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
        MarkReadButton.IsEnabled = !busy;
    }
}
