using System.Net.Http.Headers;

namespace SmartApplyHub.Mobile.Services;

public class AuthHandler : DelegatingHandler
{
    private readonly AuthService _auth;

    public AuthHandler(AuthService auth)
    {
        _auth = auth;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _auth.GetToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        // Never treat login/register 401s as a session expiry — those are expected
        // for bad credentials. Also avoid Shell navigation when Shell isn't active
        // (Login/Register live outside AppShell).
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && !IsAnonymousAuthRequest(request))
        {
            _auth.Logout();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var services = Application.Current?.Handler?.MauiContext?.Services;
                if (services == null || Application.Current?.Windows.Count == 0)
                    return;

                var loginPage = services.GetRequiredService<Pages.LoginPage>();
                Application.Current!.Windows[0].Page = new NavigationPage(loginPage);
            });
        }

        return response;
    }

    private static bool IsAnonymousAuthRequest(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        return path.Contains("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/auth/register", StringComparison.OrdinalIgnoreCase);
    }
}
