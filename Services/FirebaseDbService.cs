using System.Net;
using System.Text;
using System.Text.Json;
using CoreSwap.Config;
using CoreSwap.Models;

namespace CoreSwap.Services;

public class FirebaseDbService
{
    private static readonly HttpClient Http = new();
    private readonly SessionService _session;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseDbService(SessionService session)
    {
        _session = session;
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        var json = await response.Content.ReadAsStringAsync();
        EnsureSuccess(response, json);
        if (json == "null") return default;
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async Task<List<T>> GetAllAsync<T>(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        var json = await response.Content.ReadAsStringAsync();
        EnsureSuccess(response, json);
        if (json == "null") return new List<T>();

        var dictionary = JsonSerializer.Deserialize<Dictionary<string, T>>(json, JsonOptions);
        return dictionary?.Values.ToList() ?? new List<T>();
    }

    public async Task PutAsync<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using var response = await SendAsync(HttpMethod.Put, path, json);
        var body = await response.Content.ReadAsStringAsync();
        EnsureSuccess(response, body);
    }

    public async Task PatchAsync(string path, Dictionary<string, object?> updates)
    {
        var json = JsonSerializer.Serialize(updates, JsonOptions);
        using var response = await SendAsync(HttpMethod.Patch, path, json);
        var body = await response.Content.ReadAsStringAsync();
        EnsureSuccess(response, body);
    }

    public async Task DeleteAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Delete, path);
        var body = await response.Content.ReadAsStringAsync();
        EnsureSuccess(response, body);
    }

    public async Task<List<RequestItem>> GetRequestsAsync()
    {
        var items = await GetAllAsync<RequestItem>("requests");
        return items
            .Where(x => x.Status != "Deleted")
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<List<ServiceItem>> GetServicesAsync()
    {
        var items = await GetAllAsync<ServiceItem>("services");
        return items
            .Where(x => x.Status == "Active")
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<List<OfferItem>> GetOffersForRequestAsync(string requestId)
    {
        var items = await GetAllAsync<OfferItem>("offers");
        return items
            .Where(x => x.RequestId == requestId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToList();
    }

    public async Task<OfferItem?> GetMyOfferForRequestAsync(string requestId, string userId)
    {
        var offers = await GetOffersForRequestAsync(requestId);
        return offers.FirstOrDefault(x => x.OfferedById == userId && x.Status != "Withdrawn");
    }

    public async Task AcceptOfferAsync(RequestItem request, OfferItem acceptedOffer)
    {
        var allOffers = await GetOffersForRequestAsync(request.RequestId);
        var updates = new Dictionary<string, object?>
        {
            [$"requests/{request.RequestId}/Status"] = "Accepted",
            [$"requests/{request.RequestId}/AcceptedOfferId"] = acceptedOffer.OfferId,
            [$"requests/{request.RequestId}/UpdatedAtUtc"] = DateTime.UtcNow,
            [$"offers/{acceptedOffer.OfferId}/Status"] = "Accepted",
            [$"offers/{acceptedOffer.OfferId}/UpdatedAtUtc"] = DateTime.UtcNow
        };

        foreach (var offer in allOffers.Where(x => x.OfferId != acceptedOffer.OfferId &&
                                                    (x.Status == "Pending" || x.Status == "Countered")))
        {
            updates[$"offers/{offer.OfferId}/Status"] = "Declined";
            updates[$"offers/{offer.OfferId}/UpdatedAtUtc"] = DateTime.UtcNow;
        }

        // Realtime Database multi-path PATCH updates all listed paths in one write.
        await PatchAsync(string.Empty, updates);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json = null, bool allowRetry = true)
    {
        var token = await _session.GetIdTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            throw new Exception("You are not signed in.");

        var cleanPath = path.Trim('/');
        var endpoint = string.IsNullOrEmpty(cleanPath)
            ? $"{FirebaseConfig.CleanDatabaseUrl}/.json"
            : $"{FirebaseConfig.CleanDatabaseUrl}/{cleanPath}.json";

        var url = $"{endpoint}?auth={Uri.EscapeDataString(token)}";
        using var request = new HttpRequestMessage(method, url);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized && allowRetry)
        {
            response.Dispose();
            await AppServices.Auth.RefreshTokenAsync();
            return await SendAsync(method, path, json, false);
        }

        return response;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;
        throw new Exception($"Database error ({(int)response.StatusCode}): {body}");
    }
}
