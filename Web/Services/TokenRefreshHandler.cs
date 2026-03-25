using System.Net;
using System.Net.Http.Json;

namespace Web.Services
{
    /// <summary>
    /// DelegatingHandler che, prima di ogni richiesta autenticata, controlla
    /// se il JWT scade entro 5 minuti e, in caso, chiama POST /api/v1/auth/refresh
    /// senza body — il refresh token viene trasmesso automaticamente dal
    /// CookieContainer (cookie HttpOnly set dall'API).
    /// In caso di 401 inatteso esegue il logout.
    /// </summary>
    public class TokenRefreshHandler : DelegatingHandler
    {
        private readonly AuthStateService _authState;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public TokenRefreshHandler(AuthStateService authState)
        {
            _authState = authState;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("/auth/"))
                return await base.SendAsync(request, ct);

            // Refresh proattivo se il JWT scade entro 5 minuti
            if (_authState.IsAuthenticated &&
                _authState.TokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
            {
                await TryRefreshTokenAsync(ct);
            }

            if (_authState.IsAuthenticated)
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", _authState.Token);
            }

            var response = await base.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized && _authState.IsAuthenticated)
            {
                var refreshed = await TryRefreshTokenAsync(ct);
                if (refreshed)
                {
                    var retry = CloneRequest(request);
                    retry.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Bearer", _authState.Token);
                    response = await base.SendAsync(retry, ct);
                }
                else
                {
                    await _authState.LogoutAsync();
                }
            }

            return response;
        }

        private async Task<bool> TryRefreshTokenAsync(CancellationToken ct)
        {
            await _lock.WaitAsync(ct);
            try
            {
                if (!_authState.IsAuthenticated)
                    return false;

                // Già rinnovato da un altro thread
                if (_authState.TokenExpiresAt > DateTime.UtcNow.AddMinutes(5))
                    return true;

                // POST con body vuoto — il cookie refresh_token viene inviato
                // automaticamente dal CookieContainer del HttpClientHandler.
                var refreshMsg = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/refresh")
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                };
                var refreshResponse = await base.SendAsync(refreshMsg, ct);

                if (!refreshResponse.IsSuccessStatusCode)
                    return false;

                var result = await refreshResponse.Content
                    .ReadFromJsonAsync<TokenRefreshResult>(ct);

                if (result is null)
                    return false;

                await _authState.UpdateAccessTokenAsync(result.AccessToken, result.ExpiresAt);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);
            foreach (var header in original.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return clone;
        }

        private record TokenRefreshResult(
            string AccessToken, string Username, string Email,
            string UserType, DateTime ExpiresAt, int ExpiresIn);
    }
}
