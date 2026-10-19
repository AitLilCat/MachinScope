# Current capabilities

This reference describes the v0.1.0-beta implementation. Provider access varies
with Windows version, edition, hardware, permissions and target lifetime.

| Area | Implemented | Boundaries |
| --- | --- | --- |
| Processes | Enumeration, creation identities, parent/child relationships, CPU/memory/I/O samples, threads/modules/services, executable/user information where readable, property tabs | Protected/exited metadata may be unavailable; I/O counters may include more than physical-disk traffic; handle details expose count only |
| Process controls | Reviewed end, priority and affinity; restart of standard-user processes launched through MachinScope in the current session | Creation identity and native protections rechecked; system/critical/self/Windows targets refused; no protection bypass or guessed restart command |
| Performance | CPU/memory counters, supported disk/GPU/interface counters, bounded history and resource views | First/reset/missing rates unavailable; no invented temperature/power or aggregate GPU utilization |
| Storage | Physical disks, partitions, volumes, mappings, properties, supported reliability counters and storage events | Provider health is not decoded SMART/NVMe; events are not guaranteed per-disk attribution; no mutation, imaging or surface tests |
| Files | Browsing/history, native properties/ACL information, explicit hashes, cancellable size/error scans and verified duplicate analysis | Reparse traversal skipped; logical-byte totals differ from allocated space; hard links excluded from duplicate recovery estimates |
| File operations | Reviewed individual copy/move/rename/recycle, open, folder/properties and Restart Manager file-user query | No overwrite, permanent-delete fallback, forced unlock or automatic/bulk cleanup; concurrent mutable-path risks remain |
| USB/PnP devices | Present SetupAPI inventory, USB ancestry/controllers/hubs, hardware IDs/VID/PID, driver properties and storage relationships, native notifications and bounded session observations | No complete remembered non-present inventory; no inferred physical empty ports, speed or serial; no eject/disable backend |
| Networking | IPv4/IPv6 TCP/UDP endpoints, states, supported process identity association, native TCP bind timestamps where exposed, bounded observation timeline | Direction unknown without evidence; polling can miss transitions; UDP has no remote-connection semantics; no packet capture or safe direct close |
| Adapters/traffic | Interface addresses/configuration, cumulative counters and adapter-level byte/packet rates | Not per-process or per-connection bandwidth; first/down/reset rates unavailable |
| Diagnostics | Explicit DNS/reverse DNS, ICMP ping, TCP and bounded IPv4 traceroute | User-requested destination only; DNS uses configured resolvers; no automatic background probing |
| Startup/services | Registry/folder/service startup inventory, read-only service details; opt-in current-user Run disable/restore with exact backup | HKLM/RunOnce/folders/services/StartupApproved remain read-only; logon configuration differs from running state |
| Windows/tools | OS/token information and searchable native administration entry points with availability checks | Opening a tool grants no privileges; utility availability varies by edition/policy |
| Tables/settings | Filtering, sorting, columns, multi-selection, clipboard, properties, scoped CSV/JSON export; local theme/refresh/log/diagnostic preferences | Exported information may be private; diagnostics are local and bounded |

System discovery is the current focus. ISO/media, advanced storage management,
virtualization tooling and platform expansion are [roadmap directions](../ROADMAP.md).
For action safeguards see [SECURITY.md](../SECURITY.md).
