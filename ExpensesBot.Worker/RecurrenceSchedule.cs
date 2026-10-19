using ExpensesBot.Data.Entities;

namespace ExpensesBot.Worker;

public static class RecurrenceSchedule
{
    /// <summary>
    /// Whether a recurring rule last executed at <paramref name="lastExecutedUtc"/>
    /// should run again at <paramref name="nowUtc"/>.
    /// </summary>
    public static bool IsDue(RecurrencePattern pattern, DateTime lastExecutedUtc, DateTime nowUtc) =>
        pattern switch
        {
            RecurrencePattern.Daily => nowUtc.Date > lastExecutedUtc.Date,
            RecurrencePattern.Weekly => nowUtc.Date >= lastExecutedUtc.Date.AddDays(7),
            RecurrencePattern.Monthly => nowUtc >= lastExecutedUtc.AddMonths(1),
            RecurrencePattern.Yearly => nowUtc >= lastExecutedUtc.AddYears(1),
            _ => false
        };
}
