using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using FluentAssertions;

namespace ExpensesBot.Worker.Tests;

public class LocalizationTests
{
    [Fact]
    public void Money_UsesEachLanguagesSeparators()
    {
        MessageText.En.Money(150_000_00, "AMD").Should().Be("150,000.00 AMD");
        MessageText.Ru.Money(150_000_00, "AMD").Should().Be("150 000,00 AMD");
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("en", "en")]
    [InlineData(null, "en")]
    [InlineData("xx", "en")]
    public void BotText_FallsBackToEnglish(string? language, string expected)
    {
        BotText.For(language).Language.Should().Be(expected);
    }

    [Fact]
    public void Notifications_AreWrittenInTheRecipientsLanguage()
    {
        MessageText.En.DailyReminder("Ann", 1_500_00, "AMD").Should().Contain("Good evening, Ann").And.Contain("1,500.00 AMD");
        MessageText.Ru.DailyReminder("Аня", 1_500_00, "AMD").Should().Contain("Добрый вечер, Аня");

        MessageText.En.BudgetAlert("Cafe", 90_00, 100_00, "USD").Should().Contain("90%").And.Contain("“Cafe”");
        MessageText.Ru.BudgetAlert("Кафе", 90_00, 100_00, "USD").Should().Contain("90%").And.Contain("«Кафе»");
    }

    [Fact]
    public void Recorded_DescribesTheEntryInEachLanguage()
    {
        BotText.En.Recorded(TransactionType.Expense, "1500 AMD", "☕ Cafe", "coffee")
            .Should().StartWith("✅ Expense 1500 AMD recorded · ☕ Cafe · coffee");
        BotText.Ru.Recorded(TransactionType.Income, "1500 AMD", null, null)
            .Should().StartWith("✅ Доход 1500 AMD записан.");
    }

    [Fact]
    public void Hints_ShowExamplesTheParserUnderstands()
    {
        foreach (var text in new[] { BotText.En, BotText.Ru })
        {
            var examples = text.EntryHint.Split('\n').Skip(1).Select(line => line.TrimStart('•', ' ').Split(" — ")[0].Split(" or ")[0].Split(" или ")[0]);
            examples.Where(example => !TelegramBotHandler.TryParseEntry(example, out var _)).Should().BeEmpty("every example in the hint must parse");
        }
    }
}
