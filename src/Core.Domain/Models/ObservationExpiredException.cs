namespace Core.Domain.Models;

/// <summary>The selected observation no longer identifies the current resource.</summary>
public sealed class ObservationExpiredException(string message) : IOException(message);
