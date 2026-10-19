using ExpensesBot.Data.Entities;
using FluentAssertions;

namespace ExpensesBot.Worker.Tests;

public class BotEntryParserTests
{
    [Theory]
    [InlineData("1500 кофе", 1500, null, null, "кофе")]
    [InlineData("такси 700", 700, null, null, "такси")]
    [InlineData("1 250 продукты на неделю", 1250, null, null, "продукты на неделю")]
    [InlineData("+50000 зарплата", 50000, null, TransactionType.Income, "зарплата")]
    [InlineData("-300 руб метро", 300, "RUB", TransactionType.Expense, "метро")]
    [InlineData("20$ такси", 20, "USD", null, "такси")]
    [InlineData("$12.5 кофе", 12.5, "USD", null, "кофе")]
    [InlineData("100 долларов подарок", 100, "USD", null, "подарок")]
    [InlineData("такси 700 руб", 700, "RUB", null, "такси")]
    [InlineData("500р", 500, "RUB", null, null)]
    [InlineData("2000 драм хлеб", 2000, "AMD", null, "хлеб")]
    [InlineData("1,5к продукты", 1500, null, null, "продукты")]
    [InlineData("3k", 3000, null, null, null)]
    [InlineData("2 тыс аренда", 2000, null, null, "аренда")]
    [InlineData("1500 coffee", 1500, null, null, "coffee")]
    [InlineData("+50000 salary", 50000, null, TransactionType.Income, "salary")]
    [InlineData("20 dollars taxi", 20, "USD", null, "taxi")]
    [InlineData("taxi 700 rub", 700, "RUB", null, "taxi")]
    [InlineData("metro 300 roubles", 300, "RUB", null, "metro")]
    [InlineData("2000 dram bread", 2000, "AMD", null, "bread")]
    [InlineData("1.5k groceries", 1500, null, null, "groceries")]
    public void Parses_AmountCurrencyTypeAndNote(string text, double amount, string? currency, TransactionType? type, string? note)
    {
        BotEntryParser.TryParse(text, out var entry).Should().BeTrue();

        entry.Amount.Should().Be((decimal)amount);
        entry.Currency.Should().Be(currency);
        entry.Type.Should().Be(type);
        entry.Note.Should().Be(note);
    }

    [Fact]
    public void WordsThatOnlyLookLikeCurrencies_StayInTheNote()
    {
        BotEntryParser.TryParse("3000 рубашка", out var entry).Should().BeTrue();

        entry.Currency.Should().BeNull();
        entry.Note.Should().Be("рубашка");
    }

    [Fact]
    public void SpaceBeforeK_IsNotAMultiplier()
    {
        BotEntryParser.TryParse("500 к чаю", out var entry).Should().BeTrue();

        entry.Amount.Should().Be(500);
        entry.Note.Should().Be("к чаю");
    }

    [Theory]
    [InlineData("привет")]
    [InlineData("")]
    [InlineData("/start")]
    [InlineData("0 кофе")]
    [InlineData("кофе 10.001")]
    [InlineData("2000000000 яхта")]
    [InlineData("кофе 2 раза в день")]
    public void Rejects_MessagesWithoutAUsableAmount(string text)
    {
        BotEntryParser.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void LongNotes_AreTrimmed()
    {
        BotEntryParser.TryParse("100 " + new string('я', 300), out var entry).Should().BeTrue();

        entry.Note!.Length.Should().Be(BotEntryParser.MaxNoteLength);
    }

    [Fact]
    public void BareAmounts_KeepWorkingThroughTheHandler()
    {
        TelegramBotHandler.TryParseEntry("1 500,50", out var entry).Should().BeTrue();

        entry.Should().Be(new BotEntry(1500.50m, null, null, null));
    }
}

public class CategoryGuesserTests
{
    private static readonly List<CategoryOption> Categories =
    [
        new(1, "Продукты", "🛒"),
        new(2, "Транспорт", "🚕"),
        new(3, "Кафе", "☕"),
        new(4, "Зарплата", "💼"),
        new(5, "Спорт", "🏋️")
    ];

    [Fact]
    public void PreviousChoiceForTheSameNote_Wins()
    {
        var history = new List<(string, long)> { ("Кофе", 5), ("кофе", 3) };

        CategoryGuesser.Guess("кофе", Categories, history)!.Id.Should().Be(5, "the newest matching entry comes first");
    }

    [Fact]
    public void HistoryPointingToAnUnavailableCategory_IsIgnored()
    {
        var history = new List<(string, long)> { ("кофе", 99) };

        CategoryGuesser.Guess("кофе", Categories, history)!.Name.Should().Be("Кафе");
    }

    [Theory]
    [InlineData("продукты на неделю", "Продукты")]
    [InlineData("абонемент в спорт зал", "Спорт")]
    [InlineData("такси до аэропорта", "Транспорт")]
    [InlineData("бизнес-ланч обед", "Кафе")]
    [InlineData("аванс", "Зарплата")]
    public void NamesAndKeywords_PickACategory(string note, string expected)
    {
        CategoryGuesser.Guess(note, Categories, [])!.Name.Should().Be(expected);
    }

    // The English default categories (see MessageText.DefaultCategories).
    private static readonly List<CategoryOption> EnglishCategories =
    [
        new(11, "Groceries", "🛒"), new(12, "Transport", "🚕"), new(13, "Home", "🏠"),
        new(14, "Cafe", "☕"), new(15, "Salary", "💼"), new(16, "Other", "📦")
    ];

    [Theory]
    [InlineData("taxi to the airport", "Transport")]
    [InlineData("coffee", "Cafe")]
    [InlineData("groceries for the week", "Groceries")]
    [InlineData("rent", "Home")]
    [InlineData("salary", "Salary")]
    public void EnglishNotes_PickTheEnglishDefaults(string note, string expected)
    {
        CategoryGuesser.Guess(note, EnglishCategories, [])!.Name.Should().Be(expected);
    }

    [Fact]
    public void Suggest_PutsGuessFirst_ThenMostUsed_ThenAlphabetical()
    {
        var usage = new Dictionary<long, int> { [2] = 10, [5] = 3 };

        var suggested = CategoryGuesser.Suggest(Categories, usage, guessedId: 3, max: 4);

        suggested.Select(c => c.Id).Should().Equal(3, 2, 5, 4);
    }

    [Fact]
    public void Suggest_WithoutGuessOrUsage_IsAlphabetical()
    {
        CategoryGuesser.Suggest(Categories, new Dictionary<long, int>(), null)
            .Select(c => c.Name)
            .Should().Equal("Зарплата", "Кафе", "Продукты", "Спорт", "Транспорт");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("подарок маме")]
    public void Unknown_ReturnsNull(string? note)
    {
        CategoryGuesser.Guess(note, Categories, []).Should().BeNull();
    }
}

public class RecordCallbackTests
{
    [Fact]
    public void RoundTrips_AllFields_WithinTelegramsLimit()
    {
        var callback = new RecordCallback(TransactionType.Income, 999_999_999.99m, "USD", 1_234_567_890);

        var data = callback.Build();

        RecordCallback.TryParse(data, out var parsed).Should().BeTrue();
        parsed.Should().Be(callback);
        System.Text.Encoding.UTF8.GetByteCount(data).Should().BeLessThanOrEqualTo(64);
    }

    [Theory]
    [InlineData("tx:e:1500", TransactionType.Expense, null, null)]
    [InlineData("tx:i:1500:RUB", TransactionType.Income, "RUB", null)]
    [InlineData("tx:e:1500::7", TransactionType.Expense, null, 7L)]
    [InlineData("tx:e:1500:AMD:0", TransactionType.Expense, "AMD", null)]
    public void OptionalParts_CanBeOmitted(string data, TransactionType type, string? currency, long? categoryId)
    {
        RecordCallback.TryParse(data, out var parsed).Should().BeTrue();

        parsed.Should().Be(new RecordCallback(type, 1500m, currency, categoryId));
    }

    [Theory]
    [InlineData("tx:e:1500:EUR")]
    [InlineData("tx:e:1500:AMD:abc")]
    [InlineData("tx:e:1500:AMD:-1")]
    [InlineData("tx:e:1500:AMD:1:extra")]
    public void InvalidParts_AreRejected(string data)
    {
        RecordCallback.TryParse(data, out _).Should().BeFalse();
    }

    [Fact]
    public void TypeStep_RoundTripsAndIsNotARecordButton()
    {
        var step = new RecordCallback(TransactionType.Income, 50_000m, "AMD", 4);

        var data = step.BuildTypeStep();

        data.Should().Be("ty:i:50000:AMD:4");
        RecordCallback.TryParseTypeStep(data, out var parsed).Should().BeTrue();
        parsed.Should().Be(step);
        RecordCallback.TryParse(data, out _).Should().BeFalse("choosing the type must not record anything yet");
    }

    [Fact]
    public void LegacyConfirmButtons_StillRecordExpenses()
    {
        TelegramBotHandler.TryParseRecord("confirm:250", out var request).Should().BeTrue();

        request.Should().Be(new RecordCallback(TransactionType.Expense, 250m));
    }
}

public class JoinLinkTests
{
    [Theory]
    [InlineData("/start join_ABCD2345", true, "ABCD2345")]
    [InlineData("/start join_abcd2345", true, "abcd2345")]
    [InlineData("/start", false, "")]
    [InlineData("/start expense_1500", false, "")]
    [InlineData("/start join_SHORT", false, "SHORT")]
    public void TryReadJoinCode_AcceptsOnlyWellFormedCodes(string text, bool valid, string code)
    {
        TelegramBotHandler.TryReadJoinCode(text, out var parsed).Should().Be(valid);
        parsed.Should().Be(code);
    }
}
