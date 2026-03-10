namespace Web.Services
{
    public class ApiHttpClient
    {
        private readonly HttpClient _http;
        private readonly AuthStateService _authState;

        public ApiHttpClient(HttpClient http, AuthStateService authState)
        {
            _http = http;
            _authState = authState;
        }

        public HttpClient GetClient(string tenantSubdomain = "cliente1")
        {
            // Tenant header — necessario perché il server non risolve cliente1.localhost
            _http.DefaultRequestHeaders.Remove("X-Tenant-Id");
            _http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantSubdomain);

            if (_authState.IsAuthenticated)
            {
                _http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", _authState.Token);
            }
            else
            {
                _http.DefaultRequestHeaders.Authorization = null;
            }

            Console.WriteLine($"IsAuthenticated: {_authState.IsAuthenticated}");
            Console.WriteLine($"Token null: {_authState.Token is null}");

            return _http;
        }
    }
}
