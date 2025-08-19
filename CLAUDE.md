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

1. **RobinForm**: Main UI form handling user interactions and download management
2. **YouTubeExplodeVideoDownloader**: Primary download implementation using YoutubeExplode library
3. **YouTubeVideoDownloader**: Abstract base class for video downloader implementations
4. **RobinVideoInfo**: Data structure for video metadata (Title, Extension, Resolution, Bitrate, Size)
5. **RobinUtils**: Utilities for FFmpeg path resolution and common operations
6. **RobinUpdater**: ClickOnce update management

### Key Design Patterns

- **Async/Await**: All download operations use async patterns to prevent UI blocking
- **Progress Reporting**: IProgress<T> pattern for real-time download progress
- **Thread Safety**: UI updates marshaled with Control.Invoke
- **Multiple Download Strategies**: Fallback mechanisms for different video types (regular, live streams)

### External Dependencies

- **YoutubeExplode**: Main YouTube extraction library
- **FFmpeg**: Video processing (included as ffmpeg.exe, also searches WinGet packages)
- **NLog**: Logging to console and robin_log.txt file
- **ClickOnce**: Auto-update deployment via GitHub

### Deployment

The application uses ClickOnce deployment with:
- Install URL: https://raw.githubusercontent.com/albertinopadin/Robin/main/published/
- Auto-update enabled
- Current version: 0.6.0.34

## Important Considerations

1. **No Test Framework**: The project currently has no automated tests
2. **UI Thread Safety**: Always use Invoke/BeginInvoke for UI updates from async operations
3. **FFmpeg Location**: Automatically searches for FFmpeg in WinGet packages or uses bundled ffmpeg.exe
4. **Live Video Handling**: Special fallback logic for YouTube live streams that may fail with primary method
5. **Logging**: Check robin_log.txt for debug information when troubleshooting