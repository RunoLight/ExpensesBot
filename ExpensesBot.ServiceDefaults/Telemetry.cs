using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ExpensesBot.ServiceDefaults;

/// <summary>
/// The application's own traces and metrics. Everything is published under one
/// name, which <see cref="ServiceDefaultsExtensions"/> subscribes to.
/// </summary>
public static class Telemetry
{
    public const string Name = "ExpensesBot";

    public static readonly ActivitySource ActivitySource = new(Name);

    public static readonly Meter Meter = new(Name);

    /// <summary>Transactions recorded, tagged with <c>source</c> (api, bot, recurring) and <c>type</c>.</summary>
    public static readonly Counter<long> TransactionsCreated =
        Meter.CreateCounter<long>("expensesbot.transactions.created", description: "Transactions recorded");

    /// <summary>Outbox delivery attempts, tagged with <c>kind</c> and <c>outcome</c> (sent, retry, failed).</summary>
    public static readonly Counter<long> OutboxAttempts =
        Meter.CreateCounter<long>("expensesbot.outbox.attempts", description: "Outbox delivery attempts");

    /// <summary>Telegram updates handled by the bot, tagged with <c>type</c> (message, callback).</summary>
    public static readonly Counter<long> BotUpdates =
        Meter.CreateCounter<long>("expensesbot.bot.updates", description: "Telegram updates handled");
}
