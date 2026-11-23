# Insight Screen Recorder 1.6.4 — evaluation build

## Fixed in 1.6.4

- Settings dropdown fields repaint their complete area on initial layout and resize, without mouse hover. The regression test failed on the previous version and passes with this change.

## Included

- Studio layout with wider media input cards and a single-line System sound title.
- Friendly display labels and compact device selectors with full-name information.
- Countdown, Pause/Resume and Stop controls.
- Recording library and settings workflows.
- Self-contained Windows x64 package and branded installer.

## Packaging verification

The app was rebuilt from the current source. The portable package passed inventory, embedded-icon extraction and per-entry ZIP integrity checks. The installer is compiled from the matching version and toolchain. Output manifests contain build timestamps and SHA-256 hashes.

These checks do not certify microphone speech capture, prolonged recording stability, clean-machine installation, upgrade/uninstall behavior or online Pro activation.

## Unresolved release blockers

- Whole-computer freezes were reported; their cause remains unresolved.
- A user speech recording contained an AAC track with a peak around -90 dBFS. A separate direct microphone test was also nearly silent. Headset input/hardware/driver routing and the application's complete capture path still need verification.
- The refreshed screenshots detect the connected TX USB Audio microphone and show it enabled. Device recognition does not certify a completed speech test.
- The installer and application are unsigned.
- Exact GPL-tool corresponding source/build materials and dependency notice checks remain outstanding before binary redistribution.
- The installed Inno Setup compiler reports "Non-commercial use only"; resolve its terms for intended commercial use.

**Disposition: prepared for internal evaluation; not cleared for a stable public binary release.**
