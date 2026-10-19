# MachinScope v0.1.0-beta — Windows x64

Extract the complete archive to a writable folder and run MachinScope.exe.
The .NET and Windows App SDK runtimes are embedded and extract on first launch;
no separate .NET installation or administrator account is required.
Windows 10 build 19041+ is targeted; Windows 11 x64 is the validation host.
Windows 10 and a clean machine still require manual acceptance.

This early beta is unsigned. AitLilCat is product metadata, not an Authenticode
verified publisher. Respect Windows security decisions; do not disable protections.
Verify the ZIP against SHA256SUMS.txt using Get-FileHash -Algorithm SHA256.

Settings, local logs and startup recovery backups retain the compatible
%LOCALAPPDATA%\CORE location. Exports may contain private system information.
Windows access controls apply. Optional DNS/ICMP/TCP diagnostics contact the
requested destination only when invoked. No accounts or telemetry are required.

Read RUNTIME-TERMS.txt before use or redistribution. See LICENSE and
THIRD-PARTY-NOTICES for distribution notices and separately licensed runtime terms.
Source, documentation and issues: https://github.com/AitLilCat/MachinScope
Developed and maintained by AitLilCat, with AI-assisted development, review,
cleanup and documentation.
