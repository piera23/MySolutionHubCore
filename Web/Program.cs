using Infrastructure;
using MasterDb;
using MudBlazor.Services;
using Web.Components;
using Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddHttpClient<ApiHttpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]
        ?? "http://localhost:5000");
});
builder.Services.AddScoped<Web.Services.AuthStateService>();
builder.Services.AddScoped<Web.Services.ApiHttpClient>(sp =>
{
    var authState = sp.GetRequiredService<Web.Services.AuthStateService>();
    var http = new HttpClient
    {
        BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]
            ?? "http://cliente1.localhost:5000")
    };
    return new Web.Services.ApiHttpClient(http, authState);
});
builder.Services.AddMasterDb(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructure(builder.Configuration);

// HttpClient per chiamare le API
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]
        ?? "http://localhost:5000")
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseMultiTenant();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();