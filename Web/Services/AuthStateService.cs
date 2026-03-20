using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Web.Services
{
    public class AuthStateService
    {
        private readonly ProtectedSessionStorage _storage;

        public string? Token { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? Username { get; private set; }
        public string? Email { get; private set; }
        public string? UserType { get; private set; }
        public int UserId { get; private set; }
        public DateTime TokenExpiresAt { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrEmpty(Token);
        public string? TenantSubdomain { get; private set; }

        public event Action? OnChange;

        public AuthStateService(ProtectedSessionStorage storage)
        {
            _storage = storage;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var token = await _storage.GetAsync<string>("auth_token");
                var refreshToken = await _storage.GetAsync<string>("auth_refresh_token");
                var username = await _storage.GetAsync<string>("auth_username");
                var email = await _storage.GetAsync<string>("auth_email");
                var userType = await _storage.GetAsync<string>("auth_usertype");
                var tenant = await _storage.GetAsync<string>("auth_tenant");
                var expiresAt = await _storage.GetAsync<long>("auth_expires_at");

                if (token.Success && !string.IsNullOrEmpty(token.Value))
                {
                    Token = token.Value;
                    RefreshToken = refreshToken.Value;
                    Username = username.Value;
                    Email = email.Value;
                    UserType = userType.Value;
                    TenantSubdomain = tenant.Value ?? "cliente1";
                    TokenExpiresAt = expiresAt.Success
                        ? new DateTime(expiresAt.Value, DateTimeKind.Utc)
                        : DateTime.UtcNow.AddHours(8);
                    ExtractUserId();
                }
            }
            catch { }
        }

        public async Task SetUserAsync(
            string token,
            string refreshToken,
            string username,
            string email,
            string userType,
            DateTime expiresAt,
            string tenantSubdomain = "cliente1")
        {
            Token = token;
            RefreshToken = refreshToken;
            Username = username;
            Email = email;
            UserType = userType;
            TenantSubdomain = tenantSubdomain;
            TokenExpiresAt = expiresAt;
            ExtractUserId();

            await _storage.SetAsync("auth_token", token);
            await _storage.SetAsync("auth_refresh_token", refreshToken);
            await _storage.SetAsync("auth_username", username);
            await _storage.SetAsync("auth_email", email);
            await _storage.SetAsync("auth_usertype", userType);
            await _storage.SetAsync("auth_tenant", tenantSubdomain);
            await _storage.SetAsync("auth_expires_at", expiresAt.Ticks);

            NotifyStateChanged();
        }

        /// <summary>Aggiorna solo token e refresh token (usato dall'auto-refresh).</summary>
        public async Task UpdateTokensAsync(string token, string refreshToken, DateTime expiresAt)
        {
            Token = token;
            RefreshToken = refreshToken;
            TokenExpiresAt = expiresAt;
            ExtractUserId();

            await _storage.SetAsync("auth_token", token);
            await _storage.SetAsync("auth_refresh_token", refreshToken);
            await _storage.SetAsync("auth_expires_at", expiresAt.Ticks);

            NotifyStateChanged();
        }

        public async Task LogoutAsync()
        {
            Token = null;
            RefreshToken = null;
            Username = null;
            Email = null;
            UserType = null;
            TenantSubdomain = null;
            UserId = 0;
            TokenExpiresAt = default;

            await _storage.DeleteAsync("auth_token");
            await _storage.DeleteAsync("auth_refresh_token");
            await _storage.DeleteAsync("auth_username");
            await _storage.DeleteAsync("auth_email");
            await _storage.DeleteAsync("auth_usertype");
            await _storage.DeleteAsync("auth_tenant");
            await _storage.DeleteAsync("auth_expires_at");

            NotifyStateChanged();
        }

        private void ExtractUserId()
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(Token);
            var idClaim = jwt.Claims.FirstOrDefault(c => c.Type == "nameid" || c.Type == "sub");
            UserId = int.TryParse(idClaim?.Value, out var id) ? id : 0;
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
