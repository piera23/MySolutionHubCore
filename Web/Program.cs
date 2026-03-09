using Infrastructure;
using MasterDb;
using Web.Components;

var builder = WebApplication.CreateBuilder(args);

// ── Servizi ──────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMasterDb(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructure(builder.Configuration);

// ── App ──────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Tenant resolution PRIMA dei componenti Blazor
app.UseMultiTenant();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();