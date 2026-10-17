namespace ExpensesBot.Client.Models;

/// <summary>Used both for adding a new transaction and for editing an existing one.</summary>
public sealed class TransactionFormModel
{
    public double Amount { get; set; }
    public long? CategoryId { get; set; }
    public string Currency { get; set; } = "AMD";
    public string? Note { get; set; }
    public TransactionType Type { get; set; } = TransactionType.Expense;
}

public sealed class NewCategoryForm
{
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
}

public sealed class NewTagForm
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = TagColors.Default;
}

public sealed class BudgetForm
{
    public double Amount { get; set; }
    public string Currency { get; set; } = "AMD";
    public long? CategoryId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddMonths(1);
}

public sealed class GoalForm
{
    public string Name { get; set; } = string.Empty;
    public double TargetAmount { get; set; }
    public string Currency { get; set; } = "AMD";
    public long? CategoryId { get; set; }
    public DateTime? TargetDate { get; set; }
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
