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
The interface has three steps: Game setup, Mods, and Install.
Back/Next preserves selections. The final page summarizes the chosen destination,
game options and downloads, with performance controls, progress and Play now.
Navigation is locked while installing; Play now becomes available on completion.
PPSA23566.rar at the project root is packaged inside data.bin as the optional
Alter Egos component. It is never embedded in setup.exe. The installer applies
it only when selected and leaves its internal payload folder out of the game.
Keep the RAR with the packing sources; do not distribute it as a third file.
Updating this component changes data.bin; setup-only rebuilds need no RAR file.
Button prompts remain at the game's original PlayStation setting during game
extraction. The separate Controller Icons Mod is offered on the Mods page and
is downloaded from its official GitHub release only when selected.
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
the optional mods. Cancellation removes the temporary extraction. No admin
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
Package details, destination/free space, installation options, mods, and
progress are grouped in bordered cards. Play now is visibly grey until available.
Alter Egos uses a pill switch (on by default). Action buttons use rounded corners
with hover, pressed, keyboard-focus and disabled states.
The two optional-tool cards are populated by optional-tools.json in the public
GitHub repository. Each slot can be disabled or given another name, description,
note, full setup instructions, GitHub owner/repository and installation behavior
without rebuilding setup.exe. The first slot is Parsectagon Overlay and the
second is UFC Controller Icons Mod. Instructions opens each mod's full steps.
installMode is "installer" for a Windows setup EXE or "github" for software
that users must install manually. The GitHub mode shows the release link and
instructions without an auto-install switch. Installer mode needs an
installerPattern matching exactly one EXE in the latest stable GitHub release.
installOrder sets the order when both are selected (lower runs first); the
installer waits for the first setup to close before opening the next.
installerArguments can contain {gameFolder}, replaced with the selected game
destination in quotes. For setup programs that can target a separate folder,
set chooseFolder to true, provide a folderPrompt, and include {selectedFolder}
in installerArguments. The installer asks for that folder and passes it to the
setup executable in quotes. The setup program remains responsible for checking
whether the chosen folder is compatible. detectedFile can be a path beginning with {gameFolder},
{localAppData}, {programFiles}, or {programFilesX86}; if found, setup asks
before reinstalling. See optional-tools.json for a working example. Only the
repository owner should edit that file: an enabled installer slot can launch a
verified executable from the configured repository.
The installer validates the GitHub asset URL, filename, size and published
SHA-256 before opening it. If the configuration cannot be loaded, both optional
tool cards stay hidden and game installation still works.
UFC 6 Parsectagon Overlay has its own card above Controller Icons Mod, with the project's icon,
a short feature summary, GitHub link and optional install switch (off by default).
It downloads the latest stable standalone setup from thyaguster/ufc6-parsectagon-overlay
and verifies its GitHub asset SHA-256. Its own installer checks engine
compatibility before changes.
When both mods are selected, overlay setup opens first. The UFC installer waits
for it to close before opening Controller Icons Mod. The UFC installer asks for
either a PORTHEON games folder, games\UFC6 folder, or Clean Kyty folder and passes
it to overlay setup. The overlay setup verifies engine compatibility before
installing; selecting a folder does not bypass that check.
Cancellation keeps the game and leaves any already-open external setup running.
Retry resumes pending setups. Overlay setup is interactive; check the chosen
engine folder and close UFC 6 and its launcher before applying it. The
Controller Icons Mod is a separate interactive app for Xbox, PlayStation,
Switch, and keyboard button prompts. It can use the installed UFC 6 folder,
creates reversible backups, and can restore the original PlayStation prompts.
No game-data repack is required for either mod. If an existing installation is
detected, setup asks before running that mod again.
The performance dropdown and popup use the same dark palette, rounded border,
and red selection accent, with arrow-key navigation and accessibility support.
Play now on the final page opens an existing PORTHEON installation after the
game files are installed. It checks common install paths and Windows application
registrations, or lets the user locate PORTHEON.exe. In PORTHEON, select UFC 6
and, if needed, choose this folder in Game files. PORTHEON is not included.
Each mod has Get from GitHub and an unchecked Install it for me switch. The
installer queries the latest stable release, verifies its published SHA-256
digest and file size, then opens the publisher's interactive EXE. Download
failure or cancellation keeps the completed game installation intact and
offers a retry.
Verified downloads are cached under %LOCALAPPDATA%\UFC6 Installer\Downloads.
The game installation works offline when this optional download is unchecked.
The Mods page checks access to GitHub when opened. If GitHub is not
reachable, it shows an offline message and disables only those download controls;
game setup and installation remain available offline.
Install Alter Egos is checked by default. Selecting it applies the archive's
PPSA23566 contents over the game files before installation is finalized;
unchecking it leaves the base game files intact. Installed size, file count,
required working space, and progress reflect this choice. The overlay is applied
only inside the new installation; the source game folder is never changed.
The game is installed with its original button prompts. Controller Icons Mod
is optional and runs separately after the game install; its own UI handles
game selection, prompt style, backups, and restoration.
Use -AlterEgosArchive 'path' when packing to select another archive with the same
folder layout, or -AlterEgosArchive '' to pack only the base game. These options
do not change component contents during setup-only rebuilds.

VALIDATION
  .\tests\smoke.ps1
  powershell.exe -NoProfile -File .\tests\signed-package.ps1
  .\tests\portheon.ps1
  .\tests\portheon.ps1 -Live
  .\tests\portheon.ps1 -Live -Overlay
  .\tests\portheon.ps1 -Live -Icons
  .\tests\ui.ps1
  .\tests\performance.ps1
  powershell.exe -NoProfile -File .\tests\selector.ps1

UI rendering covers 1280x720 at 100%, 1366x768 at 150%, and 1920x1080 at
200%, including the top and bottom of each scrollable viewport. These are
rendered scale simulations, not changes to the Windows display configuration.

tests/full-install.ps1 is queued in the background after pack-game.ps1 finishes.
It builds the production setup.exe against the completed archive and creates a
separate installation under R:\UFC6-installer-validation. It compares every
installed file's SHA-256 against the base source or selected Alter Egos payload.
Status: .build/full-test-state.json. Log: .build/full-install-test.log.
It retains the verified installation for an interactive PORTHEON gameplay check;
it does not label gameplay as passed without an in-game check.

Download tests cover stable-release asset selection, official URLs, mandatory
checksums, hashing, and cancellation. Optional live tests download and verify
the latest official mod installers in .build; they do not execute them.

--extract <new destination> runs the same engine without a window, for testing.
Append --without-alter-egos to test an installation with Alter Egos unchecked.
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
