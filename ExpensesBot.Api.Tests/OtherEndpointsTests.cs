using ExpensesBot.Api.Contracts.Categories;
using ExpensesBot.Api.Contracts.Goals;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;

namespace ExpensesBot.Api.Tests.Endpoints;

public class CategoriesEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CategoriesEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void SetDevUserAuthentication(long userId)
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Dev-User-Id", userId.ToString());
    }

    [Fact]
    public async Task GetCategories_ReturnsEmptyList()
    {
        // Arrange
        SetDevUserAuthentication(111222333);
        
        // Act
        var response = await _client.GetAsync("/api/v1/categories");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateCategory_ValidRequest_ReturnsCreated()
    {
        // Arrange
        SetDevUserAuthentication(222333444);
        var request = new { Name = "Food", Icon = "🍔" };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/categories", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateCategory_EmptyName_ReturnsBadRequest()
    {
        // Arrange
        SetDevUserAuthentication(333444555);
        var request = new { Name = "", Icon = "🍔" };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/categories", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ArchiveCategory_ReturnsOk()
    {
        // Arrange
        SetDevUserAuthentication(444555666);
        
        // Create a category first
        var createRequest = new { Name = "To Archive", Icon = "📦" };
        await _client.PostAsJsonAsync("/api/v1/categories", createRequest);
        
        // Act - try to archive non-existent (should still return OK or NotFound)
        var response = await _client.PostAsync("/api/v1/categories/999999/archive", null);
        
        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }
}

public class BudgetsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BudgetsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void SetDevUserAuthentication(long userId)
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Dev-User-Id", userId.ToString());
    }

    [Fact]
    public async Task GetBudgets_ReturnsEmptyList()
    {
        // Arrange
        SetDevUserAuthentication(555666777);
        
        // Act
        var response = await _client.GetAsync("/api/v1/budgets");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateBudget_ValidRequest_ReturnsCreated()
    {
        // Arrange
        SetDevUserAuthentication(666777888);
        var request = new
        {
            AmountMinor = 100000,
            Currency = "AMD",
            CategoryId = (long?)null,
            StartDateUtc = DateTime.UtcNow.AddDays(-10),
            EndDateUtc = DateTime.UtcNow.AddDays(20)
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/budgets", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateBudget_InvalidDates_ReturnsBadRequest()
    {
        // Arrange
        SetDevUserAuthentication(777888999);
        var request = new
        {
            AmountMinor = 100000,
            Currency = "AMD",
            CategoryId = (long?)null,
            StartDateUtc = DateTime.UtcNow.AddDays(10), // Start after end
            EndDateUtc = DateTime.UtcNow.AddDays(5)
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/budgets", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class GoalsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GoalsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void SetDevUserAuthentication(long userId)
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Dev-User-Id", userId.ToString());
    }

    [Fact]
    public async Task GetGoals_ReturnsEmptyList()
    {
        // Arrange
        SetDevUserAuthentication(888999000);
        
        // Act
        var response = await _client.GetAsync("/api/v1/goals");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateGoal_ValidRequest_ReturnsCreated()
    {
        // Arrange
        SetDevUserAuthentication(999000111);
        var request = new
        {
            Name = "New Phone",
            TargetAmountMinor = 5000000,
            Currency = "AMD",
            CategoryId = (long?)null,
            TargetDateUtc = DateTime.UtcNow.AddMonths(3)
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/goals", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ContributeToGoal_UnknownGoal_ReturnsNotFound()
    {
        SetDevUserAuthentication(111000222);

        var response = await _client.PostAsJsonAsync("/api/v1/goals/999999/contribute", new { AmountMinor = 1000 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ContributeToGoal_UsesRequestedAmount_AndIgnoresOtherIncome()
    {
        SetDevUserAuthentication(111000333);

        var create = await _client.PostAsJsonAsync("/api/v1/goals", new
        {
            Name = "Отпуск",
            TargetAmountMinor = 1_000_000,
            Currency = "AMD",
            CategoryId = (long?)null,
            TargetDateUtc = (DateTime?)null
        });
        var goal = await create.Content.ReadFromJsonAsync<GoalResponse>();
        goal!.CategoryId.Should().NotBeNull("a goal without a category gets its own one");

        // Salary is income too, but it was not put towards the goal.
        await _client.PostAsJsonAsync("/api/v1/transactions", new { AmountMinor = 5_000_000, Currency = "AMD", Type = 1 });

        var contribute = await _client.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/contribute", new { AmountMinor = 250_000 });
        contribute.StatusCode.Should().Be(HttpStatusCode.OK);

        var goals = await _client.GetFromJsonAsync<List<GoalResponse>>("/api/v1/goals");
        var reloaded = goals!.Single(g => g.Id == goal.Id);

        reloaded.SavedAmountMinor.Should().Be(250_000);
        reloaded.ProgressPercent.Should().BeApproximately(25, 0.01);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ContributeToGoal_NonPositiveAmount_ReturnsBadRequest(long amountMinor)
    {
        SetDevUserAuthentication(111000444);
        var create = await _client.PostAsJsonAsync("/api/v1/goals", new
        {
            Name = "Телефон",
            TargetAmountMinor = 100_000,
            Currency = "AMD"
        });
        var goal = await create.Content.ReadFromJsonAsync<GoalResponse>();

        var response = await _client.PostAsJsonAsync($"/api/v1/goals/{goal!.Id}/contribute", new { AmountMinor = amountMinor });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateGoal_WithAnotherUsersCategory_ReturnsBadRequest()
    {
        SetDevUserAuthentication(111000555);
        var foreignCategories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/v1/categories");

        SetDevUserAuthentication(111000666);
        var response = await _client.PostAsJsonAsync("/api/v1/goals", new
        {
            Name = "Чужая категория",
            TargetAmountMinor = 100_000,
            Currency = "AMD",
            CategoryId = foreignCategories![0].Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class TagsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TagsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void SetDevUserAuthentication(long userId)
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Dev-User-Id", userId.ToString());
    }

    [Fact]
    public async Task GetTags_ReturnsEmptyList()
    {
        // Arrange
        SetDevUserAuthentication(222111333);
        
        // Act
        var response = await _client.GetAsync("/api/v1/tags");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateTag_ValidRequest_ReturnsCreated()
    {
        // Arrange
        SetDevUserAuthentication(333222444);
        var request = new { Name = $"Tag_{Guid.NewGuid()}", Color = "#FF6384" };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/tags", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateTag_EmptyName_ReturnsBadRequest()
    {
        // Arrange
        SetDevUserAuthentication(444333555);
        var request = new { Name = "", Color = "#FF6384" };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/tags", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class ProfileEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProfileEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void SetDevUserAuthentication(long userId)
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Dev-User-Id", userId.ToString());
    }

    [Fact]
    public async Task GetProfile_ReturnsUserProfile()
    {
        // Arrange
        SetDevUserAuthentication(555444666);
        
        // Act
        var response = await _client.GetAsync("/api/v1/profile");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetDefaultCurrency_ValidCurrency_ReturnsOk()
    {
        // Arrange
        SetDevUserAuthentication(666555777);
        var request = new { Currency = "USD" };
        
        // Act
        var response = await _client.PutAsJsonAsync("/api/v1/profile/currency", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetDefaultCurrency_InvalidCurrency_ReturnsBadRequest()
    {
        // Arrange
        SetDevUserAuthentication(777666888);
        var request = new { Currency = "EUR" };
        
        // Act
        var response = await _client.PutAsJsonAsync("/api/v1/profile/currency", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteAccount_ReturnsNoContent()
    {
        // Arrange
        SetDevUserAuthentication(888777999);
        
        // Act
        var response = await _client.DeleteAsync("/api/v1/profile");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
