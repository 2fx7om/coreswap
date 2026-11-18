using System.Collections.ObjectModel;
using CoreSwap.Models;
using CoreSwap.Services;

namespace CoreSwap.Pages;

public partial class RequestDetailsPage : ContentPage, IQueryAttributable
{
    private RequestItem? _request;
    private readonly ObservableCollection<OfferItem> _offers = new();
    private OfferItem? _myOffer;
    private OfferItem? _selectedCounterOffer;
    private string _currentUserId = string.Empty;
    private bool _isOwner;
    private bool _offerSubmitting;
    private bool _acceptingOffer;

    // Hides owner negotiation buttons when a request is no longer open.
    public bool CanManageOffers { get; private set; }

    public RequestDetailsPage()
    {
        InitializeComponent();
        OffersCollection.ItemsSource = _offers;
        OfferDatePicker.MinimumDate = DateTime.Today;
        var suggested = DateTime.Now.AddHours(1);
        OfferDatePicker.Date = suggested.Date;
        OfferTimePicker.Time = suggested.TimeOfDay;
        CounterDatePicker.MinimumDate = DateTime.Today;
        CounterDatePicker.Date = suggested.Date;
        CounterTimePicker.Time = suggested.TimeOfDay;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Request", out var value) && value is RequestItem request)
        {
            _request = request;
            try { await ReloadAsync(); }
            catch (Exception ex) { await DisplayAlert("Unable to load request", ex.Message, "OK"); }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_request is not null)
        {
            try { await ReloadAsync(); }
            catch (Exception ex) { await DisplayAlert("Unable to load request", ex.Message, "OK"); }
        }
    }

    private async Task ReloadAsync()
    {
        if (_request is null) return;

        var fresh = await AppServices.Db.GetAsync<RequestItem>($"requests/{_request.RequestId}");
        if (fresh is null)
        {
            await DisplayAlert("Request unavailable", "This request no longer exists.", "OK");
            await Shell.Current.GoToAsync("..");
            return;
        }
        _request = fresh;

        _currentUserId = await AppServices.Session.GetUserIdAsync() ?? string.Empty;
        _isOwner = _request.OwnerId == _currentUserId;

        RequestImage.Source = await FirebasePhotoLoader.LoadAsync(
     $"requestPhotos/{_request.RequestId}",
     _request.ImageSource,
     "laptop_request.jpg");
        TitleLabel.Text = _request.Title;
        StatusLabel.Text = _request.Status;
        OwnerLabel.Text = _isOwner ? "Your request" : $"Posted by {_request.OwnerName}";
        CategoryLabel.Text = _request.Category;
        DescriptionLabel.Text = _request.Description;
        BudgetLabel.Text = _request.BudgetText;
        PreferredTimeLabel.Text = string.IsNullOrWhiteSpace(_request.PreferredDate)
            ? (string.IsNullOrWhiteSpace(_request.PreferredTime) ? "No preferred date specified" : $"Preferred: {_request.PreferredTime}")
            : $"Preferred: {_request.PreferredDate} at {_request.PreferredTime}";

        HelperPanel.IsVisible = !_isOwner && (_request.Status == "Available" || _request.Status == "Negotiating");
        OwnerPanel.IsVisible = _isOwner;
        OwnerActionsGrid.IsVisible = _isOwner &&
            (_request.Status == "Available" || _request.Status == "Negotiating");
        CanManageOffers = _isOwner &&
            (_request.Status == "Available" || _request.Status == "Negotiating");
        OnPropertyChanged(nameof(CanManageOffers));

        // Keep the offer history scrollable even when no actions are allowed.
        OffersCollection.IsEnabled = _isOwner;
        CompleteButton.IsVisible = _isOwner && _request.Status == "Accepted";

        var offers = await AppServices.Db.GetOffersForRequestAsync(_request.RequestId);

        _offers.Clear();
        foreach (var offer in offers.Where(x => x.Status != "Withdrawn"))
            _offers.Add(offer);

        if (!_isOwner)
        {
            _myOffer = offers.FirstOrDefault(x => x.OfferedById == _currentUserId && x.Status != "Withdrawn");
            RenderMyOffer();

            // A targeted request can only be answered by the selected provider.
            if (!string.IsNullOrWhiteSpace(_request.TargetProviderId) && _request.TargetProviderId != _currentUserId)
            {
                MakeOfferButton.IsVisible = false;
                MyOfferCard.IsVisible = false;
            }
        }

        await RenderMatchAsync(offers);
    }

    private void RenderMyOffer()
    {
        MyOfferCard.IsVisible = _myOffer is not null;
        MakeOfferButton.IsVisible = _myOffer is null;
        if (_myOffer is null) return;

        MyOfferSummaryLabel.Text = _myOffer.Summary;
        MyOfferStatusLabel.Text = _myOffer.Status;
        CounterResponseButtons.IsVisible = _myOffer.Status == "Countered";
    }

    private async Task RenderMatchAsync(List<OfferItem> offers)
    {
        MatchCard.IsVisible = false;
        if (_request is null || string.IsNullOrWhiteSpace(_request.AcceptedOfferId)) return;

        var accepted = offers.FirstOrDefault(x => x.OfferId == _request.AcceptedOfferId);
        if (accepted is null) return;

        var visibleToUser = _isOwner || accepted.OfferedById == _currentUserId;
        if (!visibleToUser) return;

        MatchCard.IsVisible = true;
        MatchedWithLabel.Text = _isOwner
            ? $"Matched with {accepted.OfferedByName}"
            : $"Matched with {_request.OwnerName}";
        MatchTermsLabel.Text = accepted.Summary;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        // If a dialog-like overlay is open, close it before navigating away.
        if (OfferOverlay.IsVisible)
        {
            OfferOverlay.IsVisible = false;
            return;
        }

        if (CounterOverlay.IsVisible)
        {
            CounterOverlay.IsVisible = false;
            _selectedCounterOffer = null;
            return;
        }

        await Shell.Current.GoToAsync("..");
    }

    private void OnMakeOfferClicked(object sender, EventArgs e)
    {
        OfferOverlay.IsVisible = true;
    }

    private void OnOfferOverlayCancelClicked(object sender, EventArgs e)
    {
        OfferOverlay.IsVisible = false;
    }

    private async void OnSubmitOfferClicked(object sender, EventArgs e)
    {
        if (_offerSubmitting || _request is null) return;

        if (!decimal.TryParse(OfferPriceEntry.Text, out var price) || price < 0)
        {
            await DisplayAlert("Price", "Enter a valid proposed price.", "OK");
            return;
        }

        var date = OfferDatePicker.Date;
        var time = OfferTimePicker.Time;
        var location = OfferLocationEntry.Text?.Trim() ?? string.Empty;
        var message = OfferMessageEditor.Text?.Trim() ?? string.Empty;
        if (!ScheduleValidator.IsFuture(date, time))
        {
            await DisplayAlert("Date", "Choose a future offer date and time.", "OK");
            return;
        }
        if (string.IsNullOrWhiteSpace(location))
        {
            await DisplayAlert("Missing information", "Meeting location is required.", "OK");
            return;
        }

        _offerSubmitting = true;
        SubmitOfferButton.IsEnabled = false;
        try
        {
            var uid = await AppServices.Session.GetUserIdAsync() ?? string.Empty;
            var fresh = await AppServices.Db.GetAsync<RequestItem>($"requests/{_request.RequestId}");
            if (fresh is null || (fresh.Status != "Available" && fresh.Status != "Negotiating"))
            {
                await DisplayAlert("Unavailable", "This request is no longer accepting offers.", "OK");
                await ReloadAsync();
                return;
            }
            if (string.IsNullOrWhiteSpace(uid) || uid == fresh.OwnerId)
            {
                await DisplayAlert("Not allowed", "You cannot offer on your own request.", "OK");
                return;
            }
            if (!string.IsNullOrWhiteSpace(fresh.TargetProviderId) && fresh.TargetProviderId != uid)
            {
                await DisplayAlert("Targeted request", "This request is reserved for a specific provider.", "OK");
                return;
            }
            var existing = await AppServices.Db.GetMyOfferForRequestAsync(fresh.RequestId, uid);
            if (existing is not null)
            {
                await DisplayAlert("Offer exists", "You already have an active offer for this request.", "OK");
                await ReloadAsync();
                return;
            }
            var id = Guid.NewGuid().ToString("N");
            var offer = new OfferItem
            {
                OfferId = id,
                RequestId = fresh.RequestId,
                OfferedById = uid,
                OfferedByName = await AppServices.Session.GetFullNameAsync() ?? "Student",
                ProposedPrice = price,
                ProposedDate = ScheduleValidator.ToDateString(date),
                ProposedTime = ScheduleValidator.ToTimeString(time),
                Location = location,
                Message = message,
                Status = "Pending",
                LastUpdatedBy = uid,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            await AppServices.Db.PutAsync($"offers/{id}", offer);
            OfferOverlay.IsVisible = false;
            await DisplayAlert("Offer sent", "Your offer was sent to the request owner.", "OK");
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Unable to send offer", ex.Message, "OK");
        }
        finally
        {
            _offerSubmitting = false;
            SubmitOfferButton.IsEnabled = true;
        }
    }

    private async void OnAcceptOfferClicked(object sender, EventArgs e)
    {
        if (_acceptingOffer || !_isOwner || _request is null ||
            sender is not Button { CommandParameter: OfferItem offer }) return;

        _acceptingOffer = true;
        try
        {
            var current = await AppServices.Db.GetAsync<RequestItem>($"requests/{_request.RequestId}");
            var savedOffer = await AppServices.Db.GetAsync<OfferItem>($"offers/{offer.OfferId}");
            if (current is null || (current.Status != "Available" && current.Status != "Negotiating") ||
                savedOffer is null || savedOffer.RequestId != _request.RequestId ||
                savedOffer.Status != "Pending")
            {
                await DisplayAlert("Offer unavailable", "The offer must be pending and the request still open.", "OK");
                await ReloadAsync();
                return;
            }

            // The owner must not accept their own counter until the helper has accepted it.
            var confirm = await DisplayAlert("Accept offer?",
                $"Accept {savedOffer.OfferedByName}'s offer for {savedOffer.Summary}?", "Accept", "Cancel");
            if (!confirm) return;

            // Re-read after the confirmation dialog. Server-side transaction rules are
            // still required to prevent two devices accepting different offers at once.
            current = await AppServices.Db.GetAsync<RequestItem>($"requests/{_request.RequestId}");
            savedOffer = await AppServices.Db.GetAsync<OfferItem>($"offers/{offer.OfferId}");
            if (current is null || (current.Status != "Available" && current.Status != "Negotiating") ||
                savedOffer is null || savedOffer.Status != "Pending")
            {
                await DisplayAlert("Offer changed", "Refresh the request and try again.", "OK");
                await ReloadAsync();
                return;
            }

            await AppServices.Db.AcceptOfferAsync(current, savedOffer);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Unable to accept offer", ex.Message, "OK");
        }
        finally
        {
            _acceptingOffer = false;
        }
    }

    private void OnCounterOfferClicked(object sender, EventArgs e)
    {
        if (!_isOwner || _request is null ||
            (_request.Status != "Available" && _request.Status != "Negotiating") ||
            sender is not Button { CommandParameter: OfferItem offer }) return;
        _selectedCounterOffer = offer;
        CounterPriceEntry.Text = offer.ProposedPrice.ToString("0.##");
        if (ScheduleValidator.TryParseDate(offer.ProposedDate, out var counterDate) && counterDate >= DateTime.Today)
            CounterDatePicker.Date = counterDate;
        if (TimeSpan.TryParse(offer.ProposedTime, out var counterTime))
            CounterTimePicker.Time = counterTime;
        CounterLocationEntry.Text = offer.Location;
        CounterMessageEditor.Text = offer.Message;
        CounterOverlay.IsVisible = true;
    }

    private async void OnDeclineOfferClicked(object sender, EventArgs e)
    {
        if (!_isOwner || _request is null ||
            (_request.Status != "Available" && _request.Status != "Negotiating") ||
            sender is not Button { CommandParameter: OfferItem offer }) return;
        await AppServices.Db.PatchAsync($"offers/{offer.OfferId}", new Dictionary<string, object?>
        {
            ["Status"] = "Declined",
            ["UpdatedAtUtc"] = DateTime.UtcNow,
            ["LastUpdatedBy"] = _currentUserId
        });
        await ReloadAsync();
    }

    private void OnCounterOverlayCancelClicked(object sender, EventArgs e)
    {
        CounterOverlay.IsVisible = false;
        _selectedCounterOffer = null;
    }

    private async void OnSendCounterClicked(object sender, EventArgs e)
    {
        var offer = _selectedCounterOffer ?? _myOffer;
        if (offer is null || _request is null ||
            (_request.Status != "Available" && _request.Status != "Negotiating")) return;

        if (!decimal.TryParse(CounterPriceEntry.Text, out var price) || price < 0)
        {
            await DisplayAlert("Price", "Enter a valid counter price.", "OK");
            return;
        }

        if (!ScheduleValidator.IsFuture(CounterDatePicker.Date, CounterTimePicker.Time))
        {
            await DisplayAlert("Date", "Choose a future counter date and time.", "OK");
            return;
        }
        if (string.IsNullOrWhiteSpace(CounterLocationEntry.Text))
        {
            await DisplayAlert("Location", "Enter a meeting location.", "OK");
            return;
        }

        await AppServices.Db.PatchAsync($"offers/{offer.OfferId}", new Dictionary<string, object?>
        {
            ["ProposedPrice"] = price,
            ["ProposedDate"] = ScheduleValidator.ToDateString(CounterDatePicker.Date),
            ["ProposedTime"] = ScheduleValidator.ToTimeString(CounterTimePicker.Time),
            ["Location"] = CounterLocationEntry.Text?.Trim() ?? string.Empty,
            ["Message"] = CounterMessageEditor.Text?.Trim() ?? string.Empty,
            ["Status"] = "Countered",
            ["LastUpdatedBy"] = _currentUserId,
            ["UpdatedAtUtc"] = DateTime.UtcNow
        });

        if (_isOwner)
        {
            await AppServices.Db.PatchAsync($"requests/{_request.RequestId}", new Dictionary<string, object?>
            {
                ["Status"] = "Negotiating",
                ["UpdatedAtUtc"] = DateTime.UtcNow
            });
        }

        CounterOverlay.IsVisible = false;
        _selectedCounterOffer = null;
        await ReloadAsync();
    }

    private async void OnAcceptCounterClicked(object sender, EventArgs e)
    {
        if (_myOffer is null || _request is null) return;
        // Helper accepts the owner's latest counter. The request owner still remains the one
        // who finally locks the request by pressing Accept on the offer card.
        await AppServices.Db.PatchAsync($"offers/{_myOffer.OfferId}", new Dictionary<string, object?>
        {
            ["Status"] = "Pending",
            ["LastUpdatedBy"] = _currentUserId,
            ["UpdatedAtUtc"] = DateTime.UtcNow
        });
        await DisplayAlert("Counter accepted", "The request owner can now confirm the match.", "OK");
        await ReloadAsync();
    }

    private void OnCounterAgainClicked(object sender, EventArgs e)
    {
        if (_myOffer is null) return;
        _selectedCounterOffer = _myOffer;
        CounterPriceEntry.Text = _myOffer.ProposedPrice.ToString("0.##");
        if (ScheduleValidator.TryParseDate(_myOffer.ProposedDate, out var counterDateAgain) && counterDateAgain >= DateTime.Today)
            CounterDatePicker.Date = counterDateAgain;
        if (TimeSpan.TryParse(_myOffer.ProposedTime, out var counterTimeAgain))
            CounterTimePicker.Time = counterTimeAgain;
        CounterLocationEntry.Text = _myOffer.Location;
        CounterMessageEditor.Text = _myOffer.Message;
        CounterOverlay.IsVisible = true;
    }

    private async void OnWithdrawOfferClicked(object sender, EventArgs e)
    {
        if (_myOffer is null) return;
        await AppServices.Db.PatchAsync($"offers/{_myOffer.OfferId}", new Dictionary<string, object?>
        {
            ["Status"] = "Withdrawn",
            ["LastUpdatedBy"] = _currentUserId,
            ["UpdatedAtUtc"] = DateTime.UtcNow
        });
        await ReloadAsync();
    }

    private async void OnEditRequestClicked(object sender, EventArgs e)
    {
        if (!_isOwner || _request is null) return;
        if (_request.Status == "Accepted" || _request.Status == "Completed")
        {
            await DisplayAlert("Not editable", "Accepted or completed requests are kept as history.", "OK");
            return;
        }

        await Shell.Current.GoToAsync(nameof(CreateRequestPage), new Dictionary<string, object>
        {
            ["EditRequest"] = _request
        });
    }

    private async void OnDeleteRequestClicked(object sender, EventArgs e)
    {
        if (!_isOwner || _request is null) return;

        var currentOffers = await AppServices.Db.GetOffersForRequestAsync(_request.RequestId);
        if (currentOffers.Any())
        {
            await DisplayAlert("Use Cancel instead", "A request that has received any offers should not be hard-deleted, so related activity remains available.", "OK");
            return;
        }

        var confirm = await DisplayAlert("Delete request?", "This open request has no active offers and will be permanently deleted.", "Delete", "Keep");
        if (!confirm) return;

        await AppServices.Db.DeleteAsync($"requests/{_request.RequestId}");
        await Shell.Current.GoToAsync("..");
    }

    private async void OnCancelRequestClicked(object sender, EventArgs e)
    {
        if (!_isOwner || _request is null) return;
        if (_request.Status == "Accepted" || _request.Status == "Completed")
        {
            await DisplayAlert("Not allowed", "An accepted/completed request cannot be deleted. Complete the workflow instead.", "OK");
            return;
        }

        var confirm = await DisplayAlert("Cancel request?", "The request will be marked as Cancelled and kept in your history.", "Cancel Request", "Keep");
        if (!confirm) return;

        await AppServices.Db.PatchAsync($"requests/{_request.RequestId}", new Dictionary<string, object?>
        {
            ["Status"] = "Cancelled",
            ["UpdatedAtUtc"] = DateTime.UtcNow
        });
        await Shell.Current.GoToAsync("..");
    }

    private async void OnCompleteClicked(object sender, EventArgs e)
    {
        if (!_isOwner || _request is null || _request.Status != "Accepted") return;
        await AppServices.Db.PatchAsync($"requests/{_request.RequestId}", new Dictionary<string, object?>
        {
            ["Status"] = "Completed",
            ["UpdatedAtUtc"] = DateTime.UtcNow
        });
        await ReloadAsync();
    }

    private async void OnContactStudentClicked(object sender, EventArgs e)
    {
        if (_request is null || string.IsNullOrWhiteSpace(_request.AcceptedOfferId)) return;
        var offers = await AppServices.Db.GetOffersForRequestAsync(_request.RequestId);
        var accepted = offers.FirstOrDefault(x => x.OfferId == _request.AcceptedOfferId);
        if (accepted is null) return;

        string? otherEmail;
        if (_isOwner)
        {
            var user = await AppServices.Db.GetAsync<AppUser>($"users/{accepted.OfferedById}");
            otherEmail = user?.StudentEmail;
        }
        else
        {
            var user = await AppServices.Db.GetAsync<AppUser>($"users/{_request.OwnerId}");
            otherEmail = user?.StudentEmail;
        }

        if (string.IsNullOrWhiteSpace(otherEmail))
        {
            await DisplayAlert("Contact", "No contact email is available.", "OK");
            return;
        }

        try
        {
            await Email.Default.ComposeAsync(new EmailMessage
            {
                Subject = $"CoreSwap: {_request.Title}",
                Body = "Hi, we matched on CoreSwap. Let's confirm the meeting details.",
                To = new List<string> { otherEmail }
            });
        }
        catch
        {
            await DisplayAlert("Contact", $"Email: {otherEmail}", "OK");
        }
    }
}
