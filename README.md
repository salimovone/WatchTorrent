# WatchTorrent

WatchTorrent is a BitTorrent video player built with **Android** in mind. It uses libtorrent for downloads and VLC for playback.

The **Windows x64** installer will be published on [GitHub Releases](https://github.com/salimovone/WatchTorrent/releases).

## Features

- **Stream torrents while they download.** Start watching as soon as enough data is available. WatchTorrent prioritizes the pieces needed for playback.
- **Watch together in sync.** Invite a friend and keep playback in sync when either of you plays, pauses, or seeks.
- **Talk while watching.** Use in-app voice calls with a peer-to-peer connection when the network allows it.

## Getting started

**Watch on your own.** Open **Discover** or **Search**, select a video torrent, and choose **Watch** to start streaming or **Download** to save it. Open **Downloads** to follow progress or return to a video later. Startup time and buffering depend on how quickly peers can supply the pieces you need.

**Watch with a friend.** Install the same WatchTorrent build on both devices. In **Connection**, choose **Send invitation** and share the HTTPS link. Your friend can tap it to open WatchTorrent on Android or paste it into **Enter link**. Choose **Accept invitation** to connect. Once connected, choose **Watch** on a video to send a viewing invitation. The microphone starts muted and asks for permission when first enabled.

Voice and playback controls use a peer-to-peer connection when the network allows it. Invitation setup uses public signaling and STUN services. A paid service or subscription is unnecessary. Some NAT or firewall configurations prevent a direct connection. See [VoiceConnection notes](native/voice/README.md) for implementation and relay details.

## Build from source

The build instructions below use PowerShell on Windows. Run them from the repository root. Install Git, CMake, and the .NET SDK version pinned in [global.json](global.json). The native build script fetches a pinned vcpkg revision and caches its dependencies.

### Windows x64

Install the **Desktop development with C++** workload for Visual Studio 2026, including MSVC x64 and CMake tools. Then run:

```powershell
./scripts/build-native.ps1 -Target Windows
./scripts/build-voice.ps1 -Target Windows
./scripts/build.ps1 -Target Windows -RestoreWorkloads
```

Run `WatchTorrent.exe` from `artifacts/windows/`.

To create the Windows setup executable with WiX, run `./scripts/build.ps1 -Target WindowsInstaller`. The script publishes the app and bundles its runtime and native libraries into the installer in `artifacts/windows-installer/`.

### Android

Install JDK 17, Android SDK Platform 36, Build Tools 36.0.0, NDK 27.2.12479018, and Ninja. Set `JAVA_HOME`, `ANDROID_HOME`, and `ANDROID_NDK_HOME` to the corresponding install directories, or pass the SDK, JDK, and NDK directories through the script parameters.

```powershell
./scripts/build-native.ps1 -Target AndroidArm64
./scripts/build-voice.ps1 -Target Android
./scripts/build.ps1 -Target Android -AndroidRid android-arm64 -RestoreWorkloads
```

The script writes a development-signed APK to `artifacts/android/` for devices running **Android 9 or later**. Install it on a device with the matching architecture. For 32-bit ARM use `AndroidArm` and `android-arm`. For an x64 emulator use `AndroidX64` and `android-x64`.

Opening HTTPS invitations directly requires the APK signing certificate to appear in the site's Android App Links association. When distributing an APK signed with a new certificate, add its SHA-256 fingerprint to the [GitHub Pages deployment](https://github.com/salimovone/salimovone.github.io/blob/main/.well-known/assetlinks.json).

### macOS and iOS

I don’t have a Mac to build and test Apple releases. If you have a Mac and Xcode, you can work on Apple builds from source. The iOS target still needs a native VoiceConnection adapter, and macOS needs an app target and native dependencies. [iOS device setup notes](WatchTorrent/Platforms/iOS/README.md)

## Project layout

| Path | Responsibility |
| --- | --- |
| [WatchTorrent](WatchTorrent/) | .NET MAUI interface, LibVLCSharp player, downloads, and synchronized playback |
| [WatchTorrent.Core](WatchTorrent.Core/) | Torrent models, magnet parsing, engine contracts, and localization resources |
| [WatchTorrent.Libtorrent](WatchTorrent.Libtorrent/) and [native](native/) | Managed torrent adapter and C++ bridge to libtorrent-rasterbar |
| [TorrentReader](TorrentReader/) | Torrent discovery, search, and details |
| [VoiceConnection](VoiceConnection/) | NTgCalls/WebRTC audio integration for Windows and Android |
| [WatchTorrent.EngineTests](WatchTorrent.EngineTests/) | Engine, streaming, playback, and localization checks |

The torrent engine prioritizes reads around the requested playback position. The player receives verified pieces over a private loopback HTTP endpoint with byte-range support. On a return to cached video, it can drop a stalled input and continue from the new position.

## Tests

Run the managed checks with:

```powershell
./scripts/build.ps1 -Target Tests
```

To include playback checks with the real VLC decoder, supply the directory containing the native VLC libraries:

```powershell
dotnet run --project WatchTorrent.EngineTests -c Release -- --vlc-path "<libvlc-directory>"
```

These checks cover seeking to missing data and returning to cached playback. The Windows native build also runs a local BitTorrent seeder and downloader test.

Third-party components have their own licenses. See the [voice dependency notices](native/voice/THIRD_PARTY_NOTICES.md) for the bundled voice stack.
