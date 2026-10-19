using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace ExpensesBot.Api.Tests;

/// <summary>A tiny bucket that barely refills, so a handful of requests hits the limit.</summary>
public class RateLimitedWebApplicationFactory : CustomWebApplicationFactory
{
    public const int TokenLimit = 3;

    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:TokenLimit"] = TokenLimit.ToString(),
        ["RateLimiting:TokensPerMinute"] = "1"
    };
}

public class RateLimitingTests(RateLimitedWebApplicationFactory factory) : IClassFixture<RateLimitedWebApplicationFactory>
{
    [Fact]
    public async Task ExceedingTheBucket_Returns429WithRetryAfter()
    {
        var client = factory.CreateClientForDevUser(300_001);

        for (var i = 0; i < RateLimitedWebApplicationFactory.TokenLimit; i++)
            (await client.GetAsync("/api/v1/categories")).StatusCode.Should().Be(HttpStatusCode.OK);

        var limited = await client.GetAsync("/api/v1/categories");

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        var problem = await limited.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(429);
    }

    [Fact]
    public async Task EachUser_HasTheirOwnBucket()
    {
        var greedy = factory.CreateClientForDevUser(300_002);
        for (var i = 0; i <= RateLimitedWebApplicationFactory.TokenLimit; i++)
            await greedy.GetAsync("/api/v1/categories");

        var other = factory.CreateClientForDevUser(300_003);

        (await other.GetAsync("/api/v1/categories")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthProbes_AreNeverLimited()
    {
        var client = factory.CreateClient();

        for (var i = 0; i < RateLimitedWebApplicationFactory.TokenLimit * 3; i++)
            (await client.GetAsync("/alive")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
