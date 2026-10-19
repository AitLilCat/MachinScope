using Core.Domain.Models;
namespace Core.Application.Services;

public sealed class CapabilityRegistry
{
    private readonly Dictionary<string, Capability> entries;
    public CapabilityRegistry(IEnumerable<Capability> capabilities) => entries = capabilities.ToDictionary(c => c.Id, StringComparer.Ordinal);
    public IReadOnlyList<Capability> Snapshot => entries.Values.ToArray();
    public Capability? Find(string id) => entries.GetValueOrDefault(id);
    public void Report(string id, CapabilityStatus status, string diagnostic)
    {
        if (!entries.TryGetValue(id, out var current)) throw new ArgumentException("Unknown capability", nameof(id));
        if (status == CapabilityStatus.Supported && !current.Available) throw new InvalidOperationException("An unimplemented provider cannot be reported as supported.");
        entries[id] = current with { ReportedStatus = status, Reason = diagnostic };
    }
    public static ActionDecision Decision(string id, IEnumerable<Capability> capabilities)
    {
        var capability = capabilities.FirstOrDefault(c => c.Id == id);
        return capability == null ? ActionDecision.Deny("Provider is not installed.") : capability.Available && capability.Status == CapabilityStatus.Supported
            ? ActionDecision.Permit : ActionDecision.Deny($"{capability.Status}: {capability.Reason}");
    }
}
