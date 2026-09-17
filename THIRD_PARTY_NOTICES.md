# Third-party notices

In portable releases, the source and license paths below are inside `THIRD-PARTY.zip`.

## PawnIO EC port module

`ec/LpcACPIEC.bin` is the unmodified signed module from
[PawnIO.Modules 0.2.9](https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.9),
archive `release_0_2_9.zip`.

- Copyright (C) 2023 namazso <admin@namazso.eu>.
- Module: LGPL-2.1-or-later; full license in `ec/source/COPYING`.
- Corresponding source, headers and build workflow are bundled in `ec/source/`.
- Source commit: `3cba9cbcbf01824c39d0bfd1ca33f72f98e9d2e6`.
- SHA-256: `C38FD116E7AFF4D1FDB0A494E296BE0A6708E5A22FC72F14587442FB7F8F7906`.
- Headers retain their own notices, including 0BSD where indicated.

The separate [PawnIO driver](https://github.com/namazso/PawnIO.Setup) must be
installed. Its [source and license](https://github.com/namazso/PawnIO) specify
GPL with the project's interface exception. The application bundles the unmodified official signed 2.2.0 installer in
`driver/PawnIO_setup.exe`. Driver source (including the pinned PawnPP submodule),
COPYING and the interface exception are bundled in `driver/PawnIO-2.2.0-source.zip`.
Source commit: `5cdf470831fdfff3f7f1d06363ca6b230f3bf35a`. The official installer
comes from https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0 .
Installer SHA-256: `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`.
No separate PawnIOLib DLL is needed by this application. The C# transport is independently written against its ABI.
The module remains an external replaceable file. Modified modules require the
upstream project's appropriate signing/development setup; the application does
not bypass stock PawnIO's signature enforcement.

## Research

[OmenMon](https://github.com/OmenMon/OmenMon) was consulted for architecture and
protocol references. No OmenMon source, DLL or WinRing0 driver is included.
The 84DB mapping was independently established from the local ACPI DSDT.
No HP binaries or decompiled HP code are distributed by this update.
