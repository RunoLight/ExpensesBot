namespace ExpensesBot.Client.Models;

/// <summary>Used both for adding a new transaction and for editing an existing one.</summary>
public sealed class TransactionFormModel
{
    public double Amount { get; set; }
    public long? CategoryId { get; set; }
    public string Currency { get; set; } = "AMD";
    public string? Note { get; set; }
    public TransactionType Type { get; set; } = TransactionType.Expense;

    /// <summary>Day of the transaction in the user's time zone.</summary>
    public DateTime Date { get; set; } = DateTime.Today;

    /// <summary>Tags of the transaction being edited (the new-transaction form keeps its own selection).</summary>
    public HashSet<long> TagIds { get; init; } = [];

    /// <summary>
    /// The UTC time to send for <see cref="Date"/>, or null when the day hasn't changed:
    /// a new entry then gets the current time from the server, an edited one keeps its time.
    /// Another day is recorded at local noon, safely inside that day in any time zone.
    /// </summary>
    /// <param name="currentUtc">The edited transaction's time; null for a new one (today).</param>
    public DateTime? OccurredAtUtcFor(DateTime? currentUtc = null)
    {
        var currentDay = (currentUtc?.ToLocalTime() ?? DateTime.Now).Date;
        return Date.Date == currentDay
            ? null
            : DateTime.SpecifyKind(Date.Date.AddHours(12), DateTimeKind.Local).ToUniversalTime();
    }

    /// <summary>Shared wallet to record into; null records a personal transaction.</summary>
    public long? WalletId { get; set; }

    /// <summary>
    /// Sent as the Idempotency-Key header. The form is replaced after a successful
    /// save, so a double tap or a retry of the same entry reuses the key.
    /// </summary>
    public string IdempotencyKey { get; } = Guid.NewGuid().ToString();
}

public sealed class NewCategoryForm
{
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
}

/// <summary>Used both for creating a tag and for renaming/recolouring one.</summary>
public sealed class NewTagForm
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = TagColors.Default;

    public static NewTagForm From(TagDto tag) => new() { Name = tag.Name, Color = tag.Color };
}

/// <summary>A tag edit: which tag and its new name and colour.</summary>
public sealed record TagEdit(long Id, NewTagForm Form);

public sealed class BudgetForm
{
    public double Amount { get; set; }
    public string Currency { get; set; } = "AMD";
    public long? CategoryId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddMonths(1);

    /// <summary>The budget's values as form input (dates as whole days).</summary>
    public static BudgetForm From(BudgetDto budget) => new()
    {
        Amount = budget.AmountMinor / 100.0,
        Currency = budget.Currency,
        CategoryId = budget.CategoryId,
        StartDate = budget.StartDateUtc.Date,
        EndDate = budget.EndDateUtc.Date
    };
}

public sealed class GoalForm
{
    public string Name { get; set; } = string.Empty;
    public double TargetAmount { get; set; }
    public string Currency { get; set; } = "AMD";
    public long? CategoryId { get; set; }
    public DateTime? TargetDate { get; set; }

    public static GoalForm From(GoalDto goal) => new()
    {
        Name = goal.Name,
        TargetAmount = goal.TargetAmountMinor / 100.0,
        Currency = goal.Currency,
        CategoryId = goal.CategoryId,
        TargetDate = goal.TargetDateUtc?.Date
    };
}

public sealed class RecurringTransactionForm
{
    public double Amount { get; set; }
    public long? CategoryId { get; set; }
    public string Currency { get; set; } = "AMD";
    public string? Note { get; set; }
    public string Pattern { get; set; } = "Monthly";
    public TransactionType Type { get; set; } = TransactionType.Expense;
}
