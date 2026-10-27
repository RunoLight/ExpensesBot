using Bunit;
using ExpensesBot.Client.Components;
using ExpensesBot.Client.Localization;
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

        cut.Find(".summary-card__range").TextContent.Should().Contain("1 Oct – 16 Oct 2026").And.Contain("5 transactions");
        cut.Find(".summary-card__stats").TextContent.Should().Contain("Projected spending this month");
    }

    [Fact]
    public void InRussian_UsesRussianDatesAndPlurals()
    {
        var cut = Render<SummaryCard>(p => p
            .AddCascadingValue(UiText.Ru)
            .Add(x => x.Summary, Summary())
            .Add(x => x.SummaryMode, "month")
            .Add(x => x.Today, new DateTime(2026, 10, 16)));

        cut.Find(".summary-card__range").TextContent.Should().Contain("1 окт – 16 окт 2026").And.Contain("5 операций");
        cut.Find(".summary-card__stats").TextContent.Should().Contain("Прогноз расходов на месяц");
    }

    [Fact]
    public void PastMonth_ShowsTheWholeMonth_WithoutAProjection()
    {
        DateTime? picked = null;
        var cut = Render<SummaryCard>(p => p
            .Add(x => x.Summary, Summary())
            .Add(x => x.SummaryMode, "month")
            .Add(x => x.Month, new DateTime(2026, 9, 1))
            .Add(x => x.Today, new DateTime(2026, 10, 16))
            .Add(x => x.OnMonthChanged, (DateTime month) => picked = month));

        cut.Find(".summary-card__month-title").TextContent.Should().Be("September 2026");
        cut.Find(".summary-card__range").TextContent.Should().Contain("1 Sep – 30 Sep 2026");
        cut.Find(".summary-card__stats").TextContent.Should().NotContain("Projected");

        cut.Find("button[aria-label='Previous month']").Click();
        picked.Should().Be(new DateTime(2026, 8, 1));
    }

    [Fact]
    public void CurrentMonth_CannotStepIntoTheFuture()
    {
        var cut = Render<SummaryCard>(p => p
            .Add(x => x.Summary, Summary())
            .Add(x => x.SummaryMode, "month")
            .Add(x => x.Today, new DateTime(2026, 10, 16)));

        cut.Find("button[aria-label='Next month']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ActivePeriod_IsHighlighted()
    {
        var cut = Render<SummaryCard>(p => p
            .Add(x => x.Summary, Summary())
            .Add(x => x.SummaryMode, "30days"));

        cut.Find(".summary-card__periods button.active").TextContent.Should().Contain("30 days");
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

        cut.Markup.Should().Contain("Nothing here yet");
    }

    [Fact]
    public void CategoryFilter_AsksForThatCategory()
    {
        HistoryFilter? requested = null;
        var cut = RenderList(p => p.Add(x => x.OnFilterChanged, (HistoryFilter f) => requested = f));

        cut.FindAll(".filter-bar select")[0].Change("5");

        requested.Should().Be(new HistoryFilter(CategoryId: 5));
    }

    [Fact]
    public void Search_AsksTheApiAfterAPauseInTyping()
    {
        var requests = new List<HistoryFilter>();
        var cut = RenderList(p => p.Add(x => x.OnFilterChanged, (HistoryFilter f) => requests.Add(f)));

        cut.Find(".filter-bar input").Input("sa");
        cut.Find(".filter-bar input").Input("sal");

        cut.WaitForAssertion(() => requests.Should().ContainSingle().Which.Search.Should().Be("sal"));
    }

    [Fact]
    public void Sort_AsksForTheNewOrder()
    {
        HistoryFilter? requested = null;
        var cut = RenderList(p => p.Add(x => x.OnFilterChanged, (HistoryFilter f) => requested = f));

        cut.FindAll(".filter-bar select")[1].Change("amount-asc");

        requested!.Sort.Should().Be("amount-asc");
    }

    [Fact]
    public void TagFilter_AddsAndClearsTags()
    {
        var tags = new List<TagDto> { new(30, "отпуск", "#36A2EB", DateTime.UtcNow), new(31, "работа", "#FF6384", DateTime.UtcNow) };
        HistoryFilter? requested = null;
        var cut = RenderList(p => p
            .Add(x => x.Tags, tags)
            .Add(x => x.OnFilterChanged, (HistoryFilter f) => requested = f));

        cut.FindAll(".filter-bar__tags .tag-chip")[0].Click();
        requested!.Tags.Should().Equal(30);

        // The parent reloads with the new filter; a second tag widens it.
        cut.Render(p => p.Add(x => x.Filter, requested));
        cut.FindAll(".filter-bar__tags .tag-chip")[1].Click();
        requested.Tags.Should().Equal(30, 31);

        cut.Render(p => p.Add(x => x.Filter, requested));
        cut.Find(".filter-bar__tags button[title='Clear tags']").Click();
        requested.Tags.Should().BeEmpty();
    }

    [Fact]
    public void WithoutTags_NoTagFilterIsShown()
    {
        RenderList().FindAll(".filter-bar__tags").Should().BeEmpty();
    }

    [Fact]
    public void NoMatches_ShowsNothingFound()
    {
        var cut = Render<TransactionList>(p => p
            .Add(x => x.Transactions, new List<TransactionDto>())
            .Add(x => x.Filter, new HistoryFilter(Search: "no such note")));

        cut.Markup.Should().Contain("Nothing found");
    }

    [Fact]
    public void ShowMore_IsOfferedWhileMorePagesExist()
    {
        var loadMore = 0;
        var cut = RenderList(p => p
            .Add(x => x.HasMore, true)
            .Add(x => x.OnLoadMore, () => loadMore++));

        cut.Find(".history__more").Click();

        loadMore.Should().Be(1);
        RenderList().FindAll(".history__more").Should().BeEmpty();
    }

    [Fact]
    public void ConvertsForeignCurrencyUsingApiRates()
    {
        var cut = RenderList(p => p.Add(x => x.Rates,
            new List<ExchangeRateDto> { new("USD", "AMD", 400m, DateTime.UtcNow) }));

        // 200.00 USD → ~80 000.00 AMD
        cut.FindAll(".transaction-row__converted").Should().ContainSingle()
            .Which.TextContent.Should().Contain("AMD");
    }

    [Fact]
    public void WithoutRates_DoesNotShowMadeUpConversion()
    {
        RenderList().FindAll(".transaction-row__converted").Should().BeEmpty();
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

    [Fact]
    public void DateAndTagsAreEditable()
    {
        var form = new TransactionFormModel { Amount = 100, Date = new DateTime(2026, 10, 20), TagIds = [1] };

        var cut = Render<EditModal>(p => p
            .Add(x => x.Form, form)
            .Add(x => x.Tags, new List<TagDto> { new(1, "food", "#36A2EB", DateTime.UtcNow), new(2, "trip", "#FF6384", DateTime.UtcNow) }));

        cut.Find("input#edit-date").Change("2026-10-03");
        cut.FindAll(".tag-chip")[0].Click();
        cut.FindAll(".tag-chip")[1].Click();

        form.Date.Should().Be(new DateTime(2026, 10, 3));
        form.TagIds.Should().BeEquivalentTo([2L]);
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

        cut.Find("#delete-confirm-input").Input("DELETE");
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

public class CategoryManagerTests : BunitContext
{
    [Fact]
    public void Rename_OpensTheFormWithTheCategory_AndSavesIt()
    {
        (long Id, string Name)? saved = null;
        var cut = Render<CategoryManager>(p => p
            .Add(x => x.Categories, new List<CategoryDto> { new(5, "Food", "🍔", false) })
            .Add(x => x.OnUpdate, (long id, NewCategoryForm form) =>
            {
                saved = (id, form.Name);
                return Task.FromResult(true);
            }));

        cut.Find("button[title='Manage categories']").Click();
        cut.Find("button[title='Rename']").Click();
        cut.Find("input#cat-name").GetAttribute("value").Should().Be("Food");

        cut.Find("input#cat-name").Change("Groceries");
        cut.Find("form").Submit();

        saved.Should().Be((5L, "Groceries"));
        cut.FindAll("form").Should().BeEmpty();
    }
}

public class RecurringTransactionsTests : BunitContext
{
    [Fact]
    public void Edit_OpensTheFormWithTheRule_AndSavesIt()
    {
        (long Id, RecurringTransactionForm Form)? saved = null;
        var rule = new RecurringTransactionDto(9, 150_000, "AMD", null, null, "Rent", "Monthly", null, 0, DateTime.UtcNow, true, DateTime.UtcNow);
        var cut = Render<RecurringTransactions>(p => p
            .Add(x => x.Items, new List<RecurringTransactionDto> { rule })
            .Add(x => x.OnUpdate, (long id, RecurringTransactionForm form) =>
            {
                saved = (id, form);
                return Task.FromResult(true);
            }));

        cut.Find("button[title='Edit']").Click();
        cut.Find("input#rec-amount").GetAttribute("value").Should().Be("1500");

        cut.Find("select#rec-pattern").Change("Weekly");
        cut.Find("form").Submit();

        saved!.Value.Id.Should().Be(9);
        saved.Value.Form.Pattern.Should().Be("Weekly");
        saved.Value.Form.Note.Should().Be("Rent");
    }
}

public class ProfileTests : BunitContext
{
    [Fact]
    public void Header_OnlyOpensHelpAndTheProfile()
    {
        var opened = false;
        var cut = Render<AppHeader>(p => p
            .Add(x => x.Currency, "USD")
            .Add(x => x.OnProfile, () => opened = true));

        cut.FindAll(".app-header__actions button").Should().HaveCount(2);
        cut.Find("button[aria-label='Profile']").TextContent.Should().Contain("USD");
        cut.Find("button[aria-label='Profile']").Click();

        opened.Should().BeTrue();
    }

    [Fact]
    public void Profile_HoldsCurrencyDataAndAccountDeletion()
    {
        string? currency = null;
        var exported = false;
        var deleteRequested = false;
        var cut = Render<ProfileModal>(p => p
            .Add(x => x.Currency, "AMD")
            .Add(x => x.OnCurrencyChanged, (string value) => currency = value)
            .Add(x => x.OnExport, () => exported = true)
            .Add(x => x.OnDeleteAccount, () => deleteRequested = true));

        cut.Find("select#profile-currency").Change("USD");
        cut.Find("button:contains('Export CSV')").Click();
        cut.Find("button:contains('Delete account')").Click();

        currency.Should().Be("USD");
        exported.Should().BeTrue();
        deleteRequested.Should().BeTrue();
    }
}

public class BudgetsTests : BunitContext
{
    [Fact]
    public void EditButton_PrefillsTheFormAndSavesThroughOnUpdate()
    {
        var budget = new BudgetDto(9, 50_000, "USD", null, null, null,
            new DateTime(2026, 10, 1), new DateTime(2026, 10, 31, 23, 59, 59), true, 0, 0);

        (long Id, BudgetForm Form)? updated = null;
        var created = false;

        var cut = Render<Budgets>(p => p
            .Add(x => x.Items, new List<BudgetDto> { budget })
            .Add(x => x.OnCreate, _ => { created = true; return Task.FromResult(true); })
            .Add(x => x.OnUpdate, (id, form) => { updated = (id, form); return Task.FromResult(true); }));

        cut.Find("button[title='Edit']").Click();

        cut.Find("#budget-amount").GetAttribute("value").Should().Be("500");
        cut.Find("button[type='submit']").TextContent.Should().Contain("Save");

        cut.Find("#budget-amount").Change("750");
        cut.Find("form").Submit();

        created.Should().BeFalse();
        updated.Should().NotBeNull();
        updated!.Value.Id.Should().Be(9);
        updated.Value.Form.Amount.Should().Be(750);
        updated.Value.Form.Currency.Should().Be("USD");
        updated.Value.Form.EndDate.Should().Be(new DateTime(2026, 10, 31));
    }
}

public class TagSelectorTests : BunitContext
{
    [Fact]
    public void EditingATag_SendsTheNewNameAndColour()
    {
        TagEdit? edit = null;
        var tag = new TagDto(3, "отпуск", "#36A2EB", DateTime.UtcNow);

        var cut = Render<TagSelector>(p => p
            .Add(x => x.Tags, new List<TagDto> { tag })
            .Add(x => x.OnUpdateTag, e => { edit = e; return Task.FromResult(true); }));

        cut.Find("button[title='Edit tag']").Click();
        cut.Find("#tag-name").Change("Отпуск 2026");
        cut.Find(".color-swatch").Click();
        cut.Find(".tag-selector__form .btn--primary").Click();

        edit.Should().NotBeNull();
        edit!.Id.Should().Be(3);
        edit.Form.Name.Should().Be("Отпуск 2026");
        edit.Form.Color.Should().Be(TagColors.Next("#36A2EB"));
        cut.FindAll(".tag-selector__form").Should().BeEmpty("the form closes after saving");
    }
}

public class WalletsTests : BunitContext
{
    [Theory]
    [InlineData(1, "1 участник")]
    [InlineData(3, "3 участника")]
    [InlineData(5, "5 участников")]
    [InlineData(11, "11 участников")]
    [InlineData(21, "21 участник")]
    public void Members_UsesRussianPlurals(int count, string expected)
    {
        UiText.Ru.Members(count).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 member")]
    [InlineData(2, "2 members")]
    public void Members_UsesEnglishPlurals(int count, string expected)
    {
        UiText.En.Members(count).Should().Be(expected);
    }

    [Fact]
    public void OpeningAWallet_ShowsMembersSpendingAndOwnerInvite()
    {
        var wallet = new WalletDto(4, "Семья", "Owner", 2, "ABCD2345", DateTime.UtcNow);
        var view = new WalletView(
            new WalletDetailsDto(4, "Семья", "Owner", "ABCD2345", DateTime.UtcNow,
            [
                new(1, "Анна", "Owner", DateTime.UtcNow, true),
                new(2, "Борис", "Member", DateTime.UtcNow, false)
            ]),
            new WalletSummaryDto("AMD", "month", 0, 40_000, -40_000,
                [new(1, "Анна", 0, 30_000, 1), new(2, "Борис", 0, 10_000, 1)], [], false),
            []);

        var cut = Render<Wallets>(p => p
            .Add(x => x.Items, new List<WalletDto> { wallet })
            .Add(x => x.BotUsername, "@my_expenses_bot")
            .Add(x => x.LoadView, _ => Task.FromResult<WalletView?>(view)));

        cut.Find(".list-row button").Click();

        cut.Markup.Should().Contain("Борис").And.Contain("Анна (you)");
        cut.Find(".wallet-code").TextContent.Should().Be("ABCD2345");
        cut.Markup.Should().Contain("https://t.me/my_expenses_bot?start=join_ABCD2345");
        cut.FindAll("button[title='Remove']").Should().ContainSingle("the owner can remove others but not themselves");
    }

    [Fact]
    public void Members_SeeLeaveInsteadOfDelete()
    {
        var wallet = new WalletDto(4, "Семья", "Member", 2, null, DateTime.UtcNow);
        var view = new WalletView(
            new WalletDetailsDto(4, "Семья", "Member", null, DateTime.UtcNow, [new(2, "Борис", "Member", DateTime.UtcNow, true)]),
            new WalletSummaryDto("AMD", "month", 0, 0, 0, [], [], false),
            []);

        var cut = Render<Wallets>(p => p
            .Add(x => x.Items, new List<WalletDto> { wallet })
            .Add(x => x.LoadView, _ => Task.FromResult<WalletView?>(view)));

        cut.Find(".list-row button").Click();

        cut.Markup.Should().Contain("Leave wallet").And.NotContain("Delete wallet");
        cut.FindAll(".wallet-code").Should().BeEmpty();
    }
}

public class HelpModalTests : BunitContext
{
    [Fact]
    public void ExplainsBotNotifications_AndCloses()
    {
        var closed = false;
        var cut = Render<HelpModal>(p => p.Add(x => x.OnClose, () => closed = true));

        cut.Markup.Should().Contain("Bot notifications");
        cut.Find(".modal-card__actions button").Click();

        closed.Should().BeTrue();
    }

    [Fact]
    public void InRussian_ShowsTheRussianGuide()
    {
        var cut = Render<HelpModal>(p => p.AddCascadingValue(UiText.Ru));

        cut.Markup.Should().Contain("Уведомления от бота").And.Contain("/language");
        cut.Find(".modal-card__actions button").TextContent.Should().Be("Понятно");
    }
}

public class LanguageTests : BunitContext
{
    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("ru-RU", "ru")]
    [InlineData("en-GB", "en")]
    [InlineData("uk", "en")]
    [InlineData(null, "en")]
    public void TelegramLanguage_MapsToASupportedOne(string? code, string expected)
    {
        UiText.LanguageFromTelegram(code).Should().Be(expected);
    }

    [Fact]
    public void LanguageState_StartsInEnglish_AndNotifiesOnChange()
    {
        var state = new LanguageState();
        var changes = 0;
        state.Changed += () => changes++;

        state.Text.Should().BeSameAs(UiText.En);
        state.Set("ru");
        state.Set("ru");

        state.Text.Should().BeSameAs(UiText.Ru);
        changes.Should().Be(1, "setting the current language again is not a change");
    }

    [Fact]
    public void Profile_LanguageButtons_PickTheOtherLanguage()
    {
        string? picked = null;
        var cut = Render<ProfileModal>(p => p
            .AddCascadingValue(UiText.Ru)
            .Add(x => x.OnLanguageChanged, (string language) => picked = language));

        cut.Find(".segmented button.active").TextContent.Should().Be("Русский");
        cut.FindAll(".segmented button")[1].Click();
        picked.Should().BeNull("the current language is already selected");

        cut.FindAll(".segmented button")[0].Click();
        picked.Should().Be("en");
    }
}
