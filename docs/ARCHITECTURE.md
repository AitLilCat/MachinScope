# Architecture

MachinScope is an unpackaged WinUI 3 desktop application on .NET 10. The Windows
host composes the existing native providers through dependency injection.

| Project | Responsibility | Target |
| --- | --- | --- |
| Core.Domain | Models, identities, snapshots, preferences, capabilities and outcomes | net10.0 / AnyCPU |
| Core.Application | Contracts, routes, tables, queries, projections, orchestration and action policy | net10.0 / AnyCPU |
| Core.Persistence | Atomic local settings, recovery and file logging | net10.0 / AnyCPU |
| Core.Windows | Win32, WMI, SetupAPI, IP Helper, registry, filesystem and shell providers | Windows / x64 |
| Core.Desktop | WinUI views, controls, pickers, dialogs and composition | Windows / x64 |

The desktop references Windows and Persistence; both use Application and Domain.
`Core.Windows.WindowsServices.AddCoreWindows` registers the platform before
constructing collectors. Focused process/performance/network/storage/startup/
service interfaces share one SystemInspector. Device/USB interfaces share one
WindowsDeviceInspector. Providers are owned by the DI container.

## Collection, caching and cancellation

`RefreshCoordinator` runs one active collection and retains one replaceable
pending request. Collection begins from the owning synchronization context;
expensive native work uses worker tasks. Navigation/manual refresh advance a
revision and reject obsolete results. Exclusive actions serialize against refresh.
Cancellation does not imply that a completed mutation has been reversed.

The 1/2/5-second preference drives active-page sampling. Inactive-window pausing
is configurable. Slow storage/startup inventory is navigation/manual work.
Device enumeration is notification-invalidated with a timed fallback. Properties
and action preconditions revalidate current targets instead of trusting UI caches.

`AsyncSnapshotCache` serializes collection, uses monotonic expiry and retains only
the last successful snapshot; cancelled/failed collections do not replace it.
Storage and native administration catalogs cache observations for 30 seconds.
`StorageQuerySession` shares identical read-only WMI queries within a collection;
fresh detail/action checks remain outside that session. Bounded buffers hold
resource history, device signals and connection observations.

Shutdown stops timers, cancels collection/operations, closes tracked dialogs,
detaches subscriptions and drains worker/filter/cache work before disposing native
providers and the application services. Native handles and buffers have explicit
owners; see [Windows APIs](WINDOWS_APIS.md).

## Navigation and presentation

`NavigationRegistry` separates stable persisted route IDs from labels and host
rendering. `NavigationCoordinator` owns bounded history, selection and independent
category expansion. Unknown/unavailable saved routes fall back safely; legacy IDs
retain aliases. Homepages project current/cached observations without running
independent disk/device scans or network diagnostics.

MainWindow partials adapt routes to inspectors. The shared virtualized table engine
handles queries, sorting, columns, identity-based selection, clipboard and exports.
Filtering is coalesced/debounced; reconciled row objects preserve identity, with
bounded reset behavior for heavy structural churn. Layout/schema work remains
separate. Inspector descriptions distinguish snapshot data from live observations
and unsupported fields.

## Actions and capabilities

`CapabilityRegistry` reports provider availability; registration does not guarantee
access to every target. `ToolRegistry` and `AdministrationCatalog` describe
inspection routes and allowlisted native Windows entry points. Availability is
rechecked before launch. Launch paths resolve from Windows directories, arguments
are separate, and native tools enforce their own permissions/UAC.

`ActionService` records per-target results and requires confirmed identities for
destructive actions. Native process validation/mutation share a handle. File and
startup controls retain source/recovery checks. Service, disk and device mutation
providers are unavailable. See [security boundaries](../SECURITY.md).

## Compatibility and extension

Internal Core namespaces, resource keys, solution identifiers, serialized routes
and `%LOCALAPPDATA%\CORE` settings/recovery paths are intentional compatibility
contracts. Do not rename them as cosmetic cleanup.

The compatibility facade and a string-based MainWindow adapter remain. Some
contracts expose presentation tables rather than normalized native records.
WMI cancellation cannot abort every provider call immediately. A future provider
must preserve honest unavailable states and enforce action policy at execution,
not rely on catalog metadata as authorization. Linux needs real providers and a
frontend; see [platform support](PLATFORMS.md).
