# Third-party notices

## Microsoft .NET

The self-contained release includes the .NET 10 Windows Desktop runtime. Preserve the upstream LICENSE.txt and ThirdPartyNotices.txt supplied in Release. The INSIGHT application EULA is installed under Documentation with a separate name and does not replace the runtime license.

## FFmpeg and FFprobe

The retained executables are FFmpeg 6.1.1-full_build-www.gyan.dev and FFprobe 2025-10-12-git-0bc54cddb1-essentials_build-www.gyan.dev. Both report --enable-gpl and --enable-version3. Their reported license is GNU GPL version 3 or later. Linked libraries retain their applicable notices and license conditions.

The application launches these tools as separate processes. The product EULA does not restrict rights granted by their component licenses.

- Upstream licensing guidance: https://ffmpeg.org/legal.html
- Windows build provider: https://www.gyan.dev/ffmpeg/builds/
- GNU GPL version 3: https://www.gnu.org/licenses/gpl-3.0.html

Build/tools/TOOLCHAIN.json records exact versions, configuration, file sizes and SHA-256 hashes. Tool-reported license information is recorded in FFMPEG-LICENSE-INFO.txt and FFPROBE-LICENSE-INFO.txt. These files are included in the installer tools directory.

PUBLIC BINARY DISTRIBUTION REMAINS PENDING: provide the corresponding source and applicable build materials for these exact executables, and verify notices for their linked libraries before distributing them. A link to an upstream homepage alone is not a substitute for the required corresponding source.

INSIGHT icons and wizard artwork are user-supplied branding assets. The proprietary product EULA is in embedded/License.txt; publishing the repository does not select an open-source license for the application.
