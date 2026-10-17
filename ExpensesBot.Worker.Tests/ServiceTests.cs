using ExpensesBot.Data.Entities;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace ExpensesBot.Worker.Tests;

public class RecurrenceScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(RecurrencePattern.Daily, -1, true)]
    [InlineData(RecurrencePattern.Daily, 0, false)]
    [InlineData(RecurrencePattern.Weekly, -6, false)]
    [InlineData(RecurrencePattern.Weekly, -7, true)]
    [InlineData(RecurrencePattern.Monthly, -27, false)]
    [InlineData(RecurrencePattern.Monthly, -31, true)]
    [InlineData(RecurrencePattern.Yearly, -364, false)]
    [InlineData(RecurrencePattern.Yearly, -366, true)]
    [InlineData(RecurrencePattern.None, -1000, false)]
    public void IsDue_RespectsPattern(RecurrencePattern pattern, int lastRunDaysAgo, bool expected)
    {
        var lastRun = Now.AddDays(lastRunDaysAgo);

        RecurrenceSchedule.IsDue(pattern, lastRun, Now).Should().Be(expected);
    }

    [Fact]
    public void IsDue_DailyRunsOncePerCalendarDay()
    {
        var earlyToday = Now.Date.AddHours(1);

        RecurrenceSchedule.IsDue(RecurrencePattern.Daily, earlyToday, Now).Should().BeFalse();
        RecurrenceSchedule.IsDue(RecurrencePattern.Daily, earlyToday, Now.Date.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void AutoNote_DependsOnTransactionType()
    {
        RecurrenceSchedule.AutoNote(new RecurringTransaction { Type = TransactionType.Income })
            .Should().Be("Авто-доход");

        RecurrenceSchedule.AutoNote(new RecurringTransaction { Type = TransactionType.Expense, Note = "Аренда" })
            .Should().Be("Аренда (авто)");
    }
}

public class ExchangeRateParsingTests
{
    // Shape of https://www.cbr-xml-daily.ru/daily_json.js (trimmed).
    private const string CbrSample = """
        {
          "Date": "2026-10-16T11:30:00+03:00",
          "Valute": {
            "USD": { "CharCode": "USD", "Nominal": 1, "Value": 81.5 },
            "AMD": { "CharCode": "AMD", "Nominal": 100, "Value": 21.0 }
          }
        }
        """;

    [Fact]
    public void ParseRubPerUnit_AppliesNominal()
    {
        var rubPerUnit = ExchangeRateService.ParseRubPerUnit(JObject.Parse(CbrSample));

        rubPerUnit.Should().NotBeNull();
        rubPerUnit!["RUB"].Should().Be(1m);
        rubPerUnit["USD"].Should().Be(81.5m);
        rubPerUnit["AMD"].Should().Be(0.21m);
    }

    [Fact]
    public void CrossRates_AreDerivedFromRoubleValues()
    {
        var rubPerUnit = ExchangeRateService.ParseRubPerUnit(JObject.Parse(CbrSample))!;

        var usdToAmd = rubPerUnit["USD"] / rubPerUnit["AMD"];
        var rubToAmd = rubPerUnit["RUB"] / rubPerUnit["AMD"];

        decimal.Round(usdToAmd, 2).Should().Be(388.10m);
        decimal.Round(rubToAmd, 4).Should().Be(4.7619m);
    }

    [Fact]
    public void ParseRubPerUnit_MissingCurrency_ReturnsNull()
    {
        var json = JObject.Parse("""{ "Valute": { "USD": { "Nominal": 1, "Value": 81.5 } } }""");

        ExchangeRateService.ParseRubPerUnit(json).Should().BeNull();
    }
}

public class TelegramBotHandlerParsingTests
{
    [Theory]
    [InlineData("1500", 1500)]
    [InlineData(" 99,90 ", 99.90)]
    [InlineData("1 250.5", 1250.5)]
    public void TryParseAmount_AcceptsCommonFormats(string text, double expected)
    {
        TelegramBotHandler.TryParseAmount(text, out var amount).Should().BeTrue();
        amount.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.001")]
    [InlineData("2000000000")]
    public void TryParseAmount_RejectsInvalidInput(string text)
    {
        TelegramBotHandler.TryParseAmount(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(TransactionType.Expense)]
    [InlineData(TransactionType.Income)]
    public void RecordCallback_RoundTripsTypeAndAmount(TransactionType type)
    {
        var data = TelegramBotHandler.BuildRecordCallback(type, 1234.5m);

        TelegramBotHandler.TryParseRecordCallback(data, out var parsedType, out var amount).Should().BeTrue();
        parsedType.Should().Be(type);
        amount.Should().Be(1234.5m);
        data.Length.Should().BeLessThanOrEqualTo(64, "Telegram limits callback_data to 64 bytes");
    }

    [Fact]
    public void LegacyConfirmCallback_IsTreatedAsExpense()
    {
        TelegramBotHandler.TryParseRecordCallback("confirm:500", out var type, out var amount).Should().BeTrue();

        type.Should().Be(TransactionType.Expense);
        amount.Should().Be(500m);
    }

    [Theory]
    [InlineData("tx:x:100")]
    [InlineData("tx:e:abc")]
    [InlineData("menu_stats")]
    public void UnknownCallback_IsRejected(string data)
    {
        TelegramBotHandler.TryParseRecordCallback(data, out _, out _).Should().BeFalse();
    }
}
