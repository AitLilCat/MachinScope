namespace Core.Domain.Models;
public sealed record FileTarget(string Path, string Identity, long? Length, DateTimeOffset Modified, bool Directory);
public sealed record FileDetails(FileTarget Target, IReadOnlyList<InspectionTable> Tabs);
