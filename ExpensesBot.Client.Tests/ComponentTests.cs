using Bunit;
using ExpensesBot.Client.Components;
using ExpensesBot.Client.Models;
using FluentAssertions;

namespace ExpensesBot.Client.Tests.Components;

public class SummaryCardTests : BunitContext
{
    private static UserSummaryDto Summary(string currency = "AMD") => new(
        ByCategory: [],
        ByPeriod: [],
        TotalsByCurrency: new Dictionary<string, long> { ["AMD"] = 100_000 },
        TotalTransactions: 5,
        AveragePerDay: 3_333,
        ProjectedMonthly: 100_000,
        Mode: "month",
        DaysInPeriod: 30,
        TotalIncomeMinor: 250_000,
        TotalExpenseMinor: 100_000,
        BalanceMinor: 150_000,
        Currency: currency);

    [Fact]
    public void RendersBalanceIncomeAndExpense_InSummaryCurrency()
    {
        var cut = Render<SummaryCard>(p => p
            .Add(x => x.Summary, Summary("USD"))
            .Add(x => x.SummaryMode, "all"));

        cut.Find(".summary-card__balance-amount").TextContent.Should().Contain("USD");
        cut.Find(".summary-card__total--income").TextContent.Should().Contain("+").And.Contain("USD");
        cut.Find(".summary-card__total--expense").TextContent.Should().Contain("-").And.Contain("USD");
    }

    [Fact]
    public void Month_ShowsDateRangeAndProjection()
    {
        var cut = Render<SummaryCard>(p => p
            .Add(x => x.Summary, Summary())
            .Add(x => x.SummaryMode, "month")
            .Add(x => x.Today, new DateTime(2026, 10, 16)));

        cut.Find(".summary-card__range").TextContent.Should().Contain("1 окт – 16 окт 2026").And.Contain("5 операций");
        cut.Find(".summary-card__stats").TextContent.Should().Contain("Прогноз расходов на месяц");
    }

    [Fact]
    public void ActivePeriod_IsHighlighted()
    {
        var cut = Render<SummaryCard>(p => p
            .Add(x => x.Summary, Summary())
            .Add(x => x.SummaryMode, "30days"));

        cut.Find(".summary-card__periods button.active").TextContent.Should().Contain("30 дней");
    }

    [Fact]
    public void ClickingPeriodRaisesCallback()
    {
        string? selected = null;
        var cut = Render<SummaryCard>(p => p
            .Add(x => x.Summary, Summary())
            .Add(x => x.SummaryMode, "month")
            .Add(x => x.OnModeChanged, (string mode) => selected = mode));

        cut.FindAll(".summary-card__periods button")[2].Click();

        selected.Should().Be("all");
    }
}

public class TransactionListTests : BunitContext
{
    private static readonly List<CategoryDto> Categories = [new(5, "Food", "🍔", false), new(6, "Taxi", "🚕", false)];

    private static List<TransactionDto> Transactions() =>
    [
        new(1, 10_000, "AMD", 5, "Food", DateTime.UtcNow, DateTime.UtcNow, "Lunch", TransactionType.Expense),
        new(2, 20_000, "USD", null, null, DateTime.UtcNow, DateTime.UtcNow, "Salary", TransactionType.Income)
    ];

    private IRenderedComponent<TransactionList> RenderList(
        Action<ComponentParameterCollectionBuilder<TransactionList>>? configure = null)
    {
        var transactions = Transactions();

        return Render<TransactionList>(p =>
        {
            p.Add(x => x.Transactions, transactions)
             .Add(x => x.Categories, Categories)
             .Add(x => x.SelectedCurrency, "AMD");
            configure?.Invoke(p);
        });
    }

    [Fact]
    public void RendersRows()
    {
        RenderList().FindAll(".transaction-row").Should().HaveCount(2);
    }

    [Fact]
    public void ShowsEmptyState_WhenNoTransactions()
    {
        var cut = Render<TransactionList>(p => p
            .Add(x => x.Transactions, new List<TransactionDto>()));

        cut.Markup.Should().Contain("Пока пусто");
    }

    [Fact]
    public void CategoryFilter_HidesOtherCategories()
    {
        var cut = RenderList();

        cut.FindAll(".filter-bar select")[0].Change("5");

        cut.FindAll(".transaction-row").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Lunch");
    }

    [Fact]
    public void Search_MatchesNote()
    {
        var cut = RenderList();

        cut.Find(".filter-bar input").Input("sal");

        cut.FindAll(".transaction-row").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Salary");
    }

    [Fact]
    public void Sort_ByAmountAscending_PutsSmallestFirst()
    {
        var cut = RenderList();

        cut.FindAll(".filter-bar select")[1].Change("amount-asc");

        cut.FindAll(".transaction-row")[0].TextContent.Should().Contain("Lunch");
    }

    [Fact]
    public void NoMatches_ShowsNothingFound()
    {
        var cut = RenderList();

        cut.Find(".filter-bar input").Input("no such note");

        cut.Markup.Should().Contain("Ничего не найдено");
    }

    [Fact]
    public void ConvertsForeignCurrencyUsingApiRates()
    {
        var cut = RenderList(p => p.Add(x => x.Rates,
            new List<ExchangeRateDto> { new("USD", "AMD", 400m, DateTime.UtcNow) }));

        // 200.00 USD → ~80 000.00 AMD
        cut.FindAll(".transaction-converted").Should().ContainSingle()
            .Which.TextContent.Should().Contain("AMD");
    }

    [Fact]
    public void WithoutRates_DoesNotShowMadeUpConversion()
    {
        RenderList().FindAll(".transaction-converted").Should().BeEmpty();
    }
}

public class EditModalTests : BunitContext
{
    [Fact]
    public void EditingFieldsUpdatesTheParentForm()
    {
        var form = new TransactionFormModel { Amount = 100, Currency = "AMD", Note = "Lunch" };
        var saved = false;

        var cut = Render<EditModal>(p => p
            .Add(x => x.Form, form)
            .Add(x => x.Categories, new List<CategoryDto> { new(5, "Food", "🍔", false) })
            .Add(x => x.OnSave, () => saved = true));

        cut.Find("input#edit-amount").Change("250");
        cut.Find("input#edit-note").Change("Dinner");
        cut.Find("form").Submit();

        // The parent reads these values on save — the old modal kept a private copy.
        form.Amount.Should().Be(250);
        form.Note.Should().Be("Dinner");
        saved.Should().BeTrue();
    }

    [Fact]
    public void TypeToggleSwitchesToIncome()
    {
        var form = new TransactionFormModel { Amount = 100 };

        var cut = Render<EditModal>(p => p.Add(x => x.Form, form));
        cut.FindAll(".type-toggle button")[1].Click();

        form.Type.Should().Be(TransactionType.Income);
    }
}

public class CurrencySelectTests : BunitContext
{
    [Fact]
    public void ListsSupportedCurrencies_AndRaisesValueChanged()
    {
        string? selected = null;
        var cut = Render<CurrencySelect>(p => p
            .Add(x => x.Value, "AMD")
            .Add(x => x.ValueChanged, (string value) => selected = value));

        cut.FindAll("option").Select(o => o.TextContent).Should().Equal(Money.Currencies);

        cut.Find("select").Change("USD");

        selected.Should().Be("USD");
    }
}

public class CategorySelectTests : BunitContext
{
    private static readonly List<CategoryDto> Categories =
        [new(1, "Food", "🍔", false), new(2, "Old", "📦", true)];

    [Fact]
    public void HidesArchivedCategories_UnlessSelected()
    {
        var cut = Render<CategorySelect>(p => p.Add(x => x.Categories, Categories));
        cut.FindAll("option").Should().HaveCount(2); // empty option + Food

        cut = Render<CategorySelect>(p => p.Add(x => x.Categories, Categories).Add(x => x.Value, 2L));
        cut.FindAll("option").Should().HaveCount(3);
    }

    [Fact]
    public void EmptyOption_RaisesNull()
    {
        long? selected = 1;
        var cut = Render<CategorySelect>(p => p
            .Add(x => x.Categories, Categories)
            .Add(x => x.Value, 1L)
            .Add(x => x.ValueChanged, (long? id) => selected = id));

        cut.Find("select").Change("");

        selected.Should().BeNull();
    }
}

public class DeleteAccountModalTests : BunitContext
{
    [Fact]
    public void DeleteButton_IsEnabledOnlyAfterTypingConfirmWord()
    {
        var confirmed = false;
        var cut = Render<DeleteAccountModal>(p => p.Add(x => x.OnConfirm, () => confirmed = true));

        var deleteButton = () => cut.Find(".btn--danger");
        deleteButton().HasAttribute("disabled").Should().BeTrue();

        cut.Find("#delete-confirm-input").Input("УДАЛИТЬ");
        deleteButton().HasAttribute("disabled").Should().BeFalse();

        deleteButton().Click();
        confirmed.Should().BeTrue();
    }
}

public class GoalsTests : BunitContext
{
    [Fact]
    public void Contribution_IsConfirmedInTheGoalsCurrency()
    {
        var confirm = JSInterop.Setup<bool>("telegramApp.confirm", _ => true);
        confirm.SetResult(true);

        GoalContribution? contribution = null;
        var goal = new GoalDto(7, "Phone", 50_000, "USD", 3, "Phone", "🎯", null, 0, 0, true, DateTime.UtcNow);

        var cut = Render<Goals>(p => p
            .Add(x => x.Items, new List<GoalDto> { goal })
            .Add(x => x.OnContribute, (GoalContribution c) => contribution = c));

        cut.Find(".card__contribute input").Change("12.5");
        cut.Find(".card__contribute button").Click();

        confirm.Invocations.Should().ContainSingle()
            .Which.Arguments[0].Should().BeOfType<string>().Which.Should().Contain("USD");
        contribution.Should().Be(new GoalContribution(7, 1250));
    }
}

public class HelpModalTests : BunitContext
{
    [Fact]
    public void ExplainsBotNotifications_AndCloses()
    {
        var closed = false;
        var cut = Render<HelpModal>(p => p.Add(x => x.OnClose, () => closed = true));

        cut.Markup.Should().Contain("Уведомления от бота");
        cut.Find(".modal-card__actions button").Click();

        closed.Should().BeTrue();
    }
}
