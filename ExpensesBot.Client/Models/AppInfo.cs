using System.Reflection;

namespace ExpensesBot.Client.Models;

/// <summary>A build of the client or the API: "1.0.0" and the commit it was built from.</summary>
public sealed record VersionDto(string Version, string? Commit)
{
    /// <summary>The first seven characters, as GitHub shows commits.</summary>
    public string? ShortCommit => Commit is { Length: > 7 } commit ? commit[..7] : Commit;

    /// <summary>"1.0.0 · 228c066".</summary>
    public override string ToString() => ShortCommit is null ? Version : $"{Version} · {ShortCommit}";
}

/// <summary>Where the project lives and which build of the Mini App is running.</summary>
public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/RunoLight/ExpensesBot";
    public const string IssuesUrl = RepositoryUrl + "/issues";

    /// <summary>
    /// The client's own build. The SDK appends the commit to the informational version
    /// ("1.0.0+228c066…") when it builds from a git checkout, as CI does.
    /// </summary>
    public static VersionDto Client { get; } = Read(typeof(AppInfo).Assembly);

    public static string CommitUrl(string commit) => $"{RepositoryUrl}/commit/{commit}";

    private static VersionDto Read(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var parts = informational.Split('+', 2);
        return new VersionDto(parts[0], parts.Length == 2 && parts[1].Length > 0 ? parts[1] : null);
    }
}
