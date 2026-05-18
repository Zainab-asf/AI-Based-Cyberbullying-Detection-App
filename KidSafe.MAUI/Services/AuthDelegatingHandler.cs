using System.Net.Http.Headers;

namespace KidSafe.MAUI.Services;

/// <summary>
/// Injects the JWT Bearer token into every outbound HTTP request.
/// Registered in DI as a DelegatingHandler so ApiService stays stateless.
/// </summary>
public sealed class AuthDelegatingHandler : DelegatingHandler
{
    private readonly AuthStateService _auth;

    public AuthDelegatingHandler(AuthStateService auth) => _auth = auth;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _auth.CurrentUser?.Token;
        if (!string.IsNullOrEmpty(token)
            && token is not ("pending" or "disabled")
            && !AuthStateService.IsTokenExpired(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Server is offline / refused connection — swallow so the debugger
            // doesn't treat this as "user-unhandled". ApiService callers all
            // check IsSuccessStatusCode or catch the null return.
            System.Diagnostics.Debug.WriteLine($"[KidSafe] Server unreachable: {ex.Message}");
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Server unreachable"
            };
        }
        catch (TaskCanceledException)
        {
            // Timeout
            return new HttpResponseMessage(System.Net.HttpStatusCode.RequestTimeout)
            {
                ReasonPhrase = "Request timed out"
            };
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            try { await _auth.LogoutAsync(); } catch { /* best-effort logout on 401 */ }

        return response;
    }
}
