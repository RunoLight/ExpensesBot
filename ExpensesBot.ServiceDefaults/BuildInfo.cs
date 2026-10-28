using System.Reflection;

namespace ExpensesBot.ServiceDefaults;

/// <summary>
/// Version of the running service: "1.0.0" plus the commit it was built from. The SDK
/// appends the commit to the informational version ("1.0.0+228c066…") when it builds
/// from a git checkout; the Docker builds pass it in as SourceRevisionId.
/// </summary>
public sealed record BuildInfo(string Version, string? Commit)
{
    public static BuildInfo FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? assembly.GetName().Version?.ToString(3)
                            ?? "0.0.0";

        var parts = informational.Split('+', 2);
        return new BuildInfo(parts[0], parts.Length == 2 && parts[1].Length > 0 ? parts[1] : null);
    }
}
