EA SPORTS UFC 6 folder installer

Built from scratch. No executable, code or extraction tools from the reference
installer are used. The source folder is packed in full, with no emulator
configuration, game launcher or file modifications.

PUBLIC SOURCE REPOSITORY
This repository contains installer source code and its public build assets only.
It does not include EA SPORTS UFC 6 game files, data.bin, Alter Egos, the private
package-signing key, generated installers or local compression reports. Supply
only game files and optional content that you are authorized to use and share.

Distribution: setup.exe and data.bin in the same folder. Alternatively, use
setup.exe with data.bin.header and a complete numbered payload set: data.bin.001,
data.bin.002, and so on.
setup.exe is generic: game version, sizes, components and the accepted payload
SHA-256 are read from a signed manifest inside data.bin, not embedded in the EXE.
The EXE contains the public verification key. Packages signed with the same key
and supported format can be updated without rebuilding the installer.
The 64 KiB signed header is followed by the unchanged 7z payload. Metadata is
verified before display; the payload hash is checked before any game extraction.
Missing, unsigned, modified or untrusted packages cannot be installed. Numbered
parts are direct 7z volumes: they are extracted without rebuilding data.bin or
making a second compressed copy. The signed data.bin.header and every payload
part must be present; the payload is verified by the same signature and SHA-256
checks as a single data.bin.
The interface has three steps: Game setup, Optional tools, and Install.
Back/Next preserves selections. The final page summarizes the chosen destination,
game options and downloads, with performance controls, progress and Play now.
Navigation is locked while installing; Play now becomes available on completion.
PPSA23566.rar at the project root is packaged inside data.bin as the optional
Alter Egos component. It is never embedded in setup.exe. The installer applies
it only when selected and leaves its internal payload folder out of the game.
Keep the RAR with the packing sources; do not distribute it as a third file.
Updating this component changes data.bin; setup-only rebuilds need no RAR file.
The Xbox icon mod's standalone v1.0.1 patcher is also embedded in setup.exe.
Default build path: C:\Scripts\kytyps5-ufc6-xbox-buttons-mod\release\UFC6-Xbox-Icons\UFC6-Xbox-Icons.exe.
Override with -XboxIconsPatcher 'path', or pass '' to omit the Xbox option.
Requires 64-bit Windows with .NET Framework 4.5+ on the destination computer.
setup.exe uses the supplied UFC 6 icon and embeds product/file metadata:
EA SPORTS UFC 6 Installer, installer version 1.0.0. Game version is shown
separately in the header badge and is read from the packed game's metadata.
Build requirements: PowerShell, .NET Framework C# compiler, installed 7-Zip x64.

BUILD A PACKAGE
  .\build.ps1 -OutputDirectory 'R:\UFC6-installer'

Default source is the current PPSA23566-EASPORTSUFC6GAME folder on T:.
Override with -Source 'path'. Output must be outside the source folder.
Normal uses solid LZMA2, level 5, a 64 MiB dictionary and two threads.
Alter Egos is stored in a separate block inside the same data.bin.
Use -Compression Ultra for level 9; benchmark before committing to a long run.
Use -SplitSizeGiB 10 to emit numbered 10 GiB package parts instead of data.bin.
The installer accepts either distribution layout. Split packages extract directly
from their volumes, so they do not need an extra compressed-package-sized copy on
the destination drive.
To split an already built package without recompressing or changing its signature:
  .\split-package.ps1 -InputFile 'E:\UFC6-installer\data.bin' -OutputDirectory 'E:\UFC6-installer-split' -PartSizeGiB 10
This leaves the original data.bin untouched and refuses to overwrite any part.
The output drive must have room for an incompressible archive. Packing verifies
the archive, checks that source file sizes/timestamps stayed stable, and records
the payload SHA-256 digest in signed metadata. Do not modify source files during a build.

CONVERT AN EXISTING UNSIGNED BASE-ONLY PACKAGE
  .\build.ps1 -Mode Bundle -BaseArchive 'E:\old-package\data.bin' -OutputDirectory 'E:\new-package' -PackageManifest '.build\package-copy.json'

Copy the old package manifest to package-copy.json first. Bundle writes a new
archive, retains the original compressed game streams and adds Alter Egos.
It verifies the old archive hash, unchanged compressed game streams and the
new optional payload. The original archive stays unchanged. Upload the new
setup.exe and data.bin together for this format migration. Later signed package
updates can retain the same generic setup.exe.

REBUILD JUST THE INSTALLER
  .\build.ps1 -Mode Setup -OutputDirectory 'R:\UFC6-installer'

Setup-only builds need no game files, package manifest, Alter Egos RAR or private
signing key. They retain the public key and leave data.bin untouched. The signed
manifest inside data.bin is authoritative; .build/package.json is a build record.

SIGN AN EXISTING PACKAGE WITHOUT RECOMPRESSING
  .\build.ps1 -Mode Seal -BaseArchive 'E:\old-package\data.bin' -OutputDirectory 'E:\signed-package' -PackageManifest '.build\package-copy.json'

Copy the source package's build manifest to package-copy.json first. Seal copies
and hashes the compressed payload, verifies the saved digest, and signs its
metadata. It preserves the source. It also supports re-signing metadata from an
already signed package, with the same unchanged payload. Old unsigned setup.exe
cannot read the new format: distribute the generic EXE for the initial migration.

SIGNING KEY
The initial build generates assets/package-signing-public.xml and a matching
private key at .secrets/package-signing-key.dpapi. The private key is encrypted
with Windows DPAPI for the current Windows user; it is never embedded in the EXE
or data.bin. .secrets/, *.dpapi, game archives and build outputs are gitignored.
Only the public key belongs in a public repository. Keep that public key stable
when rebuilding setup.exe so existing signed packages continue to work.
Back up the private key securely together with the Windows profile needed to
decrypt it. Losing it means publishing a new installer with a new trust key;
the public key cannot recover signing capability. A missing private key does
not prevent setup-only builds. Package signing needs the matching private key.

INSTALL
Run setup.exe and choose a NEW destination folder. Setup verifies data.bin,
checks available space, extracts to a temporary sibling folder, and renames it
only on success. Existing unrelated destinations are refused. A recognized UFC 6
folder is read-only: setup skips game extraction and can be used only to manage
the optional tools. Cancellation removes the temporary extraction. No admin
access, shortcuts, registry entries or uninstaller are added. Install into a
folder your Windows user can write to.
7-Zip and its license notice are embedded; license/source details are in About.

The interface shows archive size, installed size, file count, and live available
space for the selected drive. Game version is read from sce_sys/param.json when
packing, if available. Existing unrelated folders and insufficient space disable
Install. A detected existing UFC 6 folder identifies game options as unchanged
and enables the optional-tools-only path when at least one tool is selected.
Progress separates package checking from extraction. Elapsed time and estimated
time remaining for the CURRENT STEP appear during installation; an estimate is
shown only after five seconds of measured progress. Open folder is available
after completion.
View log opens the current diagnostic log. Logs are saved outside the game
folder under %LOCALAPPDATA%\UFC6 Installer\Logs (falling back to the Windows
temporary folder if needed). Each installation records the game version,
destination, selected options, verification/extraction stages, completion and
full exception details. Logs remain available after failures or cancellation.
For development tests, UFC6_INSTALLER_LOG_DIR overrides the log folder.
The window honors system DPI, scrolls on short screens, and hides the decorative
sidebar when it would leave too little room for the installation controls.
Installation mode can be changed before or during game installation:
  Fast: no extraction/patcher CPU cap; normal process priority; unpaced verification.
  Balanced (default): 60% worker CPU cap; below-normal priority; verification reads
  paced to approximately 192 MiB/s.
  Quiet: 25% worker CPU cap; idle priority; verification reads paced to about 48 MiB/s.
Limits apply to the extraction/patching worker and its child processes, not to
PORTHEON or the game launched afterwards. Windows job objects enforce CPU caps;
unsupported environments fall back to priority control and show that in the UI.
These are ceilings, not target utilization. Modes do not change memory required
for decompression, archive contents, or game files. Disk I/O is not hard-capped.
The optional PORTHEON download uses its normal network behavior independently.
Technical reference: https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_cpu_rate_control_information
Package details, destination/free space, installation options, PORTHEON, and
progress are grouped in bordered cards. The button-icon selector shows colored
PlayStation shapes or Xbox A/B/X/Y, with a selected border and native radio-button
keyboard/accessibility behavior. Play now is visibly grey until it is available.
Alter Egos uses a pill switch (on by default). Action buttons use rounded corners
with hover, pressed, keyboard-focus and disabled states. PlayStation symbols are
optically sized at 16 px within the same spacing as the 20 px Xbox circles.
The PORTHEON card is disabled by default. Its availability is controlled by
optional-tools.json in the public GitHub repository; set portheonEnabled to true
there to show its card, GitHub link and optional download switch in future runs.
UFC 6 Actions Overlay has its own card above PORTHEON, with the project's icon,
a short feature summary, GitHub link and optional install switch (off by default).
It downloads the latest stable standalone setup from thyaguster/ufc6-actions-overlay
and verifies its GitHub asset SHA-256. The overlay targets the PORTHEON launcher
folder games\UFC6; its own installer checks engine compatibility before changes.
When both optional setups are selected, PORTHEON opens first. This installer waits
for it to close before opening overlay setup, prefilling games\UFC6 if detected.
Cancellation keeps the game and leaves any already-open external setup running.
Retry resumes pending setups. Overlay setup is interactive; choose/check the
launcher folder and close UFC 6/PORTHEON before applying it. The project includes
its own in-game button-icon helper; the Game setup page notes that icons can also
be changed later in UFC 6 Actions Overlay. Our selector sets only the initial
game icons. No game-data repack is required for either optional setup. If
PORTHEON or the overlay is detected, setup asks before running that optional
installer again; overlay detection checks the selected game folder first, then
PORTHEON's standard games\\UFC6 folder.
The performance dropdown and popup use the same dark palette, rounded border,
and red selection accent, with arrow-key navigation and accessibility support.
The Play on PC with PORTHEON panel displays its official README logo and a short
description sourced from that README. Play now opens an existing PORTHEON
installation after the game files are installed. It checks common install paths
and Windows application registrations, or lets the user locate PORTHEON.exe.
In PORTHEON, select UFC 6 and, if needed, choose this folder in Game files.
Get from GitHub opens the official latest release. Install it for me is optional
and unchecked by default. After the
game finishes installing, setup queries the latest stable release from the
official XAndromedaXR/PORTHEON GitHub repository, downloads its Windows Setup.exe,
and verifies GitHub's published SHA-256 digest and file size. It then opens the
publisher's interactive installer; follow that setup to finish installing
PORTHEON. If the launcher does not find the game, select this installation's
folder in its Game files tab. No PORTHEON files are included in data.bin.
Download failure or cancellation keeps the completed game installation intact
and offers a retry. The Official GitHub link also opens the latest release.
Verified downloads are cached under %LOCALAPPDATA%\UFC6 Installer\Downloads.
The game installation works offline when this optional download is unchecked.
The Optional tools page checks access to GitHub when opened. If GitHub is not
reachable, it shows an offline message and disables only those download controls;
game setup and installation remain available offline.
Install Alter Egos is checked by default. Selecting it applies the archive's
PPSA23566 contents over the game files before installation is finalized;
unchecking it leaves the base game files intact. Installed size, file count,
required working space, and progress reflect this choice. The overlay is applied
only inside the new installation; the source game folder is never changed.
Button icons defaults to PlayStation, which runs no icon patch. Xbox runs the
included mod after extraction and Alter Egos, before the installation is
finalized. The mod checks the game version and affected ranges, then creates a
restore backup. Any patch failure aborts the new installation and removes it.
Xbox installs the standalone mod tool at Mods\Xbox Icons\UFC6-Xbox-Icons.exe
and preserves .ufc6-xbox-icons-backup.zip at the game root. Use that tool to
restore PlayStation icons later. Its notices are included in its Licenses view.
Xbox installed size is an estimate, allowing up to 20 MiB for the restore backup.
Cancellation during icon patching waits for its write/rollback transaction.
Use -AlterEgosArchive 'path' when packing to select another archive with the same
folder layout, or -AlterEgosArchive '' to pack only the base game. These options
do not change component contents during setup-only rebuilds.

VALIDATION
  .\tests\smoke.ps1
  powershell.exe -NoProfile -File .\tests\signed-package.ps1
  .\tests\xbox-icons.ps1
  .\tests\portheon.ps1
  .\tests\portheon.ps1 -Live
  .\tests\portheon.ps1 -Live -Overlay
  .\tests\ui.ps1
  .\tests\performance.ps1
  powershell.exe -NoProfile -File .\tests\selector.ps1

UI rendering covers 1280x720 at 100%, 1366x768 at 150%, and 1920x1080 at
200%, including the top and bottom of each scrollable viewport. These are
rendered scale simulations, not changes to the Windows display configuration.

tests/full-install.ps1 is queued in the background after pack-game.ps1 finishes.
It builds the production setup.exe against the completed archive and creates a
separate installation under R:\UFC6-installer-validation. It compares every
installed file's SHA-256 against the base source or selected Alter Egos payload,
then tests all 430 Xbox icon ranges and restoration on that full installation.
Status: .build/full-test-state.json. Log: .build/full-install-test.log.
It retains the verified installation for an interactive PORTHEON gameplay check;
it does not label gameplay as passed without an in-game check.

PORTHEON tests cover stable-release asset selection, official URLs, mandatory
checksums, hashing, and cancellation. The optional live test downloads and
verifies the latest official installer in .build; it does not execute it.

The Xbox integration test copies only verified ranges from the source game into
private sparse fixture files under .build, validates all 430 patched ranges,
and tests restoration. It never writes to the source game folder.

--extract <new destination> runs the same engine without a window, for testing.
Append --without-alter-egos to test an installation with Alter Egos unchecked.
Append --xbox-icons to select Xbox; omitting it selects PlayStation.
Append --mode=Fast, --mode=Balanced, or --mode=Quiet to select a performance mode.
On failure this mode records the exception in the diagnostic log and exits 1.
--inspect-package <report.json> verifies the signed manifest and reports metadata
without installing. Payload content is checked by --extract or normal installation.
--preview <image.png> renders the installer window to a PNG without installing.
The .build/smoke-dist package is a tiny test fixture, NOT the game distribution.
tests/scaling.ps1 (Windows PowerShell 5.1) checks all three pages at 100%, 125%,
150% and 200%, including a 1024x600 screen. It checks text/button fit, control
bounds, long destinations, game-sized numbers and access to footer navigation.
tests/ui.ps1 renders the same scale/screen cases for visual inspection. These
simulate layout scaling; they do not change Windows display settings.

COMPRESSION NOTES
The old installer's measured size is only a reference. compression-report.json
contains that inspection. Sample benchmarks cannot establish the full game's
packed size, especially where cross-file repetition dominates compression.
Full-game packing runs independently through pack-game.ps1. Its current status
is recorded in .build/pack-state.json and progress in .build/pack-game.log.
The final data.bin is created only after archive verification succeeds.
tests/bundle.ps1 checks conversion, opt-in/opt-out, unchanged compressed streams
and setup-only rebuilds without access to the Alter Egos source RAR.
