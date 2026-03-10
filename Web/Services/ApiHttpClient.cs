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

        public HttpClient GetClient()
        {
            var tenant = _authState.TenantSubdomain ?? "cliente1";

            _http.DefaultRequestHeaders.Remove("X-Tenant-Id");
            _http.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);

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

            return _http;
        }
    }
}
