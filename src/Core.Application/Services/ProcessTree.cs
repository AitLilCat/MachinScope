using Core.Domain.Models;
namespace Core.Application.Services;
public static class ProcessTree
{
    public static IReadOnlyList<ProcessSample> ObservedDescendants(ProcessSample root, IReadOnlyList<ProcessSample> snapshot)
    {
        if (root.Started == null || !snapshot.Any(p => p.Identity == root.Identity)) throw new InvalidOperationException("Root identity is missing or changed.");
        var result = new List<ProcessSample>(); var seen = new HashSet<string>();
        void Visit(ProcessSample p) { if (!seen.Add(p.Identity)) return; foreach (var child in snapshot.Where(c => c.ParentId == p.Id && c.Id != p.Id && c.Started >= p.Started)) Visit(child); result.Add(p); }
        Visit(root); return result; // Children first. No descendants created after this snapshot are included.
    }
}
