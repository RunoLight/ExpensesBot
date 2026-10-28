using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace ExpensesBot.ServiceDefaults;

/// <summary>
/// Cross-cutting setup shared by the API and the Worker (the .NET Aspire
/// "service defaults" pattern): structured logging, OpenTelemetry and health checks.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public const string LiveTag = "live";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.AddStructuredLogging();
        builder.AddOpenTelemetry();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        return builder;
    }

    /// <summary>
    /// OTLP export is switched on by the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
    /// variable, which the Aspire AppHost sets for every project it starts.
    /// </summary>
    private static string? OtlpEndpoint(IHostApplicationBuilder builder) =>
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 } endpoint ? endpoint : null;

    /// <summary>
    /// Serilog replaces the default providers. Development gets readable console lines;
    /// other environments get compact JSON, one event per line, ready for a log collector.
    /// Levels come from the "Serilog" configuration section.
    /// </summary>
    private static void AddStructuredLogging(this IHostApplicationBuilder builder)
    {
        var otlpEndpoint = OtlpEndpoint(builder);
        var serviceName = builder.Environment.ApplicationName;

        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", serviceName);

            if (builder.Environment.IsDevelopment())
                logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
            else
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());

            if (otlpEndpoint is not null)
            {
                logger.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = serviceName };
                });
            }
        });
    }

    private static void AddOpenTelemetry(this IHostApplicationBuilder builder)
    {
        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.Name)
                .AddAspNetCoreInstrumentation(options =>
                    // Health probes would drown the interesting traces.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health") &&
                                                !context.Request.Path.StartsWithSegments("/alive"))
                .AddHttpClientInstrumentation(options =>
                    // Telegram URLs contain the bot token: never record them.
                    options.EnrichWithHttpRequestMessage = (activity, request) =>
                    {
                        if (request.RequestUri is { Host: "api.telegram.org" } uri)
                            activity.SetTag("url.full", $"https://api.telegram.org/bot<token>/{uri.Segments[^1]}");
                    })
                .AddEntityFrameworkCoreInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(Telemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (OtlpEndpoint(builder) is not null)
            otel.UseOtlpExporter();
    }

    /// <summary>
    /// <c>/health</c> runs every check and returns a JSON report (status per check);
    /// <c>/alive</c> only says the process responds, for liveness probes.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteReportAsync });
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = check => check.Tags.Contains(LiveTag) });

        // Which build is running: shown in the Mini App's profile next to the client's own version.
        var buildInfo = BuildInfo.FromAssembly(Assembly.Load(new AssemblyName(app.Environment.ApplicationName)));
        app.MapGet("/version", () => buildInfo).ExcludeFromDescription();
        return app;
    }

    private static Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            durationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = (long)entry.Value.Duration.TotalMilliseconds
                })
        }, cancellationToken: context.RequestAborted);
    }
}
