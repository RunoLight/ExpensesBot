using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Services;
using ExpensesBot.Api.Tests.Infrastructure;
using FluentAssertions;

namespace ExpensesBot.Api.Tests.Services;

public class TelegramInitDataValidatorTests
{
    private const string BotToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";

    private readonly TelegramInitDataValidator _validator = new(TimeSpan.FromHours(1));

    [Fact]
    public void ValidSignature_ReturnsTelegramUserId()
    {
        var initData = TelegramInitData.Create(BotToken, 987654321);

        var result = _validator.TryValidate(initData, BotToken, out var userId);

        result.Should().BeTrue();
        userId.Should().Be(987654321);
    }

    [Fact]
    public void WrongBotToken_ReturnsFalse()
    {
        var initData = TelegramInitData.Create(BotToken, 987654321);

        _validator.TryValidate(initData, "654321:other-token", out var userId).Should().BeFalse();
        userId.Should().Be(0);
    }

    [Fact]
    public void ModifiedUser_ReturnsFalse()
    {
        var initData = TelegramInitData.Create(BotToken, 111).Replace("111", "222");

        _validator.TryValidate(initData, BotToken, out _).Should().BeFalse();
    }

    [Fact]
    public void ExpiredAuthDate_ReturnsFalse()
    {
        var initData = TelegramInitData.Create(BotToken, 111, authDate: DateTimeOffset.UtcNow.AddHours(-2));

        _validator.TryValidate(initData, BotToken, out _).Should().BeFalse();
    }

    [Fact]
    public void AuthDateFromTheFuture_ReturnsFalse()
    {
        var initData = TelegramInitData.Create(BotToken, 111, authDate: DateTimeOffset.UtcNow.AddMinutes(10));

        _validator.TryValidate(initData, BotToken, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("user={\"id\":123456789}")]
    [InlineData("user={\"id\":123456789}&hash=invalid")]
    [InlineData("user={\"id\":123456789}&hash=zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void MalformedInitData_ReturnsFalse(string initData)
    {
        _validator.TryValidate(initData, BotToken, out var userId).Should().BeFalse();
        userId.Should().Be(0);
    }

    [Fact]
    public void EmptyBotToken_ReturnsFalse()
    {
        var initData = TelegramInitData.Create(BotToken, 111);

        _validator.TryValidate(initData, "", out _).Should().BeFalse();
    }
}

public class ExchangeRateTableTests
{
    private static ExchangeRateTable Table(params (string From, string To, decimal Rate)[] rates) =>
        new(rates.ToDictionary(r => (r.From, r.To), r => r.Rate), DateTime.UtcNow, IsFallback: false);

    [Fact]
    public void SameCurrency_RateIsOne()
    {
        Table().GetRate("AMD", "AMD").Should().Be(1m);
    }

    [Fact]
    public void InverseRate_IsUsedWhenDirectIsMissing()
    {
        var table = Table(("USD", "AMD", 400m));

        table.GetRate("AMD", "USD").Should().Be(0.0025m);
    }

    [Fact]
    public void CrossRate_GoesThroughAmd()
    {
        var table = Table(("USD", "AMD", 400m), ("AMD", "RUB", 0.2m));

        table.GetRate("USD", "RUB").Should().Be(80m);
    }

    [Fact]
    public void Convert_RoundsToWholeMinorUnits()
    {
        var table = Table(("USD", "AMD", 387.65m));

        // 12.34 USD = 4 783.601 AMD → 478 360 minor units.
        table.Convert(1_234, "USD", "AMD").Should().Be(478_360);
    }

    [Fact]
    public void UnknownPair_ReturnsNull()
    {
        Table().Convert(100, "USD", "RUB").Should().BeNull();
    }
}
