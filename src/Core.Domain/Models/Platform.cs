using System.Runtime.InteropServices;
namespace Core.Domain.Models;

public enum PlatformKind { Windows, Linux, Unsupported }
public sealed record PlatformIdentity(PlatformKind Kind, string Description, Architecture OSArchitecture, Architecture ProcessArchitecture)
{
    public string Version { get; init; } = "Unavailable";
    public string Hostname { get; init; } = "Unavailable";
    public static PlatformIdentity Current() => new(OperatingSystem.IsWindows() ? PlatformKind.Windows : OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Unsupported, RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture) { Version = Environment.OSVersion.VersionString, Hostname = Environment.MachineName };
}
public enum CapabilityStatus { Supported, Unsupported, RequiresElevation, TemporarilyUnavailable, Failed }
public sealed record Capability(string Id, bool Available, string Source, string Reason)
{
    public CapabilityStatus? ReportedStatus { get; init; }
    public CapabilityStatus Status => ReportedStatus ?? (Available ? CapabilityStatus.Supported : CapabilityStatus.Unsupported);
}
public sealed record PrivilegeSnapshot(string User, bool Elevated, string Mechanism);
[Flags] public enum ActionRisk { ReadOnly = 0, LowRisk = 1, Elevated = 2, Destructive = 4 }
public sealed record ActionDecision(bool Allowed, string Reason)
{
    public static ActionDecision Permit { get; } = new(true, "Available");
    public static ActionDecision Deny(string reason) => new(false, reason);
}
public enum ActionOutcome { Succeeded, Failed, Denied, Cancelled }
public sealed record ActionItemResult(string Identity, ActionOutcome Outcome, string Message);
public sealed record ActionProgress(int Completed, int Total, string CurrentIdentity);
