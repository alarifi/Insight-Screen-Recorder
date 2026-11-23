# Insight Screen Recorder 1.6.4

Whole-machine freezes reported during recording remain under investigation. This build retains the diagnostic safeguards introduced in1.6.1; real recording has not been rerun after the freeze reports, and no root-cause repair is certified. At the user's subsequent request,1.6.4 applies the studio layout corrections: wider media card, complete device-name popups with compact field captions, short screen labels, and a separate taller shortcut warning/action. These UI changes do not constitute recording-stability clearance.

Windows 10/11, x64. This is a portable screen recorder with a self-contained .NET runtime and bundled FFmpeg tools; users do not need the development source, SDK, or a separate runtime installation.

## Included

- Recording library with duration, search, sortable columns, refresh, playback, show file, rename and confirmed Recycle Bin deletion. Friendly display names do not rename files automatically.
- Settings draft protection with Save/Discard/Cancel, confirmed Reset all settings, output-folder validation and an actionable shortcut warning.
- Direct system-sound/microphone device selectors with full-name tooltips and recording-level meters. Device selection is locked during capture; Pause releases inputs and clears meters.
- Full-display and custom-region capture
- MP4/MKV H.264, screenshots, system audio and microphone recording
- Camera overlay and up to 60 FPS with an active Pro license
- Device selection, audio tests, recording controls, and saved recordings list
- Friendly screen names with main-screen identification and resolution
- The red studio circle changes from REC to Pause/Resume, with one Stop button below the timer; paused time is excluded from the final recording
- Visible countdown before recording (default 3 seconds), Browse and Open folder beside SAVE TO, and automatic folder opening after successful Stop/save (enabled by default in General settings)

Free edition: recording up to 30 FPS. Pro edition: webcam overlay and up to 60 FPS. Pro activation checks the INSIGHT Software Hub online; Pro-only features require internet access for license verification. The Hub must have the Screen Recorder licensing update deployed before customer keys can be issued.

## Run

Extract the package and launch `InsightScreenRecorder.exe`. Recording tools are in `tools`. Settings and the protected license key are stored under the current Windows user's Local AppData; recordings remain in the selected Videos folder. Uninstalling the app does not remove those user files.

See `THIRD_PARTY_NOTICES.md` for bundled component notices and source information.

## Pause and resume

Pause temporarily closes the current screen, camera and audio capture segment. Resume continues the same recording with the original capture settings and devices. Stop works while recording or paused and produces one MP4/MKV file. The displayed timer excludes pauses. Very short transitions may take a moment while the segment is finalized or the devices reopen. Floating controls retain their Pause/Resume and Stop & save actions.

Internal segments are stored temporarily beside the destination file in a uniquely named `.insight-recording-*` folder. Successful saves remove these known temporary files. If capture, device restart or finalization fails, existing segments are preserved and the recording log identifies their location; do not delete them before recovering the recording. An existing destination file is never overwritten. FFmpeg and FFprobe remain bundled; no separate installation is required.
