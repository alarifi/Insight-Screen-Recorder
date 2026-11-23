# Packaging maintenance

The project root contains Build (active development), Release (portable app), embedded (installer resources), output (installer and checksums) and Archive (preserved history).

Run Build-Release.ps1 from Build or the project root. It refuses to replace a running application and preserves the previous Release. Build-Installer.ps1 verifies the portable ZIP and toolchain hashes, checks application/installer version agreement, compiles Inno Setup and writes output/INSTALLER-INFO.json and SHA256SUMS.txt. It does not install the product.

The current Inno recipe installs into the shared C:\Program Files\Insight Tools directory as requested. Do not add wildcard uninstall cleanup: other INSIGHT products may share that directory. User data remains in the Windows profile and selected recording folders.

embedded/WizardImage0.bmp was resized from 906x1736 (4,721,974 bytes) to 328x628 (618,006 bytes), preserving the existing artwork. Original artwork and product-mismatched license documents are backed up in Archive/Packaging-20261123-052714/embedded.

The old root media tools were FFmpeg 3.2 and FFprobe 3.4.2 and are archived under obsolete-tools. Build/tools uses the current working Release toolchain instead. Do not restore obsolete tools into a build: FFmpeg must support fps_mode.

Historical capture diagnostics and prior plans are archived. One existing library-test media fixture/pointer is retained under Build/docs/evidence, excluded from Git. No new real recording or hardware stress was performed for packaging.

Release builds/ZIP and installer compilation do not establish recording stability, clean-machine installation, signature trust or completion of GPL corresponding-source obligations. See root README and THIRD_PARTY_NOTICES before publication.
