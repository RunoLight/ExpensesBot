using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ExpensesBot.Api.Common;

/// <summary>
/// One token bucket per caller: the authenticated user, or the client IP for
/// anonymous requests. A bucket allows a burst (opening the Mini App fires about
/// ten requests at once) and refills at a steady rate. Health probes are exempt.
/// Limits come from the "RateLimiting" configuration section.
/// </summary>
public static class ApiRateLimiting
{
    public sealed class Options
    {
        /// <summary>Requests a caller may make at once (bucket size).</summary>
        public int TokenLimit { get; set; } = 60;

        /// <summary>Steady-state requests per minute (refill rate).</summary>
        public int TokensPerMinute { get; set; } = 120;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Read when the first request arrives, not at registration: configuration can
        // still change until the host is built (tests rely on that).
        services.Configure<Options>(configuration.GetSection("RateLimiting"));

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/alive"))
                    return RateLimitPartition.GetNoLimiter("probes");

                var options = context.RequestServices.GetRequiredService<IOptions<Options>>().Value;

                // One token every 60/TokensPerMinute seconds: 120 per minute is one every 0.5 s.
                return RateLimitPartition.GetTokenBucketLimiter(PartitionKey(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = options.TokenLimit,
                    TokensPerPeriod = 1,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1.0 / Math.Max(options.TokensPerMinute, 1)),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });

            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(ApiRateLimiting))
                    .LogWarning("Rate limit exceeded for {Partition} on {Path}",
                        PartitionKey(context.HttpContext), context.HttpContext.Request.Path);

                await context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails =
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = "Too many requests",
                            Detail = "Слишком много запросов. Подождите немного и попробуйте снова."
                        }
                    });
            };
        });
    }

    /// <summary>Runs after authentication, so signed-in callers are limited per user.</summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
