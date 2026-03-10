using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Web.Services
{
    public class AuthStateService
    {
        private readonly ProtectedSessionStorage _storage;

        public string? Token { get; private set; }
        public string? Username { get; private set; }
        public string? Email { get; private set; }
        public string? UserType { get; private set; }
        public int UserId { get; private set; }
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
                var username = await _storage.GetAsync<string>("auth_username");
                var email = await _storage.GetAsync<string>("auth_email");
                var userType = await _storage.GetAsync<string>("auth_usertype");
                var tenant = await _storage.GetAsync<string>("auth_tenant");

                if (token.Success && !string.IsNullOrEmpty(token.Value))
                {
                    Token = token.Value;
                    Username = username.Value;
                    Email = email.Value;
                    UserType = userType.Value;
                    TenantSubdomain = tenant.Value ?? "cliente1";
                    ExtractUserId();
                }
            }
            catch { }
        }

        public async Task SetUserAsync(string token, string username, string email, string userType, string tenantSubdomain = "cliente1")
        {
            Token = token;
            Username = username;
            Email = email;
            UserType = userType;
            TenantSubdomain = tenantSubdomain;
            ExtractUserId();

            await _storage.SetAsync("auth_token", token);
            await _storage.SetAsync("auth_username", username);
            await _storage.SetAsync("auth_email", email);
            await _storage.SetAsync("auth_usertype", userType);
            await _storage.SetAsync("auth_tenant", tenantSubdomain);

            NotifyStateChanged();
        }

        public async Task LogoutAsync()
        {
            Token = null;
            Username = null;
            Email = null;
            UserType = null;
            TenantSubdomain = null;
            UserId = 0;

            await _storage.DeleteAsync("auth_token");
            await _storage.DeleteAsync("auth_username");
            await _storage.DeleteAsync("auth_email");
            await _storage.DeleteAsync("auth_usertype");
            await _storage.DeleteAsync("auth_tenant");

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
