using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExpensesBot.Data;

/// <summary>
/// Case-insensitive search for any script. SQLite's <c>lower()</c> and <c>LIKE</c> fold
/// only ASCII letters, so "кофе" wouldn't find "Кофе". <see cref="ExpensesDbContext.Fold"/>
/// maps to a SQL function backed by .NET's <see cref="string.ToLowerInvariant"/>, which
/// this interceptor registers on every connection before its first query.
/// </summary>
public sealed class TextFoldingInterceptor : DbCommandInterceptor
{
    public const string FunctionName = "expenses_fold";

    public static readonly TextFoldingInterceptor Instance = new();

    private static readonly ConditionalWeakTable<SqliteConnection, object> Registered = [];

    private TextFoldingInterceptor()
    {
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Register(command.Connection);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Register(command.Connection);
        return ValueTask.FromResult(result);
    }

    // Microsoft.Data.Sqlite keeps created functions and re-applies them whenever the connection reopens.
    private static void Register(DbConnection? connection)
    {
        if (connection is SqliteConnection sqlite && !Registered.TryGetValue(sqlite, out _))
        {
            sqlite.CreateFunction(FunctionName, (string? value) => value?.ToLowerInvariant(), isDeterministic: true);
            Registered.AddOrUpdate(sqlite, Instance);
        }
    }
}
