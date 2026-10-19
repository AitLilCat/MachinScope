# Windows APIs and ownership

| Information / action | API | Semantics / restrictions |
|---|---|---|
| Total CPU | kernel32 `GetSystemTimes` | Kernel includes idle; delta calculation; first sample unavailable |
| Physical RAM / uptime | `GlobalMemoryStatusEx`, `GetTickCount64` | Native memory snapshot, monotonic boot uptime |
| Process snapshot | `System.Diagnostics.Process`, Toolhelp `Process32FirstW/NextW` | Exited/inaccessible processes handled independently; safe snapshot handle disposed |
| Process I/O | `OpenProcess`, `GetProcessIoCounters` | `SafeProcessHandle`; byte rates use monotonic elapsed time; may include non-disk I/O |
| Process controls | `GetProcessTimes`, `IsProcessCritical`, `QueryFullProcessImageNameW`, `TerminateProcess`, `SetPriorityClass`, `SetProcessAffinityMask` | Same handle validates and mutates; no critical/system operations; rights errors surfaced |
| Process properties | Process threads/modules; `Win32_Process`, `Win32_Service` | Native access restriction and cross-architecture/provider limits apply |
| Hardware metadata | `Win32_Processor`, `Win32_VideoController`, `Win32_DiskDrive` | WMI read on navigation/refresh, not recurring one-second work |
| CPU/memory performance | `Win32_PerfFormattedData_Counters_ProcessorInformation`, `Win32_PerfFormattedData_PerfOS_Memory` | WMI formatted counters; providers/counters can be missing |
| Storage mapping | `Win32_DiskDrive`, `Win32_DiskPartition`, `Win32_LogicalDisk`, `Win32_LogicalDiskToPartition` | Association references displayed without guessing drive→disk correspondence |
| Storage-provider inventory | `root/Microsoft/Windows/Storage:MSFT_PhysicalDisk` | Bus/media/health values explicitly labelled provider codes; not SMART health evidence |
| Disk counters | `Win32_PerfFormattedData_PerfDisk_PhysicalDisk` | Instance identity retained; counter time is not presented as an invented active-time bound |
| Files | Streaming .NET filesystem enumeration | Reparse skipped; per-depth enumerators disposed; cancellation between entries |
| Duplicates/hard links | SHA-256 + byte comparison; `GetFileInformationByHandle` | Volume serial + file index identify hard-link aliases; missing identities produce errors |
| File locks | Restart Manager `RmStartSession`, `RmRegisterResources`, `RmGetList`, `RmEndSession` | Session ended in finally; no shutdown/restart; empty list is not proof of unlocked file |
| Recycle | `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile` with `SendToRecycleBin` | Windows shell semantics; user dialogs; no permanent-delete fallback |
| Properties / Explorer | `ShellExecuteExW` with properties verb, `ProcessStartInfo.ArgumentList` | File paths are not interpolated into shell command text |
| Adapters | `NetworkInterface.GetAllNetworkInterfaces`, IP properties/statistics | Removed adapter exceptions handled; aggregate deltas reset on adapter removal/counter reset |
| Endpoint owners | IP Helper `GetExtendedTcpTable/GetExtendedUdpTable` | IPv4 and IPv6 layouts parsed independently; bounded allocation, retry and finally-free unmanaged buffers |
| Diagnostics | `Dns`, `Ping`, `TcpClient` | Single-host, cancellation, 10s timeout or max 20-hop/60s IPv4 trace |
| Startup | CurrentUser/LocalMachine registry views, startup folders, Win32_Service | Inventory plus reviewed current-user Run recovery controls; other sources remain read-only; configuration differs from effective StartupApproved state |

Native declarations are isolated in `Core.Windows.Native`, `FileLocks` and `ShellActions`. No unsafe C# blocks or native drivers are used. Snapshot/process/file handles have documented owners and deterministic disposal. WMI objects/searchers are disposed. Endpoint tables own a bounded HGlobal allocation, freed on every exit path. Unsupported or denied providers remain visible as errors/notices; they are never inferred to be zero, healthy, safe or inactive.

Build/deployment reference: [Microsoft unpackaged WinUI deployment](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app) and [Windows App SDK self-contained deployment](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps). Framework-dependent and self-contained deployments are different; this project supports both self-contained single-file publication and a folder distribution. See [BUILDING.md](../BUILDING.md).

## Storage, file properties and window integration

- [WinUI title-bar customization](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar?tabs=winui3): SetTitleBar/ExtendsContentIntoTitleBar retain native caption controls. The desktop uses a native SystemParametersInfo high-contrast query because a WinRT event subscription failed in the unpackaged host.
- [MSFT_Disk](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk) and MSFT_Partition: unique IDs, system/boot flags and GPT partition roles; legacy Win32 associations provide drive-letter links. Unknown topology does not authorize mutation.
- [MSFT_StorageReliabilityCounter](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter) through [the physical-disk association](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisktostoragereliabilitycounter): optional raw reliability fields after unique-ID/serial correlation. No SMART/NVMe protocol decoding is claimed.
- [FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info): GetFileInformationByHandleEx supplies per-object allocation/length/link/deletion information; unavailable calls do not fabricate values. Handle-based file identity and timestamps bracket explicit checksum reads.
- Win32_NTLogEvent: read-only, last-24-hour storage-source events capped at 100; the UI explicitly does not assert exclusive attribution to the selected disk.

Native entry points additionally live in the dedicated DiskInspector/FilePropertiesInspector and Windows-host chrome file. Focused application contracts hide these implementations from inspection workflows.

## Native device inspection

SetupDiGetClassDevs/SetupDiEnumDeviceInfo, SetupDiGetDeviceRegistryProperty and SetupDiGetDeviceProperty provide present devnodes and native metadata. SafeHandle owns the HDEVINFO set. CM_Get_Parent/CM_Get_Device_ID and CM_Get_DevNode_Status provide relationships/status. CM_Register_Notification watches interface and instance events; callback delegates remain rooted until CM_Unregister_Notification completes. SDK headers supply structure layouts and property GUID/type definitions. Driver values are queried lazily. Exact Win32_DiskDrive PNPDeviceID plus devnode ancestry joins the existing protected disk inventory.

References: [SetupAPI properties](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetdeviceregistrypropertya), [notification filters](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/ns-cfgmgr32-cm_notify_filter), [notification payloads](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/ns-cfgmgr32-cm_notify_event_data), [notification actions](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/ne-cfgmgr32-cm_notify_action).
