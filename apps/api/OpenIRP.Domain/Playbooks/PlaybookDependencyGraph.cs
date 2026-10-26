namespace OpenIRP.Domain.Playbooks;

public static class PlaybookDependencyGraph
{
    public static bool IsAcyclic(IEnumerable<string> codes, IEnumerable<(string From, string To)> dependencies)
    {
        var degree = codes.ToDictionary(c => c, _ => 0, StringComparer.Ordinal);
        var outgoing = degree.Keys.ToDictionary(c => c, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to) in dependencies)
        {
            if (!degree.ContainsKey(from) || !degree.ContainsKey(to) || from == to) return false;
            outgoing[from].Add(to); degree[to]++;
        }
        var ready = new Queue<string>(degree.Where(p => p.Value == 0).Select(p => p.Key));
        var visited = 0;
        while (ready.TryDequeue(out var code))
        {
            visited++;
            foreach (var successor in outgoing[code])
                if (--degree[successor] == 0) ready.Enqueue(successor);
        }
        return visited == degree.Count;
    }
}
