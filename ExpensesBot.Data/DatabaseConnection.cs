using Microsoft.Data.Sqlite;

namespace ExpensesBot.Data;

/// <summary>
/// The API and the Worker are separate processes that share one SQLite file.
/// A relative "Data Source" is resolved against the repository root (the folder
/// containing ExpensesBot.slnx) so both processes open the same file no matter
/// which working directory they were started from. Absolute paths and in-memory
/// databases are left untouched.
/// </summary>
public static class DatabaseConnection
{
    private const string SolutionFileName = "ExpensesBot.slnx";

    public static string Resolve(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ExpensesDatabase' is not configured. " +
                "Set ConnectionStrings:ExpensesDatabase in appsettings.json or the " +
                "ConnectionStrings__ExpensesDatabase environment variable.");
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;

        var isInMemory =
            builder.Mode == SqliteOpenMode.Memory ||
            string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
            dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

        if (isInMemory || string.IsNullOrWhiteSpace(dataSource) || Path.IsPathRooted(dataSource))
            return builder.ToString();

        var root = FindSolutionRoot() ?? Directory.GetCurrentDirectory();
        var fullPath = Path.GetFullPath(Path.Combine(root, dataSource));

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        builder.DataSource = fullPath;
        return builder.ToString();
    }

    private static string? FindSolutionRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        return null;
    }
}
