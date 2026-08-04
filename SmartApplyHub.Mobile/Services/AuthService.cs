using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.Services;

public class AuthService
{
    private const string TokenKey = "jwt_token";
    private const string ExpiresKey = "jwt_expires";
    private const string UserEmailKey = "user_email";
    private const string UserIdKey = "user_id";

    public bool IsLoggedIn => !string.IsNullOrEmpty(GetToken());

    public string? GetToken()
    {
        try
        {
            var token = SecureStorage.GetAsync(TokenKey).Result;
            if (string.IsNullOrEmpty(token)) return null;

            var expiresStr = SecureStorage.GetAsync(ExpiresKey).Result;
            if (DateTime.TryParse(expiresStr, out var expires) && expires <= DateTime.UtcNow)
            {
                Logout();
                return null;
            }

            return token;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAuthAsync(AuthResponse response)
    {
        await SecureStorage.SetAsync(TokenKey, response.Token);
        await SecureStorage.SetAsync(ExpiresKey, response.ExpiresAt.ToString("O"));
        await SecureStorage.SetAsync(UserEmailKey, response.Email);
        await SecureStorage.SetAsync(UserIdKey, response.UserId);
    }

    public string? GetEmail()
    {
        try { return SecureStorage.GetAsync(UserEmailKey).Result; }
        catch { return null; }
    }

    public void Logout()
    {
        SecureStorage.Remove(TokenKey);
        SecureStorage.Remove(ExpiresKey);
        SecureStorage.Remove(UserEmailKey);
        SecureStorage.Remove(UserIdKey);
    }
}
