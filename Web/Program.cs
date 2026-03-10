using Infrastructure;
using MasterDb;
using MudBlazor.Services;
using Web.Components;
using Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddMasterDb(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<ApiHttpClient>(sp =>
{
    var authState = sp.GetRequiredService<AuthStateService>();
    var http = new HttpClient
    {
        BaseAddress = new Uri("http://localhost:5000")
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