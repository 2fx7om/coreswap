
namespace CoreSwap.Services;

public class SessionService
{
    private const string IdTokenKey = "coreswap_id_token";
    private const string RefreshTokenKey = "coreswap_refresh_token";
    private const string UserIdKey = "coreswap_user_id";
    private const string EmailKey = "coreswap_email";
    private const string FullNameKey = "coreswap_full_name";
    private const string ExpiresKey = "coreswap_expires_utc";

    public async Task SaveAsync(
        string idToken,
        string refreshToken,
        string userId,
        string email,
        string fullName,
        int expiresInSeconds)
    {
        await SecureStorage.Default.SetAsync(IdTokenKey, idToken);
        await SecureStorage.Default.SetAsync(
            RefreshTokenKey, refreshToken);
        await SecureStorage.Default.SetAsync(UserIdKey, userId);
        await SecureStorage.Default.SetAsync(EmailKey, email);
        await SecureStorage.Default.SetAsync(
            FullNameKey, fullName);

        await SecureStorage.Default.SetAsync(
            ExpiresKey,
            DateTime.UtcNow
                .AddSeconds(Math.Max(0, expiresInSeconds - 60))
                .ToString("O"));
    }

    public Task<string?> GetIdTokenAsync() =>
        SecureStorage.Default.GetAsync(IdTokenKey);

    public Task<string?> GetRefreshTokenAsync() =>
        SecureStorage.Default.GetAsync(RefreshTokenKey);

    public Task<string?> GetUserIdAsync() =>
        SecureStorage.Default.GetAsync(UserIdKey);

    public Task<string?> GetEmailAsync() =>
        SecureStorage.Default.GetAsync(EmailKey);

    public Task<string?> GetFullNameAsync() =>
        SecureStorage.Default.GetAsync(FullNameKey);

    public async Task UpdateFullNameAsync(string fullName)
    {
        await SecureStorage.Default.SetAsync(
            FullNameKey, fullName);
    }

    public async Task<bool> IsExpiredAsync()
    {
        var raw = await SecureStorage.Default.GetAsync(ExpiresKey);

        if (!DateTime.TryParse(raw, out var expiry))
            return true;

        return DateTime.UtcNow >= expiry.ToUniversalTime();
    }

    public async Task<bool> HasSessionAsync()
    {
        var uid = await GetUserIdAsync();
        var refresh = await GetRefreshTokenAsync();

        return !string.IsNullOrWhiteSpace(uid) &&
               !string.IsNullOrWhiteSpace(refresh);
    }

    public void Clear()
    {
        SecureStorage.Default.RemoveAll();
    }
}
