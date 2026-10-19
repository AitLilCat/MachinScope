# MachinScope roadmap

**One comprehensive system inspection and administration environment.**

The goal is practical visibility and control in one efficient, free, open-source
application. This roadmap describes direction, not fixed dates or guarantees.

## v0.1.0-beta — Current release candidate

Initial Windows system discovery, inspection and existing Windows tooling:
processes and resource counters, hardware/storage, files, present USB/PnP devices,
network endpoints and adapter traffic, startup, read-only services, configuration
information and native administration entry points. Filtering, sorting, properties
and exports support investigation. See [current capabilities](docs/FEATURES.md).
The beta is awaiting public publication.

## v0.2.0-beta

Stability, compatibility, improved diagnostics and user-reported fixes. Prioritize
reproducible failures and broader Windows/hardware acceptance.

## v0.3.0-beta

Expanded functionality, stronger system inspection and community-requested
improvements, selected through concrete use cases and maintainable data sources.

## v1.0.0

A stable release with broader validation, mature documentation and improved
distribution trust. Signing and reputation require legitimate infrastructure;
product metadata alone does not establish either.

## Long-term direction

Explore advanced physical-disk diagnostics, storage management and filesystem
inspection; ISO creation/inspection and bootable USB creation; virtual-machine and
virtual-disk tooling, including supported Hyper-V and VMware diagnostics; deeper
Windows administration, driver/device diagnostics and system repair entry points.

System-changing tools require accurate target identification, explicit review,
appropriate permissions and tested failure/recovery handling. Destructive media
operations require deliberate confirmation and isolated validation.

Possible Linux support depends on real native providers, a maintainable frontend
and platform-specific tests. Shared .NET code does not imply a Linux desktop release.

Advanced features are not assigned to a particular upcoming version without a
verified implementation and validation plan. They are not current beta promises.
