namespace ExpensesBot.Client.Models;

/// <summary>Used both for adding a new transaction and for editing an existing one.</summary>
public sealed class TransactionFormModel
{
    public double Amount { get; set; }
    public long? CategoryId { get; set; }
    public string Currency { get; set; } = "AMD";
    public string? Note { get; set; }
    public TransactionType Type { get; set; } = TransactionType.Expense;

    /// <summary>Shared wallet to record into; null records a personal transaction.</summary>
    public long? WalletId { get; set; }
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
