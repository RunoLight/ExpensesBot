using Asp.Versioning;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace ExpensesBot.Api.Common;

/// <summary>
/// API versioning and its documentation. The version is part of the URL
/// (<c>/api/v1/transactions</c>), so it is visible in logs, links and caches.
/// Every version gets its own OpenAPI document (<c>/openapi/v1.json</c>) and the
/// Scalar UI at <c>/scalar</c>.
/// </summary>
public static class ApiDocumentation
{
    public static readonly ApiVersion V1 = new(1, 0);

    public static IServiceCollection AddVersionedApi(this IServiceCollection services)
    {
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = V1;
                options.ReportApiVersions = true; // api-supported-versions response header
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'V"; // document names: v1, v2…
                options.SubstituteApiVersionInUrl = true; // documents show /api/v1/…, not /api/v{version}/…
            })
            .AddOpenApi();

        services.ConfigureAll<OpenApiOptions>(options => options.AddDocumentTransformer(DescribeAuthentication));

        return services;
    }

    /// <summary>All versioned endpoints are mapped into this group: <c>/api/v1/…</c>.</summary>
    public static RouteGroupBuilder MapVersionedApi(this WebApplication app) =>
        app.NewVersionedApi("ExpensesBot").MapGroup("/api/v{version:apiVersion}").HasApiVersion(V1);

    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi().WithDocumentPerVersion();
        app.MapScalarApiReference(options => options
            .WithTitle("ExpensesBot API")
            .AddPreferredSecuritySchemes(app.Environment.IsDevelopment() ? DevUserScheme : TelegramScheme));

        return app;
    }

    private const string TelegramScheme = "TelegramInitData";
    private const string DevUserScheme = "DevUser";

    /// <summary>
    /// Declares how to authenticate, so Scalar can send requests: Telegram initData
    /// in "Authorization: tma …" and, in Development, the X-Dev-User-Id shortcut.
    /// </summary>
    private static Task DescribeAuthentication(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken ct)
    {
        var development = context.ApplicationServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[TelegramScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = "Telegram Mini App initData: `tma <initData>`."
        };

        var schemes = new List<string> { TelegramScheme };

        if (development)
        {
            document.Components.SecuritySchemes[DevUserScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Dev-User-Id",
                Description = "Development only: any Telegram user id, e.g. 123456789."
            };
            schemes.Add(DevUserScheme);
        }

        // Either scheme is enough: one requirement object per scheme means "or".
        document.Security = schemes
            .Select(scheme => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(scheme, document)] = [] })
            .ToList();

        return Task.CompletedTask;
    }
}
