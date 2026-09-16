> Hardware investigation for v0.3.0. For driver setup behavior, see [portable.md](portable.md).

# 84DB / F.19 performance readback

Verified locally on 2026-09-16: HP OMEN 15-dc0xxx, motherboard 84DB, BIOS F.19.
This mapping is specific to this hardware/firmware combination.

## Evidence

The old application implemented Eco with `SetMode(0)` and additional NVIDIA
FPS, display refresh and Windows CPU boost policies. Installed Gaming Hub
1101.2608.3.0's previously extracted local IL was also inspected: its
`PowerControlBase` includes `EcoModeNvidiaMaxFrameRate=60`, calls to
`NVAPI_SetMaxFrameRate` and `DisplayRefreshRateHelper.SetRefreshRate`.
Eco is therefore removed as a software combination, not a fourth native mode.

[OmenMon](https://github.com/OmenMon/OmenMon/tree/d89340e6d4dc9802b609390729b0bfaec05563e2)
uses an embedded C# EC implementation with a Ring0/WinRing0 kernel driver,
not a standalone EC DLL. Its newer-machine HPCM mapping at EC[0x95] cannot
be assumed correct on this machine. No OmenMon source was copied.

The running machine's DSDT was read with Windows `GetSystemFirmwareTable` and
disassembled with official ACPICA iASL 20250807. OEM ID: HPQOEM; table ID: 84DB;
AML length: 332162 bytes. SHA-256:
`1B8E7DE67A8BE9DC63C6C092A8355D249D59050A6E4AD65DF5270E709E71CA74`.

DSDT method `\_SB.WMID.GM1A` implements WMI 0x20008 / type 0x1A. It dispatches
the second payload byte: 1 sets NVPM and clears COLM; 2 clears NVPM and sets
COLM; other values clear both. EC field declarations locate **NVPM at F8 bit 1
and COLM at EC bit 0**. EC[0x95] instead contains four-bit fields MMST and DMST.

| WMI mode byte | NVPM | COLM | Hardware mode |
| --- | --- | --- | --- |
| 0 | 0 | 0 | Default |
| 1 | 1 | 0 | Performance |
| 2 | 0 | 1 | Comfort |
| — | 1 | 1 | Unknown / conflicting flags |

These are hardware policy flags, not measurements of clock speed, fan RPM or
power consumption. This is EC readback of BIOS-controlled flags, not a newly
invented WMI get-mode command. Other bits in each byte are ignored for decoding.

## Implementation and limits

`EcReader.cs` contains independently written C# device transport, ACPI RD_EC
transactions, validation and decoding. It uses PawnIO's device ABI directly,
without PawnIOLib.dll. The separate unmodified signed `LpcACPIEC.bin` allows
byte I/O only on ports 0x62/0x66. The reader sends RD_EC (0x80), never WR_EC
(0x81); BIOS mode writes remain WMI requests.

The reader acquires `Global\Access_EC`, validates I/O results and response
lengths, bounds IBF/OBF waits, and requires three consecutive identical raw-byte
pairs. It retries whole snapshots on transient timeouts and settles after writes.
No error/timeout becomes a fabricated zero or a cached UI mode.

The shared mutex coordinates cooperating user applications, not all possible
kernel/firmware clients. An initial integration trial encountered transient
contention; this motivated retries and full-byte stability checks. These checks
improve confidence but are not an atomic firmware lock. Persistent contention
is reported as unavailable. Other motherboard/BIOS versions are rejected.

Signed PawnIO 2.2.0 was installed for local testing. The official installer's
Authenticode signature was Valid (namazso.eu). Normal installation was used:
no unrestricted mode, test signing or security exclusions. Only explicit setup uses the bundled official installer; normal hardware access
does not install drivers or change their configuration. PawnIO remains installed
as a Windows driver; this is not a driver-free solution.

## Validation

Hardware-free tests cover all 65536 raw-byte combinations, the exact port
transaction sequence, stable resampling, unstable snapshots, transient raw bytes
with matching mode bits, busy-controller timeouts and driver failure propagation.

Successful integration run, UTC 2026-09-16 15:15:32–15:15:34:

| Operation | Read mode | F8 | EC |
| --- | --- | --- | --- |
| Initial | Default | 08 | 00 |
| BIOS 1 | Performance | 0A | 00 |
| BIOS 2 | Comfort | 08 | 03 |
| BIOS 0 | Default | 08 | 02 |
| Restore initial | Default | 08 | 00 |

EC=03 and EC=01 both indicate Comfort when NVPM is clear; unrelated bits may
vary. The integration test restores the original mode in `finally` and verifies
it. No software Eco policies or keyboard colors were changed by this test.
Chinese and English UI renders were visually checked.

## Reproduce

Run `build.ps1` and `tests/run.ps1` with Windows PowerShell and .NET Framework
4.x. The unit tests do not access hardware. For a read-only snapshot, run
`OMEN-Lite-Control.exe --status <absolute-output-path>` as administrator.
Exit codes: 0 known state; 1 failed read; 2 conflicting flags.

`tests/HardwareValidation.cs` is an explicit hardware test. Compile it with
the three application sources, the build script's framework references, and
`/main:OmenModeSwitcher.HardwareValidation`. Run the test as administrator from
a directory containing `ec/LpcACPIEC.bin`, passing an absolute log path.
It temporarily changes modes and restores the original one.

See [third-party notices](../THIRD_PARTY_NOTICES.md) for external components.
