using System.Net;
using System.Net.Http.Json;

namespace Web.Services
{
    /// <summary>
    /// DelegatingHandler che, prima di ogni richiesta autenticata, controlla
    /// se il JWT scade entro 5 minuti e, in caso, lo rinnova tramite il refresh token.
    /// In caso di 401 inatteso (es. token revocato lato server) esegue il logout.
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
            // Non interferire con gli endpoint di auth per evitare ricorsione
            var path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("api/auth/"))
                return await base.SendAsync(request, ct);

            // Refresh proattivo se token scade entro 5 minuti
            if (_authState.IsAuthenticated &&
                !string.IsNullOrEmpty(_authState.RefreshToken) &&
                _authState.TokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
            {
                await TryRefreshTokenAsync(ct);
            }

            // Imposta header Authorization aggiornato
            if (_authState.IsAuthenticated)
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", _authState.Token);
            }

            var response = await base.SendAsync(request, ct);

            // Gestione 401 inatteso (token revocato lato server)
            if (response.StatusCode == HttpStatusCode.Unauthorized &&
                _authState.IsAuthenticated &&
                !string.IsNullOrEmpty(_authState.RefreshToken))
            {
                var refreshed = await TryRefreshTokenAsync(ct);
                if (refreshed)
                {
                    // Ritenta una volta sola con il nuovo token
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
                // Ricontrolla dopo aver acquisito il lock (un altro thread potrebbe già aver aggiornato)
                if (!_authState.IsAuthenticated || string.IsNullOrEmpty(_authState.RefreshToken))
                    return false;

                if (_authState.TokenExpiresAt > DateTime.UtcNow.AddMinutes(5))
                    return true; // Già rinnovato da un altro thread

                var refreshResponse = await base.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
                    {
                        Content = JsonContent.Create(new { refreshToken = _authState.RefreshToken })
                    }, ct);

                if (!refreshResponse.IsSuccessStatusCode)
                    return false;

                var authResult = await refreshResponse.Content
                    .ReadFromJsonAsync<AuthRefreshResult>(ct);

                if (authResult is null)
                    return false;

                await _authState.UpdateTokensAsync(
                    authResult.Token,
                    authResult.RefreshToken,
                    authResult.ExpiresAt);

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

        /// <summary>Crea una copia shallow della request per il retry (senza body).</summary>
        private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);
            foreach (var header in original.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            // Non cloniamo il body: i retry senza body (GET, DELETE) funzionano sempre;
            // per POST/PUT con body il retry è gestito a livello applicativo.
            return clone;
        }

        private record AuthRefreshResult(
            string Token, string RefreshToken,
            string Username, string Email, string UserType, DateTime ExpiresAt);
    }
}
