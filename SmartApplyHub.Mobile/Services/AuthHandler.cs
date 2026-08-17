using System.Net.Http.Headers;

namespace SmartApplyHub.Mobile.Services;

public class AuthHandler : DelegatingHandler
{
    private readonly AuthService _auth;
    private readonly IServiceProvider _services;

    public AuthHandler(AuthService auth, IServiceProvider services)
    {
        _auth = auth;
        _services = services;
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
                if (Application.Current?.Windows.Count is null or 0)
                    return;

                var loginPage = _services.GetRequiredService<Pages.LoginPage>();
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
