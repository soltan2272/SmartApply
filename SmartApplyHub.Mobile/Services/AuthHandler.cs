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

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _auth.Logout();
            await Shell.Current.GoToAsync("//Login");
        }

        return response;
    }
}
