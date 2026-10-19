using Core.Domain.Models;
namespace Core.Application.Interfaces;

public interface IDeviceInspector
{
    Task<DeviceSnapshot> SnapshotAsync(bool force, CancellationToken token);
    Task<DeviceDetails> DetailsAsync(DeviceTarget target, CancellationToken token);
}
public interface IUsbInspector : IDeviceInspector { }
