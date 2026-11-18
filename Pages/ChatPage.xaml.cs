
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CoreSwap.Models;
using CoreSwap.Services;
using Microsoft.Maui.Dispatching;

namespace CoreSwap.Pages;

public partial class ChatPage : ContentPage, IQueryAttributable
{
    private readonly ObservableCollection<ChatMessage>
        _messages = new();

    private readonly IDispatcherTimer _timer;

    private string _offerId = "";
    private string _uid = "";

    private bool _ready;
    private bool _loading;
    private bool _sending;

    public ChatPage()
    {
        InitializeComponent();

        MessagesCollection.ItemsSource = _messages;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(3);

        _timer.Tick += async (_, _) =>
        {
            await RefreshMessagesAsync(true);
        };
    }

    public async void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (!query.TryGetValue("OfferId", out var value))
            return;

        _offerId = value?.ToString() ?? "";

        await InitializeAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_ready)
        {
            _timer.Start();
            await RefreshMessagesAsync(true);
        }
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    private async Task InitializeAsync()
    {
        _timer.Stop();
        _ready = false;
        Composer.IsEnabled = false;
        ErrorLabel.IsVisible = false;

        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            _uid =
                await AppServices.Session.GetUserIdAsync()
                ?? "";

            if (string.IsNullOrWhiteSpace(_uid))
                throw new Exception("Please sign in again.");

            if (string.IsNullOrWhiteSpace(_offerId))
                throw new Exception("Invalid conversation.");

            var offer = await AppServices.Db
                .GetAsync<ServiceOfferItem>(
                    $"serviceOffers/{_offerId}");

            if (offer is null)
                throw new Exception("Offer not found.");

            var service = await AppServices.Db
                .GetAsync<ServiceItem>(
                    $"services/{offer.ServiceId}");

            if (service is null)
                throw new Exception("Service not found.");

            if (offer.ServiceOwnerId != service.OwnerId)
                throw new Exception("Invalid conversation owner.");

            bool participant =
                _uid == offer.OfferedById ||
                _uid == service.OwnerId;

            if (!participant)
            {
                throw new Exception(
                    "You cannot access this conversation.");
            }

            if (offer.Status != "Accepted")
            {
                throw new Exception(
                    "Chat is available after the offer is accepted.");
            }

            ChatTitle.Text = service.Title;

            ChatSubtitle.Text = _uid == service.OwnerId
                ? $"Chat with {offer.OfferedByName}"
                : $"Chat with {service.OwnerName}";

            _ready = true;
            Composer.IsEnabled = true;

            await RefreshMessagesAsync(false);

            if (IsVisible)
                _timer.Start();
        }
        catch (Exception ex)
        {
            _ready = false;
            Composer.IsEnabled = false;
            ShowError(ex.Message);
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    private async Task RefreshMessagesAsync(bool silent)
    {
        if (!_ready || _loading || _sending)
            return;

        try
        {
            _loading = true;

            var data = await AppServices.Db
                .GetAllAsync<ChatMessage>(
                    $"serviceChats/{_offerId}/messages");

            var sorted = data
                .OrderBy(x => x.SentAtUtc)
                .ThenBy(x => x.MessageId)
                .ToList();

            bool changed =
                sorted.Count != _messages.Count ||
                !sorted.Select(x => x.MessageId)
                    .SequenceEqual(
                        _messages.Select(x => x.MessageId));

            if (changed)
            {
                _messages.Clear();

                foreach (var message in sorted)
                {
                    message.IsMine =
                        message.SenderId == _uid;

                    _messages.Add(message);
                }

                ScrollToLatestMessage();
            }

            ErrorLabel.IsVisible = false;
        }
        catch (Exception ex)
        {
            ShowError(
                silent
                    ? "Unable to refresh. Check your connection."
                    : ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private void ScrollToLatestMessage()
    {
        if (_messages.Count == 0)
            return;

        Dispatcher.Dispatch(() =>
        {
            if (_messages.Count == 0)
                return;

            MessagesCollection.ScrollTo(
                _messages[^1],
                position: ScrollToPosition.End,
                animate: false);
        });
    }

    private void OnMessageFocused(
        object sender, FocusEventArgs e)
    {
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(350),
            ScrollToLatestMessage);
    }

    private async void OnSendClicked(
        object sender, EventArgs e)
    {
        await SendAsync();
    }

    private async void OnMessageCompleted(
        object sender, EventArgs e)
    {
        await SendAsync();
    }

    private async Task SendAsync()
    {
        if (!_ready || _sending)
            return;

        string text =
            MessageEntry.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(text))
            return;

        if (text.Length > 1000)
        {
            ShowError("Message is too long.");
            return;
        }

        try
        {
            _sending = true;
            SendButton.IsEnabled = false;

            var uid =
                await AppServices.Session.GetUserIdAsync();

            if (uid != _uid)
                throw new Exception("Session changed.");

            var offer = await AppServices.Db
                .GetAsync<ServiceOfferItem>(
                    $"serviceOffers/{_offerId}");

            if (offer is null ||
                offer.Status != "Accepted" ||
                (uid != offer.OfferedById &&
                 uid != offer.ServiceOwnerId))
            {
                throw new Exception(
                    "This conversation is unavailable.");
            }

            var messageId =
                Guid.NewGuid().ToString("N");

            var message = new ChatMessage
            {
                MessageId = messageId,
                SenderId = _uid,
                Text = text,
                SentAtUtc = DateTime.UtcNow
            };

            await AppServices.Db.PutAsync(
                $"serviceChats/{_offerId}/messages/{messageId}",
                message);

            MessageEntry.Text = "";
            ErrorLabel.IsVisible = false;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _sending = false;
            SendButton.IsEnabled = true;
        }

        await RefreshMessagesAsync(true);
        ScrollToLatestMessage();
    }

    private async void OnBackClicked(
        object sender, EventArgs e)
    {
        _timer.Stop();
        await Shell.Current.GoToAsync("..");
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
