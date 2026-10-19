using System.ComponentModel;
namespace Core.Application.Services;

public enum OperationErrorCategory { Validation, PermissionDenied, Unsupported, ResourceUnavailable, ObservationExpired, Cancelled, NativeProviderFailure, InternalFailure }
public sealed record OperationError(OperationErrorCategory Category, string Message);
public static class OperationErrors
{
    public static OperationError Classify(Exception error) => error switch
    {
        Core.Domain.Models.ObservationExpiredException => new(OperationErrorCategory.ObservationExpired, "The selected observation expired. Refresh the inventory and select the resource again."),
        OperationCanceledException => new(OperationErrorCategory.Cancelled, "Cancelled. Any already completed changes remain in effect."),
        UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 } => new(OperationErrorCategory.PermissionDenied, "Access denied. Check the account permissions for this operation."),
        NotSupportedException => new(OperationErrorCategory.Unsupported, "This operation is unavailable through the current provider."),
        ArgumentException or FormatException => new(OperationErrorCategory.Validation, "The input is invalid. Check the entered values and try again."),
        FileNotFoundException or DirectoryNotFoundException or Win32Exception { NativeErrorCode: 1167 } => new(OperationErrorCategory.ResourceUnavailable, "The resource is no longer available. Refresh the inventory and select it again."),
        TimeoutException or Win32Exception { NativeErrorCode: 1460 } => new(OperationErrorCategory.ResourceUnavailable, "The provider timed out. Retry after checking provider availability."),
        Win32Exception => new(OperationErrorCategory.NativeProviderFailure, "Windows could not complete the operation. Retry; technical details are in the local log."),
        IOException => new(OperationErrorCategory.ResourceUnavailable, "The resource could not be read or written. Check availability and permissions; see the local log."),
        _ => new(OperationErrorCategory.InternalFailure, "The operation failed. Technical details are in the local log.")
    };
}
