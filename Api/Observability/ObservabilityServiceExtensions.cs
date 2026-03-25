using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Api.Observability;

public static class ObservabilityServiceExtensions
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var serviceName    = builder.Configuration["Observability:ServiceName"] ?? "mysolutionhub-api";
        var serviceVersion = builder.Configuration["Observability:ServiceVersion"] ?? "1.0.0";
        var otlpEndpoint   = builder.Configuration["Observability:OtlpEndpoint"];

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName.ToLowerInvariant()
            });

        // ── Tracing ───────────────────────────────────────────────────────────
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                        // Exclude health check noise
                        o.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation(o => o.RecordException = true)
                    .AddEntityFrameworkCoreInstrumentation(o =>
                    {
                        o.SetDbStatementForText = true;
                        o.SetDbStatementForStoredProcedure = true;
                    });

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
                else if (builder.Environment.IsDevelopment())
                {
                    tracing.AddConsoleExporter();
                }
            })

        // ── Metrics ───────────────────────────────────────────────────────────
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("MySolutionHub.Api");

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
                else if (builder.Environment.IsDevelopment())
                {
                    metrics.AddConsoleExporter();
                }
            });

        // ── Logging ───────────────────────────────────────────────────────────
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resourceBuilder);
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;

            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                logging.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            }
            else if (builder.Environment.IsDevelopment())
            {
                logging.AddConsoleExporter();
            }
        });

        return builder;
    }
}
