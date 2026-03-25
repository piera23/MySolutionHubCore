using Infrastructure;
using MasterDb;
using MudBlazor.Services;
using Web.Components;
using Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddMasterDb(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<AuthStateService>();

// CookieContainer scoped: un contenitore per ogni circuito Blazor (sessione utente).
// Il refresh_token HttpOnly impostato dall'API viene conservato qui (lato server)
// e mai esposto al browser — il TokenRefreshHandler lo usa automaticamente.
builder.Services.AddScoped<System.Net.CookieContainer>();

builder.Services.AddScoped<ApiHttpClient>(sp =>
{
    var authState       = sp.GetRequiredService<AuthStateService>();
    var cookieContainer = sp.GetRequiredService<System.Net.CookieContainer>();
    var apiBaseUrl      = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";

    var handler = new Web.Services.TokenRefreshHandler(authState)
    {
        InnerHandler = new HttpClientHandler
        {
            UseCookies      = true,
            CookieContainer = cookieContainer
        }
    };

    var http = new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/")
    };

    return new ApiHttpClient(http, authState);
});

builder.Services.AddScoped<Web.Services.SignalRService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseMultiTenant();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
