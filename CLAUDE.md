# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Robin is a Windows desktop application for downloading YouTube videos. It's built with .NET Framework 4.8 and Windows Forms, providing a user-friendly GUI for video downloads.

## Build Commands

```bash
# Build the solution
msbuild Robin.sln /p:Configuration=Release

# Build for debugging
msbuild Robin.sln /p:Configuration=Debug

# Restore NuGet packages
nuget restore Robin.sln
```

## Running the Application

```bash
# Run the debug build
Robin/bin/Debug/Robin.exe

# Run the release build
Robin/bin/Release/Robin.exe
```

## Architecture Overview

### Core Components

1. **RobinForm** (`RobinForm.cs`): Main UI form handling user interactions, download list management, and cancellation tracking. Owns `activeDownloads` dictionary mapping video titles to `DownloadState` objects.
2. **YouTubeExplodeVideoDownloader** (`YouTubeExplodeVideoDownloader.cs`): Primary download implementation using YoutubeExplode library. Handles stream manifest retrieval, video info display, and the actual download pipeline. Contains fallback logic for live streams. Talks to the UI only through `IDownloadUiNotifier` and to YoutubeExplode only through `IYoutubeClientAdapter`; an internal constructor injects both (plus the ffmpeg path) for tests.
3. **YouTubeVideoDownloader** (`YouTubeVideoDownloader.cs`): Interface (not abstract class) defining `GetVideoTitle` and `DownloadVideo` contract. `DownloadVideo` takes an `IDownloadUiNotifier`, not a `RobinForm`.
4. **DownloadState** (`DownloadState.cs`): Tracks per-download state including CancellationTokenSource, video metadata, file path, and completion/cancellation flags. Implements `IDisposable`.
5. **RobinVideoInfo** (`RobinVideoInfo.cs`): C# record for video metadata (Title, Extension, Resolution, Bitrate, Size). Includes an `IsExternalInit` shim for .NET Framework 4.8 compatibility.
6. **RobinVideoStatus** (`RobinVideoStatus.cs`): Static class with string constants for download status (Downloading, Done, Cancelled, Failed).
7. **RobinUtils** (`RobinUtils.cs`): Error display plus thin shims over `FFmpegPathResolver` for FFmpeg path resolution.
8. **RobinUpdater** (`RobinUpdater.cs`): ClickOnce update management via `ApplicationDeployment`.
9. **ColorBar** (`ColorBar.cs`): P/Invoke extension method for setting ProgressBar color via Win32 `SendMessage`.
10. **Testability seams**: `IDownloadUiNotifier` (implemented by `RobinForm`), `IYoutubeClientAdapter`/`YoutubeClientAdapter` (wraps the four `YoutubeClient` operations used), `IFileSystem`/`RealFileSystem`, and `FFmpegPathResolver` (cache + WinGet scan logic, unit-tested in memory). See `plans/testability_refactoring_plan.md`.

### Download Flow

1. User enters URL → `RobinDownloadVideoWithChecks` fetches video title via `Task.Run`
2. Checks for duplicate downloads (already complete or in-progress)
3. Creates `DownloadState` with `CancellationTokenSource`
4. `YouTubeExplodeVideoDownloader.DownloadVideo` → `DownloadBestVideo` fetches video info and stream manifest
5. If manifest succeeds: `DownloadBestVideoWithManifest` gets best quality stream, spawns `Task.Run` → `DownloadVideo_Explode`
6. If manifest fails: falls back to direct download without quality selection
7. `DownloadVideo_Explode` adds item to ListView, registers with `activeDownloads`, then spawns another `Task.Run` → `DownloadVideoAsync_Explode`
8. `DownloadVideoAsync_Explode` calls `IYoutubeClientAdapter.DownloadMuxedAsync` (or `DownloadDirectAsync` for the live fallback) with progress reporting and cancellation token

### Key Design Patterns

- **Async/Await with Task.Run**: Download operations are offloaded from the UI thread via nested `Task.Run` calls
- **Progress Reporting**: `IProgress<double>` pattern for real-time download progress with progress bar updates
- **Thread Safety**: UI updates marshaled with `Control.Invoke` via recursive helper methods
- **CancellationToken**: Download cancellation supported through `DownloadState.CancellationTokenSource`
- **Fallback Strategy**: If stream manifest retrieval fails, falls back to direct download (for live streams)

### External Dependencies

- **YoutubeExplode** (6.6.0): Main YouTube extraction library — video info, stream manifests, and downloads
- **YoutubeExplode.Converter** (6.6.0): Muxing support using FFmpeg
- **FFMpegCore** (5.4.0): FFmpeg integration
- **NLog** (6.1.4): Logging to console and `robin_log.txt`
- **AngleSharp** (1.5.2): HTML parsing (transitive dependency of YoutubeExplode)
- **NiL.JS** (2.6.1722): JavaScript engine (transitive dependency of YoutubeExplode)
- **FFmpeg**: External binary — searched for in WinGet packages (`Gyan.FFmpeg`) under `%LOCALAPPDATA%`

### Deployment

The application uses ClickOnce deployment with:
- Install URL: https://raw.githubusercontent.com/albertinopadin/Robin/main/published/
- Auto-update enabled
- Current version: 0.6.0.38 (ApplicationRevision 38)

## Important Considerations

1. **Tests**: `Robin/Robin.Tests` (xUnit + FluentAssertions, net48, SDK-style csproj with `InternalsVisibleTo`) covers the download orchestration, FFmpeg path resolution, and the smaller types. Keep it green; new production files must be added to `Robin.csproj` manually (old-style project)
2. **UI Thread Safety**: Always use Invoke/BeginInvoke for UI updates from async operations. Many helper methods use a recursive pattern checking `InvokeRequired`.
3. **FFmpeg Location**: Searches for FFmpeg in WinGet packages under `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg_*`
4. **Live Video Handling**: Special fallback logic for YouTube live streams — if `GetManifestAsync` fails, retries with direct download
5. **Logging**: Check `robin_log.txt` for debug information when troubleshooting. Console logging at Info level, file logging at Debug level.
6. **Thread Model**: Downloads use nested `Task.Run` calls (up to 3 levels deep); be careful about thread affinity when modifying download logic
7. **Building on this machine**: Use VS 2022 MSBuild (`C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`); the VS 18 MSBuild currently fails on `Properties\app.manifest` with MSB3172. Run tests with VS 2022's `vstest.console.exe` against `Robin.Tests\bin\Debug\net48\Robin.Tests.dll`