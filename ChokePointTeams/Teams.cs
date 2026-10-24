using System.Collections.Concurrent;

/// <summary>Decides whether a process belongs to Microsoft Teams (new Teams "ms-teams", classic "Teams").</summary>
static class TeamsFilter
{
    static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase) { "ms-teams", "teams", "msteams" };
    static readonly ConcurrentDictionary<string, bool> cache = new();

    // appKey = lowercase exe path (or process name), as produced by Apps.Resolve
    public static bool IsTeams(string appKey) =>
        cache.GetOrAdd(appKey, k => Names.Contains(Path.GetFileNameWithoutExtension(k)));

    public static bool IsTeamsPid(int pid) => IsTeams(Apps.Resolve(pid).Key);
}
