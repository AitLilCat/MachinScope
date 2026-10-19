namespace Core.Domain.Models;
public enum ConnectionEventKind { Appeared, Disappeared, StateChanged }
public sealed record ConnectionEvent(long Sequence, DateTimeOffset Time, ConnectionEventKind Kind, ConnectionRow Connection, string? PreviousState);
