using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoreSwap.Config;
using CoreSwap.Models;

namespace CoreSwap.Services;

public class FirebaseAuthService
{
    private static readonly HttpClient Http = new();
    private readonly SessionService _session;

    public FirebaseAuthService(SessionService session)
    {
        _session = session;
    }

    public async Task RegisterAsync(string fullName, string email, string password)
    {
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseConfig.ApiKey}";
        var response = await Http.PostAsJsonAsync(url, new
        {
            email,
            password,
            returnSecureToken = true
        });

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception(ReadFirebaseError(body));

        var auth = JsonSerializer.Deserialize<AuthResponse>(body, JsonOptions())
                   ?? throw new Exception("Firebase returned an empty registration response.");

        await _session.SaveAsync(
            auth.IdToken,
            auth.RefreshToken,
            auth.LocalId,
            auth.Email,
            fullName,
            ParseExpiry(auth.ExpiresIn));

        var user = new AppUser
        {
            UserId = auth.LocalId,
            FullName = fullName,
            StudentEmail = auth.Email,
            CreatedAtUtc = DateTime.UtcNow
        };

        await AppServices.Db.PutAsync($"users/{auth.LocalId}", user);
    }

    public async Task LoginAsync(string email, string password)
    {
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseConfig.ApiKey}";
        var response = await Http.PostAsJsonAsync(url, new
        {
            email,
            password,
            returnSecureToken = true
        });

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception(ReadFirebaseError(body));

        var auth = JsonSerializer.Deserialize<AuthResponse>(body, JsonOptions())
                   ?? throw new Exception("Firebase returned an empty login response.");

        // Save a temporary session first so the database request is authenticated.
        await _session.SaveAsync(
            auth.IdToken,
            auth.RefreshToken,
            auth.LocalId,
            auth.Email,
            auth.Email.Split('@')[0],
            ParseExpiry(auth.ExpiresIn));

        var user = await AppServices.Db.GetAsync<AppUser>($"users/{auth.LocalId}");
        var fullName = user?.FullName ?? auth.Email.Split('@')[0];

        await _session.SaveAsync(
            auth.IdToken,
            auth.RefreshToken,
            auth.LocalId,
            auth.Email,
            fullName,
            ParseExpiry(auth.ExpiresIn));
    }

    public async Task<bool> EnsureValidSessionAsync()
    {
        if (!await _session.HasSessionAsync()) return false;
        if (!await _session.IsExpiredAsync()) return true;

        try
        {
            await RefreshTokenAsync();
            return true;
        }
        catch
        {
            _session.Clear();
            return false;
        }
    }

    public async Task RefreshTokenAsync()
    {
        var refreshToken = await _session.GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new Exception("No refresh token is available.");

        var url = $"https://securetoken.googleapis.com/v1/token?key={FirebaseConfig.ApiKey}";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        var response = await Http.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception(ReadFirebaseError(body));

        var refreshed = JsonSerializer.Deserialize<RefreshResponse>(body, JsonOptions())
                        ?? throw new Exception("Firebase returned an empty refresh response.");

        await _session.SaveAsync(
            refreshed.IdToken,
            refreshed.RefreshToken,
            refreshed.UserId,
            await _session.GetEmailAsync() ?? string.Empty,
            await _session.GetFullNameAsync() ?? string.Empty,
            ParseExpiry(refreshed.ExpiresIn));
    }

    public async Task SendPasswordResetAsync(string email)
    {
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseConfig.ApiKey}";
        var response = await Http.PostAsJsonAsync(url, new
        {
            requestType = "PASSWORD_RESET",
            email
        });

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception(ReadFirebaseError(body));
    }

    public void Logout() => _session.Clear();

    private static int ParseExpiry(string value) => int.TryParse(value, out var seconds) ? seconds : 3600;

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string ReadFirebaseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var message = doc.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString() ?? "Firebase request failed.";

            return message switch
            {
                "EMAIL_EXISTS" => "This email is already registered.",
                "EMAIL_NOT_FOUND" => "No account was found for this email.",
                "INVALID_PASSWORD" => "The password is incorrect.",
                "INVALID_LOGIN_CREDENTIALS" => "Email or password is incorrect.",
                "WEAK_PASSWORD : Password should be at least 6 characters" => "Password must be at least 6 characters.",
                "INVALID_EMAIL" => "Enter a valid email address.",
                "USER_DISABLED" => "This account has been disabled.",
                _ => message.Replace('_', ' ').ToLowerInvariant()
            };
        }
        catch
        {
            return "Unable to complete the Firebase request.";
        }
    }

    private sealed class AuthResponse
    {
        [JsonPropertyName("idToken")] public string IdToken { get; set; } = string.Empty;
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("expiresIn")] public string ExpiresIn { get; set; } = "3600";
        [JsonPropertyName("localId")] public string LocalId { get; set; } = string.Empty;
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("id_token")] public string IdToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public string ExpiresIn { get; set; } = "3600";
        [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
    }
}
