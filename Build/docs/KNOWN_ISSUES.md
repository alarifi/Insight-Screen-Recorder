# Current verification limits - 2026-11-23

- Whole-machine freezes occurred during recording on the development laptop. The cause remains unresolved. The current resource safeguards and diagnostic journal do not establish a fix. No real recording or capture stress test was performed in this packaging task.
- Version 1.6.4 rebuilt successfully after development files moved into Build. The portable archive passed inventory, icon extraction and per-entry byte-integrity verification (283 files).
- The installer recipe uses embedded branding/EULA and defaults to C:\Program Files\Insight Tools. Compiler results and hashes are in output. Installation, upgrade, uninstallation and clean-machine behavior have not been exercised in this task.
- The application and installer are unsigned. Pro licensing service deployment and end-user activation were not verified in this packaging task.
- Exact bundled GPL-tool corresponding source/build materials and dependency-notice verification are required before public binary redistribution. See root THIRD_PARTY_NOTICES.md.
- Older camera/audio/layout results are historical evidence under Archive. They must not be treated as a prolonged recording-stability test. The preserved commercial-library fixture under docs/evidence is local and excluded from Git.
- The local Inno Setup 6.7.4 compiler reports "Non-commercial use only". Resolve the compiler licensing terms for the intended commercial use before a commercial release.

- 1.6.4 fixes first-open settings dropdown painting. Resize invalidation regression passes; 48 component UI/countdown checks pass; first-open Output and Sound were visually verified without hover/resize. This does not clear the unrelated capture/audio/stability blockers.
