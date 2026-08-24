# Robin — yt-dlp Integration Implementation Plan

**Date:** 2026-08-24 (revised same day: first-run consent + progress popups, requirements 7–10)
**Basis:** `research/yt-dlp-integration-research.md` (the "why" and the API research) and the actual Robin source as of this branch.
**Status:** IMPLEMENTED 2026-08-24 (all ten requirements; both configurations build, 66/66 tests green). Design decisions below **override** the research doc's sketches where they differ (noted inline). Deviations found while coding: `VideoData.Resolution` doesn't exist in 1.2.0 (resolution comes from the best-video `FormatData` via `DescribeResolution`), `YoutubeDLSharp.DownloadState` collides with Robin's `DownloadState` (aliased as `YtDlpState`), `RobinForm` implements `IYtDlpSetupUi` explicitly (public class + internal seam types trips CS0051), and the yt-dlp engine lets metadata-fetch failures propagate to `RobinForm`'s catch (which owns the error dialog) instead of dialoguing inside the engine — exact parity with the YoutubeExplode engine's pre-list-add failures, and it keeps tests headless.

This plan implements ten requirements:

1. UI toggle to switch engine (yt-dlp / YoutubeExplode); **yt-dlp is the default**.
2. Individual File-menu buttons to **force-update yt-dlp** and **force-update Deno**.
3. Extract common code (`MakeValidVideoTitle`, `CleanupPartialFile`, the progress throttle) into a shared base class.
4. Get the download size via **option (a)** — sum the best video + best audio `filesize` from the up-front metadata.
5. Two-pass progress: **video = 0–90 %, audio+merge = 90–100 %**, branching on `p.State` (and, as it turns out, a reset detector — see §6).
6. Provide yt-dlp and Deno by **fetching at runtime**.
7. **First-run consent (added 2026-08-24):** before the first-ever yt-dlp/Deno fetch during the `EnsureReadyAsync` bootstrap, ask the user via a popup. Yes → download both binaries behind a popup with **one progress bar per binary**. No → **switch the engine to YouTube Explode**. Selecting the yt-dlp engine again while the binaries are still missing **re-asks** the same question.
8. **Self-update visibility (added 2026-08-24):** when the bootstrap runs the daily `yt-dlp -U` self-update, show a popup with a progress bar (marquee — `yt-dlp -U` exposes no percentage) so the user knows yt-dlp is updating.
9. **Force-update visibility (added 2026-08-24):** `RunBinaryUpdate` (the File-menu force-updates) runs inside a popup with a **real** progress bar (the force path re-downloads the binary, which is measurable).
10. **Display name (added 2026-08-24):** every user-visible string calls the engine **"yt-dlp"** (with the dash). The persisted engine token stays `"ytdlp"` and code identifiers keep their current names — display strings only.

---

## 0. What's already done (no work needed)

`YoutubeDLSharp 1.2.0` is already a dependency — verified in both places the old-style project needs:

- `Robin/packages.config` line 71: `<package id="YoutubeDLSharp" version="1.2.0" targetFramework="net48" />`
- `Robin/Robin.csproj` lines 291-293:
  ```xml
  <Reference Include="YoutubeDLSharp, Version=1.2.0.265, Culture=neutral, processorArchitecture=MSIL">
    <HintPath>packages\YoutubeDLSharp.1.2.0\lib\net45\YoutubeDLSharp.dll</HintPath>
  </Reference>
  ```
  (It resolves the **net45** asset, which runs fine on net48; CopyLocal defaults to true for a HintPath reference, so the DLL deploys.)
- `Robin.Tests/Robin.Tests.csproj` line 21 already has `<PackageReference Include="YoutubeDLSharp" Version="1.2.0" />`, matching the app — so the fake can reference `VideoData`/`DownloadProgress`/`RunResult<T>`.

`Newtonsoft.Json 13.0.4` (YoutubeDLSharp's only real dependency) is already present with a working redirect. **No `App.config` change is required by this plan** — engine selection is persisted to a small cache file, not `AppSettings` (see §8).

Everything else below is new code.

---

## 1. New file inventory

**Production (`Robin/`) — must be hand-added to `Robin.csproj` `<Compile>` (old-style project, per CLAUDE.md item 1):**

| File | Purpose | Section |
|---|---|---|
| `VideoDownloaderBase.cs` | Shared base: `MakeValidVideoTitle`, `CleanupPartialFile`, progress throttle, `baseFilePath` | §3 |
| `IYtDlpClientAdapter.cs` | Test seam over the yt-dlp ops | §4 |
| `YtDlpClientAdapter.cs` | Wraps `YoutubeDL` (fetch/download/update) | §4 |
| `YtDlpVideoDownloader.cs` | The engine (`: VideoDownloaderBase`); lazy adapter | §4–6 |
| `YtDlpProgressMapper.cs` | Two-pass → single 0..1 sweep (0–90/90–100) | §6 |
| `YtDlpPathResolver.cs` | In-house progress-reporting fetch + cache of yt-dlp.exe / deno.exe; update throttle | §7 |
| `YtDlpBootstrap.cs` | Startup/engine-switch orchestration (`EnsureReadyAsync`): first-run consent, downloads, self-update; + `YtDlpSetupResult` enum | §7 |
| `IYtDlpSetupUi.cs` | UI seam for the bootstrap popups (confirm + progress); + `BinaryProgressJob` | §7 |
| `BinaryDownloadProgressForm.cs` | Modal popup with one progress bar per binary (determinate or marquee) | §9 |
| `EngineSettings.cs` | Persisted engine choice (`%LOCALAPPDATA%\Robin\download_engine.txt`) + display names | §8 |
| `DownloaderFactory.cs` | Builds the active engine from the setting | §8 |

**Tests (`Robin.Tests/`) — SDK-style project auto-includes `.cs`, no manual registration:**

| File | Purpose | Section |
|---|---|---|
| `Fakes/FakeYtDlpClientAdapter.cs` | Canned metadata/progress/update | §10 |
| `Fakes/FakeYtDlpSetupUi.cs` | Records confirm/progress-popup calls, runs jobs inline | §10 |
| `Fakes/FakeYtDlpPathResolver.cs` | Subclass overriding the virtual install/update/download members | §10 |
| `YtDlpVideoDownloaderTests.cs` | Orchestration/UI-contract parity | §10 |
| `YtDlpProgressMapperTests.cs` | The 0–90/90–100 mapping | §10 |
| `YtDlpBootstrapTests.cs` | Consent/decline/self-update flows of `EnsureReadyAsync` | §10 |

`InternalsVisibleTo("Robin.Tests")` is already configured (the existing YouTubeExplode tests use the internal ctor), so all new `internal` types are visible to tests automatically.

### 1.1 `Robin.csproj` diff — register the new files

```diff
   <ItemGroup>
+    <Compile Include="BinaryDownloadProgressForm.cs">
+      <SubType>Form</SubType>
+    </Compile>
     <Compile Include="ColorBar.cs" />
     <Compile Include="DownloadState.cs" />
+    <Compile Include="DownloaderFactory.cs" />
+    <Compile Include="EngineSettings.cs" />
     <Compile Include="FFmpegPathResolver.cs" />
     <Compile Include="IDownloadUiNotifier.cs" />
     <Compile Include="IFileSystem.cs" />
     <Compile Include="IYoutubeClientAdapter.cs" />
+    <Compile Include="IYtDlpClientAdapter.cs" />
+    <Compile Include="IYtDlpSetupUi.cs" />
     <Compile Include="RealFileSystem.cs" />
     <Compile Include="YoutubeClientAdapter.cs" />
     <Compile Include="RobinForm.cs">
       <SubType>Form</SubType>
     </Compile>
     <Compile Include="RobinForm.Designer.cs">
       <DependentUpon>RobinForm.cs</DependentUpon>
     </Compile>
     <Compile Include="Program.cs" />
     <Compile Include="Properties\AssemblyInfo.cs" />
     <Compile Include="RobinUpdater.cs" />
     <Compile Include="RobinUtils.cs" />
     <Compile Include="RobinVideoInfo.cs" />
     <Compile Include="RobinVideoStatus.cs" />
+    <Compile Include="VideoDownloaderBase.cs" />
     <Compile Include="YouTubeExplodeVideoDownloader.cs" />
     <Compile Include="YouTubeVideoDownloader.cs" />
+    <Compile Include="YtDlpBootstrap.cs" />
+    <Compile Include="YtDlpClientAdapter.cs" />
+    <Compile Include="YtDlpPathResolver.cs" />
+    <Compile Include="YtDlpProgressMapper.cs" />
+    <Compile Include="YtDlpVideoDownloader.cs" />
```

No `<Reference>` changes: the in-house downloader (§7.1) needs `System.Net.Http` and `System.IO.Compression`/`System.IO.Compression.FileSystem` (for `ZipFile`/`ExtractToFile`), and `Robin.csproj` already references all of them (verified — `System.IO.Compression` at lines 156-163, `System.Net.Http` at 194-195). `BinaryDownloadProgressForm` is a code-only form (no `.Designer.cs`/`.resx`), hence the bare `<SubType>Form</SubType>` entry.

---

## 2. Interface stays the same

`YouTubeVideoDownloader` (the contract) is unchanged:

```csharp
internal interface YouTubeVideoDownloader
{
    ValueTask<string> GetVideoTitle(string url);
    Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state);
}
```

Both engines implement it via the new base class. `IDownloadUiNotifier` is unchanged; the yt-dlp engine drives the **exact same** notifier call sequence as the YoutubeExplode engine, so UI behavior is identical.

---

## 3. Requirement 3 — shared base class

### 3.1 New `Robin/VideoDownloaderBase.cs`

Extracts the three things both engines share today. `MakeValidVideoTitle` stays **`internal static`** (so the existing `YouTubeExplodeVideoDownloader.MakeValidVideoTitle(...)` test keeps compiling — C# resolves an inherited static through the derived type name). The two helper log lines move to the `VideoDownloaderBase` NLog category; every engine-specific log line keeps its own class category because each engine keeps its own `logger`.

```csharp
using System;
using System.IO;
using System.Threading.Tasks;

namespace Robin
{
    /// <summary>
    /// Shared behavior for the concrete download engines
    /// (YouTubeExplodeVideoDownloader, YtDlpVideoDownloader): filename
    /// sanitization, partial-file cleanup, and the progress-report throttle.
    /// </summary>
    internal abstract class VideoDownloaderBase : YouTubeVideoDownloader
    {
        // Own category so the shared helpers log under "VideoDownloaderBase";
        // named distinctly so derived classes keep their own `logger` with no CS0108 hiding.
        private static readonly NLog.Logger baseLogger = NLog.LogManager.GetCurrentClassLogger();

        protected const int ProgressThrottleMs = 100;
        protected readonly string baseFilePath;

        protected VideoDownloaderBase(string baseFilePath)
        {
            this.baseFilePath = baseFilePath;
        }

        public abstract ValueTask<string> GetVideoTitle(string url);
        public abstract Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state);

        internal static string MakeValidVideoTitle(string rawVideoTitle)
        {
            baseLogger.Info($"Raw video title: {rawVideoTitle}");
            return string.Concat(rawVideoTitle.Split(Path.GetInvalidFileNameChars())).Trim();
        }

        /// <summary>
        /// Rate-limits progress reports to one per <see cref="ProgressThrottleMs"/>,
        /// but always lets the final tick through. Mutates state.LastProgressTickMs.
        /// </summary>
        protected static bool ShouldReportProgress(DownloadState state, bool isFinal)
        {
            int now = Environment.TickCount;
            if (!isFinal && now - state.LastProgressTickMs < ProgressThrottleMs) return false;
            state.LastProgressTickMs = now;
            return true;
        }

        protected void CleanupPartialFile(string videoPath)
        {
            if (File.Exists(videoPath))
            {
                try
                {
                    File.Delete(videoPath);
                    baseLogger.Info($"Deleted partial file: {videoPath}");
                }
                catch (Exception ex)
                {
                    baseLogger.Error($"Failed to delete partial file {videoPath}: {ex.Message}");
                }
            }
        }
    }
}
```

### 3.2 `YouTubeExplodeVideoDownloader.cs` diff — inherit the base

Change the base type, delete the now-shared members, add `override`, drop the duplicated `baseFilePath`, and route its progress callback through `ShouldReportProgress`.

```diff
-    internal class YouTubeExplodeVideoDownloader : YouTubeVideoDownloader
+    internal class YouTubeExplodeVideoDownloader : VideoDownloaderBase
     {
         private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

         private static readonly HttpClient sharedHttpClient = CreateSharedHttpClient();

         private readonly IYoutubeClientAdapter client;
-        private readonly string baseFilePath;
         private readonly string ffmpegPath;

         private readonly ConcurrentDictionary<string, YoutubeExplode.Videos.Video> videoInfoCache
             = new ConcurrentDictionary<string, YoutubeExplode.Videos.Video>();

-        private const int ProgressThrottleMs = 100;
-
         private static HttpClient CreateSharedHttpClient() { /* unchanged */ }

         public YouTubeExplodeVideoDownloader(string baseFilePath)
             : this(baseFilePath, new YoutubeClientAdapter(sharedHttpClient), RobinUtils.GetPathToFFMPEG())
         {
         }

         // Test-visible constructor ([InternalsVisibleTo("Robin.Tests")] grants access).
         internal YouTubeExplodeVideoDownloader(string baseFilePath, IYoutubeClientAdapter client, string ffmpegPath)
+            : base(baseFilePath)
         {
-            this.baseFilePath = baseFilePath;
             this.client = client;
             this.ffmpegPath = ffmpegPath;
         }

-        public async ValueTask<string> GetVideoTitle(string url)
+        public override async ValueTask<string> GetVideoTitle(string url)
         {
             /* unchanged */
         }

-        public async Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state)
+        public override async Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state)
         {
             /* unchanged */
         }
```

Delete the engine's own copies (now inherited from the base):

```diff
-        internal static string MakeValidVideoTitle(string rawVideoTitle)
-        {
-            logger.Info($"Raw video title: {rawVideoTitle}");
-            return string.Concat(rawVideoTitle.Split(Path.GetInvalidFileNameChars())).Trim();
-        }
```

```diff
-        private void CleanupPartialFile(string videoPath)
-        {
-            if (File.Exists(videoPath))
-            {
-                try
-                {
-                    File.Delete(videoPath);
-                    logger.Info($"Deleted partial file: {videoPath}");
-                }
-                catch (Exception ex)
-                {
-                    logger.Error($"Failed to delete partial file {videoPath}: {ex.Message}");
-                }
-            }
-        }
```

Route its progress throttle through the shared helper (`DownloadVideoAsync_Explode`, currently lines 199-208):

```diff
             var progress = new Progress<double>(p =>
             {
-                int now = Environment.TickCount;
-                bool isFinal = p >= 1.0;
-                if (!isFinal && now - state.LastProgressTickMs < ProgressThrottleMs) return;
-                state.LastProgressTickMs = now;
-
+                bool isFinal = p >= 1.0;
+                if (!ShouldReportProgress(state, isFinal)) return;
                 int value = (int)(p * state.SizeInMegabytes);
                 notifier.ReportDownloadProgress(state, value);
             });
```

**Behavior is identical** — same throttle window, same "final always reported" rule (the existing throttle test in `YouTubeExplodeVideoDownloaderTests` must stay green). `MakeValidVideoTitle(...)` at the call site (line 86) still resolves via the inherited static; the `MakeValidVideoTitle_StripsInvalidFilenameCharsAndTrims` test is unchanged and still passes.

---

## 4. Requirement — the yt-dlp engine

### 4.1 New `Robin/IYtDlpClientAdapter.cs`

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;

namespace Robin
{
    /// <summary>
    /// Thin seam over the yt-dlp operations the downloader uses, so tests can
    /// inject canned metadata, progress, and download results without spawning
    /// a real yt-dlp process. Mirrors IYoutubeClientAdapter.
    /// </summary>
    internal interface IYtDlpClientAdapter
    {
        Task<RunResult<VideoData>> FetchMetadataAsync(string url, CancellationToken ct);

        Task<RunResult<string>> DownloadAsync(
            string url,
            string format,
            IProgress<DownloadProgress> progress,
            CancellationToken ct);

        // Wraps `yt-dlp -U`. Returns yt-dlp's status line. Fake no-ops this.
        Task<string> UpdateAsync();
    }
}
```

### 4.2 New `Robin/YtDlpClientAdapter.cs`

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace Robin
{
    internal sealed class YtDlpClientAdapter : IYtDlpClientAdapter
    {
        private readonly YoutubeDL ytdl;
        private readonly OptionSet baseOptions;

        public YtDlpClientAdapter(string ytDlpPath, string ffmpegPath, string outputFolder,
                                  OptionSet baseOptions = null)
        {
            ytdl = new YoutubeDL
            {
                YoutubeDLPath = ytDlpPath,          // absolute path to yt-dlp.exe (§7)
                FFmpegPath = ffmpegPath,            // RobinUtils.GetPathToFFMPEG()
                OutputFolder = outputFolder,        // RobinForm.baseFilePath (MyVideos)
                OutputFileTemplate = "%(title)s.%(ext)s",
                RestrictFilenames = false,          // Robin sanitizes titles itself
                OverwriteFiles = true,
            };
            this.baseOptions = baseOptions;         // reserved for cookies later (research §9)
        }

        public Task<RunResult<VideoData>> FetchMetadataAsync(string url, CancellationToken ct)
            => ytdl.RunVideoDataFetch(url, ct, overrideOptions: baseOptions);

        public Task<RunResult<string>> DownloadAsync(
            string url, string format, IProgress<DownloadProgress> progress, CancellationToken ct)
            => ytdl.RunVideoDownload(
                   url,
                   format: format,
                   mergeFormat: DownloadMergeFormat.Mp4,
                   ct: ct,
                   progress: progress,
                   overrideOptions: baseOptions);

        // Updates yt-dlp in place (downloads only when a newer build exists on the
        // stable channel). Needs a writable dir; %LOCALAPPDATA%\Robin qualifies.
        public Task<string> UpdateAsync() => ytdl.RunUpdate();
    }
}
```

Verified 1.2.0 signatures: `RunVideoDataFetch(string, CancellationToken, bool flat=true, bool fetchComments=false, OptionSet=null) → RunResult<VideoData>`; `RunVideoDownload(string, string format, DownloadMergeFormat, VideoRecodeFormat, CancellationToken, IProgress<DownloadProgress>, IProgress<string>, OptionSet) → RunResult<string>` where `.Data` is the final output path; `RunUpdate() → Task<string>`.

**Deno discovery:** `YtDlpPathResolver` (§7) fetches `deno.exe` into the **same folder** as `yt-dlp.exe`, which yt-dlp finds automatically — so no `--js-runtimes` flag is needed here. (If you ever move them apart, add `baseOptions.AddCustomOption<string>("--js-runtimes", "deno")`.)

### 4.3 New `Robin/YtDlpVideoDownloader.cs`

Mirrors `YouTubeExplodeVideoDownloader`'s notifier call sequence exactly. **The one design change vs. the research doc:** the default ctor does **not** resolve/download binaries — it wraps a `Lazy<IYtDlpClientAdapter>` that builds on first use. Because `GetVideoTitle`/`DownloadVideo` always run inside `Task.Run` (see `RobinForm`), the first-use resolution happens off the UI thread. This prevents the field initializer at `RobinForm.cs:20` from blocking the UI on a 57 MB download.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;

namespace Robin
{
    internal class YtDlpVideoDownloader : VideoDownloaderBase
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        // Prefer H.264 mp4 + m4a (two-pass), fall back to muxed mp4, then anything.
        private const string Format =
            "bv*[ext=mp4][vcodec^=avc1]+ba[ext=m4a]/bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b";

        private readonly Lazy<IYtDlpClientAdapter> client;

        // Default ctor: lazy so no download happens on the UI thread at construction.
        public YtDlpVideoDownloader(string baseFilePath)
            : base(baseFilePath)
        {
            client = new Lazy<IYtDlpClientAdapter>(() =>
            {
                var resolver = YtDlpPathResolver.Default();
                string ytDlp = resolver.Resolve();   // cached after YtDlpBootstrap warms it
                resolver.ResolveDeno();               // JS runtime alongside yt-dlp.exe
                return new YtDlpClientAdapter(ytDlp, RobinUtils.GetPathToFFMPEG(), baseFilePath);
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        // Test-visible ctor: inject a fake (eager).
        internal YtDlpVideoDownloader(string baseFilePath, IYtDlpClientAdapter client)
            : base(baseFilePath)
        {
            this.client = new Lazy<IYtDlpClientAdapter>(() => client);
        }

        public override async ValueTask<string> GetVideoTitle(string url)
        {
            var res = await client.Value.FetchMetadataAsync(url, default).ConfigureAwait(false);
            if (!res.Success || res.Data == null)
                throw new InvalidOperationException(
                    $"yt-dlp could not read video info for {url}: {string.Join("; ", res.ErrorOutput)}");
            return res.Data.Title;
        }

        public override async Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state)
        {
            notifier.SetCursorLoading();
            try
            {
                var meta = await client.Value.FetchMetadataAsync(url, state.CancellationToken).ConfigureAwait(false);
                if (!meta.Success || meta.Data == null)
                    throw new InvalidOperationException(
                        $"yt-dlp metadata fetch failed: {string.Join("; ", meta.ErrorOutput)}");

                VideoData info = meta.Data;
                state.VideoId = info.ID;
                state.VideoUrl = url;
                state.VideoTitle = MakeValidVideoTitle(info.Title);     // inherited from base
                state.FileExtension = "mp4";
                state.VideoResolution = info.Resolution ?? "UNKNOWN_RESOLUTION";
                state.Bitrate = "UNKNOWN_BITRATE";
                state.SizeInMegabytes = EstimateSizeMb(info);           // §5 (option a)
                state.FilePath = Path.Combine(baseFilePath, $"{state.VideoTitle}.mp4");

                notifier.SetVideoInfo(new RobinVideoInfo(
                    state.VideoTitle, state.FileExtension, state.VideoResolution,
                    state.Bitrate, state.SizeInMegabytes.ToString("n2")));
                notifier.AddVideoToDownloadsList(state, (int)state.SizeInMegabytes);
                notifier.RegisterActiveDownload(state.VideoTitle, state);
                notifier.ClearVideoUrlTextbox();

                await RunDownload(notifier, url, state).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.Info($"Download cancelled for: {state.VideoTitle}");
                notifier.UpdateDownloadStatus(state.ListViewItem, RobinVideoStatus.Cancelled);
                notifier.CleanupDownload(state.VideoTitle);
                CleanupPartialFile(state.FilePath);                     // inherited
            }
            catch (Exception e)
            {
                logger.Error(e, $"Download failed for: {url}");
                RobinUtils.DisplayAndLogException(e);
                if (state.ListViewItem != null)
                {
                    notifier.UpdateDownloadStatus(state.ListViewItem, RobinVideoStatus.Failed);
                    notifier.CancelProgressBarForVideo(state);
                    notifier.DisableCancelButton(state.VideoTitle);
                }
                CleanupPartialFile(state.FilePath);
            }
            finally
            {
                notifier.SetCursorNormal();
            }
        }

        private async Task RunDownload(IDownloadUiNotifier notifier, string url, DownloadState state)
        {
            var mapper = new YtDlpProgressMapper();                     // §6
            var progress = new Progress<DownloadProgress>(p =>
            {
                double overall = mapper.ToOverallFraction(p);          // 0..1 across both passes
                bool isFinal = p.State == DownloadState.Success || overall >= 1.0;
                if (!ShouldReportProgress(state, isFinal)) return;     // inherited throttle
                notifier.ReportDownloadProgress(state, (int)(overall * state.SizeInMegabytes));
            });

            RunResult<string> res =
                await client.Value.DownloadAsync(url, Format, progress, state.CancellationToken)
                                  .ConfigureAwait(false);

            if (!res.Success)
                throw new InvalidOperationException(
                    $"yt-dlp download failed: {string.Join("; ", res.ErrorOutput)}");

            if (!string.IsNullOrEmpty(res.Data) && File.Exists(res.Data))
                state.FilePath = res.Data;   // trust yt-dlp's real output path

            notifier.NotifyDownloadFinished(state);
        }

        // §5 — option (a): size from up-front metadata.
        internal static double EstimateSizeMb(VideoData info)
        {
            FormatData[] formats = info?.Formats ?? Array.Empty<FormatData>();

            long Bytes(FormatData f) => f?.FileSize ?? f?.ApproximateFileSize ?? 0L;
            bool None(string codec) => string.IsNullOrEmpty(codec) || codec == "none";

            FormatData bestVideo = formats
                .Where(f => f != null
                         && string.Equals(f.Extension, "mp4", StringComparison.OrdinalIgnoreCase)
                         && !None(f.VideoCodec) && None(f.AudioCodec))
                .OrderByDescending(f => f.Height ?? 0)
                .ThenByDescending(Bytes)
                .FirstOrDefault();

            FormatData bestAudio = formats
                .Where(f => f != null
                         && string.Equals(f.Extension, "m4a", StringComparison.OrdinalIgnoreCase)
                         && None(f.VideoCodec) && !None(f.AudioCodec))
                .OrderByDescending(f => f.Bitrate ?? 0)
                .ThenByDescending(Bytes)
                .FirstOrDefault();

            long totalBytes = Bytes(bestVideo) + Bytes(bestAudio);
            return totalBytes / (1024.0 * 1024.0);   // 0 when sizes are unknown; see §5 note
        }
    }
}
```

---

## 5. Requirement 4 — download size via option (a)

`EstimateSizeMb` (above) sums the best mp4 **video-only** (`vcodec != none`, `acodec == none`) `filesize`/`filesize_approx` and the best m4a **audio-only** (`acodec != none`, `vcodec == none`) size, matching the `Format` selection string. Verified property names against the `v.1.2.0` `FormatData.cs` source:

- `long? FileSize` (json `filesize`), `long? ApproximateFileSize` (json `filesize_approx`)
- `string Extension`, `string VideoCodec`, `string AudioCodec`, `int? Height`, `double? Bitrate`

`VideoData.Formats` is `FormatData[]` (verified). This feeds both the info label (`n2` MB) and the progress-bar maximum, exactly as the YoutubeExplode engine uses `stream.Size.MegaBytes`.

**Robustness note (0-size fallback):** some formats expose neither `filesize` nor `filesize_approx`, so the sum can be 0. When `SizeInMegabytes == 0` the bar can't animate (max clamps to 1) — same degraded behavior as today's live-stream fallback; the download still completes and is marked Done. If that proves annoying in practice, add option (b) as a safety net: on the first `Downloading` tick parse `DownloadProgress.TotalDownloadSize` (`"48.20MiB"`) and, if `SizeInMegabytes` was 0, set it then. That needs no new UI surface because the mapper already yields a 0..1 fraction; only the multiplier changes. Left out of the core to keep option (a) the single source of truth per the requirement.

---

## 6. Requirement 5 — two-pass progress mapping

**Correctness catch (verified against `v.1.2.0` `DownloadProgress.cs`):** `DownloadProgress.VideoIndex` is *"the current video index (starting at 1) if multiple items are downloaded at once"* — i.e. the **playlist** position, **not** the audio-vs-video sub-stream. For a single video downloaded as `bv*+ba`, `VideoIndex` stays `1` across both files, so it **cannot** distinguish the video pass from the audio pass. The mapping therefore keys off **`p.State`** (the requirement's primary signal) plus a **progress-reset detector** for the video→audio transition within the `Downloading` state. `DownloadState` verified as `None/PreProcessing/Downloading/PostProcessing/Error/Success`.

### 6.1 New `Robin/YtDlpProgressMapper.cs`

```csharp
using YoutubeDLSharp;

namespace Robin
{
    /// <summary>
    /// Collapses yt-dlp's two download passes (video-only, then audio-only) plus
    /// the merge step into a single monotonic 0..1 sweep for Robin's progress bar:
    ///   video download   -> 0.00 .. 0.90
    ///   audio download   -> 0.90 .. 0.98
    ///   merge/postproc   -> 0.98
    ///   success          -> 1.00
    /// yt-dlp reports each file's progress as its own 0..1 sequence, and
    /// DownloadProgress.VideoIndex is playlist-scoped (not per sub-stream), so the
    /// video->audio switch is detected by the progress value resetting downward.
    /// One instance per download (not thread-safe; Progress&lt;T&gt; marshals callbacks).
    /// </summary>
    internal sealed class YtDlpProgressMapper
    {
        private const double VideoBandEnd = 0.90;   // video pass occupies 0..90%
        private const double AudioBandEnd = 0.98;   // audio pass 90..98%, merge holds at 98%
        private const double ResetDrop = 0.30;      // downward jump => new file started

        private int pass = 0;        // 0 = video, 1 = audio
        private double lastRaw = 0;  // last raw per-file fraction seen while Downloading

        public double ToOverallFraction(DownloadProgress p)
        {
            switch (p.State)
            {
                case DownloadState.Downloading:
                    if (p.Progress + ResetDrop < lastRaw) pass = 1;   // reset => audio pass began
                    lastRaw = p.Progress;
                    return pass == 0
                        ? p.Progress * VideoBandEnd
                        : VideoBandEnd + p.Progress * (AudioBandEnd - VideoBandEnd);

                case DownloadState.PostProcessing:
                    return AudioBandEnd;                              // merging: hold near the end

                case DownloadState.Success:
                    return 1.0;

                case DownloadState.PreProcessing:
                default:
                    return pass == 0 ? 0.0 : VideoBandEnd;
            }
        }
    }
}
```

**Single-pass edge:** if yt-dlp falls back to a progressive `b[ext=mp4]` (one file, no merge), the video band fills to 90 % during download and `Success` jumps it to 100 %. Acceptable and rare.

---

## 7. Requirements 6 + 7 + 8 — fetch yt-dlp and Deno at runtime, with consent and progress

### 7.1 New `Robin/YtDlpPathResolver.cs`

Mirrors `FFmpegPathResolver`'s cache-under-`%LOCALAPPDATA%\Robin` pattern. Fetches both binaries into the same folder (so yt-dlp auto-discovers Deno) and keeps the once-a-day self-update throttle.

**Design change for requirements 7/9 (progress bars):** `YoutubeDLSharp.Utils.DownloadYtDlp`/`DownloadDeno` were verified by reflection against the 1.2.0 assembly — both are `Task Download*(string dirPath)` with **no progress overload**. So the resolver downloads **in-house** with `HttpClient` (streamed, `IProgress<double>` fractions) from the *same endpoints the library uses* (extracted from the 1.2.0 assembly):

- yt-dlp: `https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe`
- Deno: version from `https://dl.deno.land/release-latest.txt`, then `https://dl.deno.land/release/{version}/deno-x86_64-pc-windows-msvc.zip` → extract `deno.exe`

The class is **not sealed** and the install/update/download members are **virtual** — that's the test seam for `YtDlpBootstrapTests` (§10.5); no new interface needed. The separate `ForceDownload*` methods are gone: `DownloadYtDlpAsync`/`DownloadDenoAsync` always pull the latest release, so they *are* the force path (requirement 2/9) and the first-run path (requirement 7) — only the caller's popup differs.

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace Robin
{
    /// <summary>
    /// Locates, fetches, and updates the yt-dlp.exe / deno.exe pair under
    /// %LOCALAPPDATA%\Robin. Downloads are done in-house (not via
    /// YoutubeDLSharp.Utils, which reports no progress) so the setup popups can
    /// show real progress bars. Virtual members are the YtDlpBootstrapTests seam.
    /// </summary>
    internal class YtDlpPathResolver
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        // Same endpoints YoutubeDLSharp.Utils 1.2.0 uses (verified against the assembly).
        private const string YtDlpDownloadUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const string DenoVersionUrl = "https://dl.deno.land/release-latest.txt";
        private const string DenoDownloadUrlTemplate =
            "https://dl.deno.land/release/{0}/deno-x86_64-pc-windows-msvc.zip";

        // Infinite timeout: the default 100 s applies to the whole GetAsync operation
        // and would abort the ~40 MB Deno fetch on slow links. Streamed reads below
        // fail on their own if the connection actually drops.
        private static readonly HttpClient http = new HttpClient
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        private static readonly string CacheDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Robin");

        public static YtDlpPathResolver Default() => new YtDlpPathResolver();

        public string YtDlpPath => Path.Combine(CacheDir, "yt-dlp.exe");
        public string DenoPath  => Path.Combine(CacheDir, "deno.exe");

        // "First run" for requirement 7 = either binary missing.
        public virtual bool IsInstalled => File.Exists(YtDlpPath) && File.Exists(DenoPath);

        // Synchronous fetch-if-missing fallbacks for the lazy adapter path in
        // YtDlpVideoDownloader (already off the UI thread there). In the normal flow
        // the bootstrap has already downloaded both with consent, so these no-op.
        public string Resolve()
        {
            if (!File.Exists(YtDlpPath))
                DownloadYtDlpAsync(progress: null).GetAwaiter().GetResult();
            return YtDlpPath;
        }

        public string ResolveDeno()
        {
            if (!File.Exists(DenoPath))
                DownloadDenoAsync(progress: null).GetAwaiter().GetResult();
            return DenoPath;
        }

        // Always fetches the latest release (no version check) — used by the
        // first-run download (§7.2) and the force-update menu buttons (§9.2).
        public virtual async Task DownloadYtDlpAsync(IProgress<double> progress)
        {
            Directory.CreateDirectory(CacheDir);
            await DownloadFileAsync(YtDlpDownloadUrl, YtDlpPath, progress).ConfigureAwait(false);
            MarkUpdated();   // fresh binary — suppress the same-day `yt-dlp -U` check
        }

        public virtual async Task DownloadDenoAsync(IProgress<double> progress)
        {
            Directory.CreateDirectory(CacheDir);
            string version = (await http.GetStringAsync(DenoVersionUrl).ConfigureAwait(false)).Trim();
            string zipPath = Path.Combine(CacheDir, "deno.zip");
            await DownloadFileAsync(string.Format(DenoDownloadUrlTemplate, version), zipPath, progress)
                .ConfigureAwait(false);
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry entry = archive.GetEntry("deno.exe")
                    ?? throw new InvalidOperationException("deno.exe not found in the Deno release zip.");
                entry.ExtractToFile(DenoPath, overwrite: true);
            }
            File.Delete(zipPath);
        }

        // Streamed download reporting 0..1 fractions; writes to "<dest>.download" and
        // swaps in at the end, so a failed fetch never leaves a truncated binary.
        private static async Task DownloadFileAsync(string url, string destination, IProgress<double> progress)
        {
            using (HttpResponseMessage response =
                await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                string tempPath = destination + ".download";
                using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream target = File.Create(tempPath))
                {
                    byte[] buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        target.Write(buffer, 0, read);
                        done += read;
                        if (total > 0) progress?.Report((double)done / total);
                    }
                }
                File.Delete(destination);   // no-op when absent
                File.Move(tempPath, destination);
                progress?.Report(1.0);
            }
        }

        // --- Update throttle (used by YtDlpBootstrap's daily `yt-dlp -U`) ---
        private string StampPath => Path.Combine(CacheDir, ".ytdlp-update-stamp");

        public virtual bool ShouldUpdate(TimeSpan? maxAge = null)
        {
            TimeSpan window = maxAge ?? TimeSpan.FromHours(24);
            try
            {
                return !File.Exists(StampPath)
                    || DateTime.UtcNow - File.GetLastWriteTimeUtc(StampPath) > window;
            }
            catch { return true; }
        }

        public virtual void MarkUpdated()
        {
            try { Directory.CreateDirectory(CacheDir); File.WriteAllText(StampPath, ""); }
            catch (Exception ex) { logger.Warn(ex, "[YtDlpPathResolver] update stamp write failed (non-fatal)."); }
        }
    }
}
```

> **Note on the force buttons:** requirement 2 says *force*-update, so both menu actions use the unconditional `DownloadYtDlpAsync`/`DownloadDenoAsync` re-pull (guaranteed latest, ignores the daily throttle) — now with progress reporting for requirement 9. The lighter `yt-dlp -U` self-update is reserved for the automatic once-a-day path in `YtDlpBootstrap`. Deno has no self-update, so re-pull is the only option there.

### 7.2 New `Robin/YtDlpSetupResult` + rewritten `Robin/YtDlpBootstrap.cs`

`EnsureReadyAsync` is no longer a silent background warm — it's the consent + popup orchestration for requirements 7 and 8, and it now **runs on the UI thread** (called with `await` from `RobinForm`, §9.2; the old fire-and-forget `Task.Run` wrapper is gone). That's safe because everything heavy happens inside the progress popup, which runs the jobs via `Task.Run` while pumping messages modally. All UI goes through the `IYtDlpSetupUi` seam (§7.3) so the flow is unit-testable headlessly; `RobinForm` is the production implementation.

Flow:

- **Binaries missing (first run)** → `ConfirmFirstRunDownload()` popup.
  - **No** → return `Declined`; the caller switches the engine to YouTube Explode (requirement 7). Nothing is stamped, so selecting yt-dlp again re-enters this branch and re-asks.
  - **Yes** → progress popup with **two determinate bars** (yt-dlp, Deno) running both downloads. Failure → log + return `Failed` (the caller shows the message; no `MessageBox` here so tests stay headless). `DownloadYtDlpAsync` already stamped the throttle, so no same-day `-U` follows.
- **Binaries present + stamp older than 24 h** → `yt-dlp -U` inside a **marquee** progress popup (requirement 8 — `-U` emits no usable percentage). Self-update failure stays non-fatal: log, stamp, continue on the cached binary.
- Otherwise → `Ready` with no UI at all.

```csharp
using System;
using System.Threading.Tasks;

namespace Robin
{
    internal enum YtDlpSetupResult
    {
        Ready,      // binaries present (just downloaded or already cached); engine usable
        Declined,   // first run and the user said No — caller switches to YouTube Explode
        Failed,     // first-run download errored — caller informs the user and switches
    }

    /// <summary>
    /// Startup/engine-switch orchestration for the yt-dlp binaries: first-run
    /// consent popup, consented downloads with progress, and the daily yt-dlp -U
    /// self-update behind a marquee popup. All UI goes through IYtDlpSetupUi.
    /// Runs on the UI thread; the heavy work runs inside the progress popup.
    /// </summary>
    internal static class YtDlpBootstrap
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        // resolver/selfUpdate are test seams; production callers pass only `ui`.
        public static async Task<YtDlpSetupResult> EnsureReadyAsync(
            IYtDlpSetupUi ui, YtDlpPathResolver resolver = null, Func<Task> selfUpdate = null)
        {
            resolver = resolver ?? YtDlpPathResolver.Default();
            selfUpdate = selfUpdate ?? (() => SelfUpdateAsync(resolver));

            if (!resolver.IsInstalled)
            {
                // Requirement 7: ask before pulling ~57 MB on first run.
                if (!ui.ConfirmFirstRunDownload())
                    return YtDlpSetupResult.Declined;

                try
                {
                    await ui.RunWithProgressAsync(
                        "Robin — setting up yt-dlp",
                        new[]
                        {
                            new BinaryProgressJob("yt-dlp", p => resolver.DownloadYtDlpAsync(p)),
                            new BinaryProgressJob("Deno (JS runtime)", p => resolver.DownloadDenoAsync(p)),
                        },
                        marquee: false);
                }
                catch (Exception e)
                {
                    logger.Error(e, "yt-dlp first-run download failed.");
                    return YtDlpSetupResult.Failed;
                }
                return YtDlpSetupResult.Ready;   // DownloadYtDlpAsync stamped the throttle
            }

            if (resolver.ShouldUpdate())
            {
                // Requirement 8: the daily self-update is visible in a marquee popup.
                try
                {
                    await ui.RunWithProgressAsync(
                        "Robin — updating yt-dlp",
                        new[] { new BinaryProgressJob("yt-dlp", _ => selfUpdate()) },
                        marquee: true);
                }
                catch (Exception e)
                {
                    logger.Warn(e, "yt-dlp self-update failed; continuing on the cached binary");
                }
                finally
                {
                    resolver.MarkUpdated();
                }
            }
            return YtDlpSetupResult.Ready;
        }

        private static async Task SelfUpdateAsync(YtDlpPathResolver resolver)
        {
            var updater = new YtDlpClientAdapter(resolver.YtDlpPath, ffmpegPath: "", outputFolder: ".");
            string status = await updater.UpdateAsync().ConfigureAwait(false);   // yt-dlp -U
            logger.Info($"yt-dlp self-update: {status}");
        }
    }
}
```

### 7.3 New `Robin/IYtDlpSetupUi.cs` — the bootstrap's UI seam

```csharp
using System;
using System.Threading.Tasks;

namespace Robin
{
    /// <summary>
    /// One named unit of binary work shown as its own progress bar in the popup.
    /// Run receives the bar's sink and reports 0..1 fractions (ignored in marquee mode).
    /// </summary>
    internal sealed class BinaryProgressJob
    {
        public string Name { get; }
        public Func<IProgress<double>, Task> Run { get; }

        public BinaryProgressJob(string name, Func<IProgress<double>, Task> run)
        {
            Name = name;
            Run = run;
        }
    }

    /// <summary>
    /// The popups YtDlpBootstrap needs. RobinForm implements this (§9.2);
    /// tests use FakeYtDlpSetupUi (§10.5).
    /// </summary>
    internal interface IYtDlpSetupUi
    {
        // First-run popup: "download yt-dlp + Deno now?" — true = Yes.
        bool ConfirmFirstRunDownload();

        // Show a popup with one progress bar per job, run the jobs sequentially,
        // and return when all are done; propagates the first job failure.
        // marquee = true for work with no measurable percentage (yt-dlp -U).
        Task RunWithProgressAsync(string title, BinaryProgressJob[] jobs, bool marquee);
    }
}
```

---

## 8. Requirement 1 — engine selection + persistence

`ConfigurationManager.AppSettings` (the research doc's sketch) is read-only at runtime and reset by ClickOnce updates, so it can't back a live toggle. Instead, persist the choice to a one-line cache file next to the other `%LOCALAPPDATA%\Robin` state.

### 8.1 New `Robin/EngineSettings.cs`

```csharp
using System;
using System.IO;

namespace Robin
{
    internal static class EngineSettings
    {
        public const string YtDlp = "ytdlp";
        public const string Explode = "explode";

        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Robin", "download_engine.txt");

        // Default is yt-dlp (requirement 1).
        public static string Get()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string v = File.ReadAllText(FilePath).Trim();
                    if (v == YtDlp || v == Explode) return v;
                }
            }
            catch { /* fall through to default */ }
            return YtDlp;
        }

        public static void Set(string engine)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, engine == Explode ? Explode : YtDlp);
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "[EngineSettings] write failed (non-fatal).");
            }
        }

        // Requirement 10: user-visible strings say "yt-dlp" (with the dash).
        // The persisted token above stays "ytdlp" and is never rendered in the UI.
        public static string DisplayName(string engine)
            => engine == Explode ? "YouTube Explode" : "yt-dlp";
    }
}
```

**Requirement 10 rule for all UI work in §9:** any string the user can see — menu items, popup titles/bodies, `MessageBox` text, log lines quoted in dialogs — spells the engine **"yt-dlp"**. Use `EngineSettings.DisplayName(...)` wherever the engine token would otherwise leak into display text. Identifiers (`EngineSettings.YtDlp`, `"ytdlp"` in `download_engine.txt`, class/field names) are unchanged.

### 8.2 New `Robin/DownloaderFactory.cs`

```csharp
namespace Robin
{
    internal static class DownloaderFactory
    {
        public static YouTubeVideoDownloader Create(string baseFilePath, string engine)
            => engine == EngineSettings.Explode
                ? (YouTubeVideoDownloader)new YouTubeExplodeVideoDownloader(baseFilePath)
                : new YtDlpVideoDownloader(baseFilePath);
    }
}
```

---

## 9. Requirement 1 + 2 — the UI (File menu) and form wiring

### 9.1 `RobinForm.Designer.cs` — declare the new menu items

Add the field declarations (after line 347, next to `checkForUpdatesToolStripMenuItem`):

```diff
         private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
         private System.Windows.Forms.ToolStripMenuItem checkForUpdatesToolStripMenuItem;
+        private System.Windows.Forms.ToolStripMenuItem downloadEngineToolStripMenuItem;
+        private System.Windows.Forms.ToolStripMenuItem ytdlpEngineToolStripMenuItem;
+        private System.Windows.Forms.ToolStripMenuItem youtubeExplodeEngineToolStripMenuItem;
+        private System.Windows.Forms.ToolStripMenuItem updateYtDlpToolStripMenuItem;
+        private System.Windows.Forms.ToolStripMenuItem updateDenoToolStripMenuItem;
+        private System.Windows.Forms.ToolStripSeparator fileMenuSeparator1;
+        private System.Windows.Forms.ToolStripSeparator fileMenuSeparator2;
```

Instantiate them (inside `InitializeComponent`, alongside line 55):

```diff
             this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
             this.checkForUpdatesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
+            this.downloadEngineToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
+            this.ytdlpEngineToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
+            this.youtubeExplodeEngineToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
+            this.updateYtDlpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
+            this.updateDenoToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
+            this.fileMenuSeparator1 = new System.Windows.Forms.ToolStripSeparator();
+            this.fileMenuSeparator2 = new System.Windows.Forms.ToolStripSeparator();
```

Rebuild the File dropdown to include them (replace the `fileToolStripMenuItem.DropDownItems.AddRange` at lines 259-260):

```diff
             this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
-            this.checkForUpdatesToolStripMenuItem});
+            this.downloadEngineToolStripMenuItem,
+            this.fileMenuSeparator1,
+            this.updateYtDlpToolStripMenuItem,
+            this.updateDenoToolStripMenuItem,
+            this.fileMenuSeparator2,
+            this.checkForUpdatesToolStripMenuItem});
```

Add the item definitions (after the `checkForUpdatesToolStripMenuItem` block, ~line 271):

```diff
             this.checkForUpdatesToolStripMenuItem.Text = "Check for Updates";
             this.checkForUpdatesToolStripMenuItem.Click += new System.EventHandler(this.checkForUpdatesToolStripMenuItem_Click);
+            //
+            // downloadEngineToolStripMenuItem
+            //
+            this.downloadEngineToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
+            this.ytdlpEngineToolStripMenuItem,
+            this.youtubeExplodeEngineToolStripMenuItem});
+            this.downloadEngineToolStripMenuItem.Name = "downloadEngineToolStripMenuItem";
+            this.downloadEngineToolStripMenuItem.Size = new System.Drawing.Size(370, 46);
+            this.downloadEngineToolStripMenuItem.Text = "Download Engine";
+            //
+            // ytdlpEngineToolStripMenuItem
+            //
+            this.ytdlpEngineToolStripMenuItem.Name = "ytdlpEngineToolStripMenuItem";
+            this.ytdlpEngineToolStripMenuItem.Size = new System.Drawing.Size(370, 46);
+            this.ytdlpEngineToolStripMenuItem.Text = "yt-dlp (recommended)";
+            this.ytdlpEngineToolStripMenuItem.Click += new System.EventHandler(this.ytdlpEngineToolStripMenuItem_Click);
+            //
+            // youtubeExplodeEngineToolStripMenuItem
+            //
+            this.youtubeExplodeEngineToolStripMenuItem.Name = "youtubeExplodeEngineToolStripMenuItem";
+            this.youtubeExplodeEngineToolStripMenuItem.Size = new System.Drawing.Size(370, 46);
+            this.youtubeExplodeEngineToolStripMenuItem.Text = "YouTube Explode";
+            this.youtubeExplodeEngineToolStripMenuItem.Click += new System.EventHandler(this.youtubeExplodeEngineToolStripMenuItem_Click);
+            //
+            // updateYtDlpToolStripMenuItem
+            //
+            this.updateYtDlpToolStripMenuItem.Name = "updateYtDlpToolStripMenuItem";
+            this.updateYtDlpToolStripMenuItem.Size = new System.Drawing.Size(370, 46);
+            this.updateYtDlpToolStripMenuItem.Text = "Update yt-dlp";
+            this.updateYtDlpToolStripMenuItem.Click += new System.EventHandler(this.updateYtDlpToolStripMenuItem_Click);
+            //
+            // updateDenoToolStripMenuItem
+            //
+            this.updateDenoToolStripMenuItem.Name = "updateDenoToolStripMenuItem";
+            this.updateDenoToolStripMenuItem.Size = new System.Drawing.Size(370, 46);
+            this.updateDenoToolStripMenuItem.Text = "Update Deno (JS runtime)";
+            this.updateDenoToolStripMenuItem.Click += new System.EventHandler(this.updateDenoToolStripMenuItem_Click);
```

(The `Size` values match the existing `checkForUpdatesToolStripMenuItem` at this DPI; the designer re-fits them on next open.)

### 9.2 `RobinForm.cs` — field init, bootstrap wiring, and handlers

**Declare the setup-UI seam** on the class (line 11) — `RobinForm` is the production `IYtDlpSetupUi`:

```diff
-    public partial class RobinForm : Form, IDownloadUiNotifier
+    public partial class RobinForm : Form, IDownloadUiNotifier, IYtDlpSetupUi
```

**Change the engine field (line 20)** to build from the persisted setting (default yt-dlp), and wire `Load` + `Shown` handlers in the ctor. The bootstrap hangs off **`Shown`, not `Load`**: `Load` fires before the window is painted, and requirement 7's popup should appear over a visible main window (`Shown` also gives `MessageBox`/`ShowDialog` a live owner). No `Task.Run` around the bootstrap anymore — it's awaited on the UI thread and the heavy work runs inside the modal progress popup (§7.2):

```diff
-        YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);
+        YouTubeVideoDownloader videoDownloader = DownloaderFactory.Create(baseFilePath, EngineSettings.Get());
         private ConcurrentDictionary<string, DownloadState> activeDownloads = new ConcurrentDictionary<string, DownloadState>();

         public RobinForm()
         {
             InitializeComponent();
             DisplayAppVersion();
             SetDownloadListClickHandler();
             this.FormClosing += RobinForm_FormClosing;
+            this.Load += RobinForm_Load;
+            this.Shown += RobinForm_Shown;
         }
+
+        private void RobinForm_Load(object sender, EventArgs e) => SyncEngineMenuChecks();
+
+        // Requirements 7 + 8: first-run consent popup / daily self-update popup.
+        private async void RobinForm_Shown(object sender, EventArgs e)
+        {
+            if (EngineSettings.Get() != EngineSettings.YtDlp) return;
+            YtDlpSetupResult result = await YtDlpBootstrap.EnsureReadyAsync(this);
+            if (result == YtDlpSetupResult.Ready) return;
+
+            if (result == YtDlpSetupResult.Failed)
+            {
+                MessageBox.Show(this,
+                    "Downloading the yt-dlp helper programs failed — switching to the " +
+                    "YouTube Explode engine.\nYou can retry via File > Download Engine > " +
+                    "yt-dlp. Details: robin_log.txt.",
+                    "Robin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
+            }
+            SwitchEngine(EngineSettings.Explode);   // Declined or Failed (requirement 7)
+        }
```

**Engine toggle handlers** (radio behavior + persist + rebuild the engine; a running download keeps the engine it already captured). Switching **to yt-dlp** goes through the same bootstrap gate as startup, so while the binaries are missing it re-asks the first-run question (requirement 7) — only a `Ready` result actually switches:

```csharp
private async void ytdlpEngineToolStripMenuItem_Click(object sender, EventArgs e)
{
    YtDlpSetupResult result = await YtDlpBootstrap.EnsureReadyAsync(this);
    if (result == YtDlpSetupResult.Ready)
    {
        SwitchEngine(EngineSettings.YtDlp);
        return;
    }
    if (result == YtDlpSetupResult.Failed)
    {
        MessageBox.Show(this,
            "Downloading the yt-dlp helper programs failed; staying on the current " +
            "engine. Details: robin_log.txt.",
            "Robin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    SyncEngineMenuChecks();   // Declined/Failed: engine and menu checks stay as they were
}

private void youtubeExplodeEngineToolStripMenuItem_Click(object sender, EventArgs e)
    => SwitchEngine(EngineSettings.Explode);

private void SwitchEngine(string engine)
{
    EngineSettings.Set(engine);
    videoDownloader = DownloaderFactory.Create(baseFilePath, engine);
    SyncEngineMenuChecks();
    logger.Info($"Download engine switched to: {EngineSettings.DisplayName(engine)}");
}

private void SyncEngineMenuChecks()
{
    string engine = EngineSettings.Get();
    ytdlpEngineToolStripMenuItem.Checked = engine == EngineSettings.YtDlp;
    youtubeExplodeEngineToolStripMenuItem.Checked = engine == EngineSettings.Explode;
}
```

**`IYtDlpSetupUi` implementation** (the popups the bootstrap drives):

```csharp
public bool ConfirmFirstRunDownload()
    => MessageBox.Show(this,
           "The yt-dlp download engine needs two helper programs, downloaded once " +
           "into %LOCALAPPDATA%\\Robin:\n\n" +
           "    •  yt-dlp  (~17 MB)\n" +
           "    •  Deno JavaScript runtime  (~40 MB)\n\n" +
           "Download them now?\n" +
           "Choosing No switches the download engine to YouTube Explode.",
           "Robin — set up yt-dlp",
           MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

public Task RunWithProgressAsync(string title, BinaryProgressJob[] jobs, bool marquee)
{
    using (var dlg = new BinaryDownloadProgressForm(title, jobs, marquee))
    {
        dlg.ShowDialog(this);   // modal; the dialog closes itself when the jobs finish (§9.3)
        if (dlg.Error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(dlg.Error).Throw();
    }
    return Task.CompletedTask;
}
```

**Force-update handlers** (requirement 9: the update runs inside a popup with a real progress bar — the force path re-downloads the binary, which is measurable. The old wait-cursor is gone; the popup itself communicates the activity. Buttons disabled while running):

```csharp
private async void updateYtDlpToolStripMenuItem_Click(object sender, EventArgs e)
    => await RunBinaryUpdate("yt-dlp", updateYtDlpToolStripMenuItem,
                             (r, p) => r.DownloadYtDlpAsync(p));

private async void updateDenoToolStripMenuItem_Click(object sender, EventArgs e)
    => await RunBinaryUpdate("Deno", updateDenoToolStripMenuItem,
                             (r, p) => r.DownloadDenoAsync(p));

private async Task RunBinaryUpdate(string name, ToolStripMenuItem item,
                                   Func<YtDlpPathResolver, IProgress<double>, Task> update)
{
    item.Enabled = false;
    try
    {
        var resolver = YtDlpPathResolver.Default();
        await RunWithProgressAsync($"Robin — updating {name}",
            new[] { new BinaryProgressJob(name, p => update(resolver, p)) },
            marquee: false);
        MessageBox.Show(this, $"{name} updated to the latest version.", "Robin",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex)
    {
        logger.Error(ex, $"{name} force-update failed.");
        RobinUtils.DisplayAndLogException(ex);
    }
    finally
    {
        item.Enabled = true;
    }
}
```

(`async void` is correct for WinForms event handlers; the inner work is awaited. `RunWithProgressAsync` is `RobinForm`'s own `IYtDlpSetupUi` implementation above, so the bootstrap popups and the force-update popup are the same surface.)

### 9.3 New `Robin/BinaryDownloadProgressForm.cs` — the progress popup

Code-only form (no designer file). One labeled bar per `BinaryProgressJob`; the work starts on `Shown`, runs sequentially off the UI thread via `Task.Run` (the modal loop keeps pumping, so the bars animate), and the dialog closes itself when every job finishes. The first failure lands in `Error` for the caller to rethrow.

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Robin
{
    /// <summary>
    /// Small modal popup with one labeled progress bar per binary job (first-run
    /// downloads, self-update, force-update). Determinate 0-100 bars normally;
    /// marquee for work with no measurable percentage (yt-dlp -U).
    /// </summary>
    internal sealed class BinaryDownloadProgressForm : Form
    {
        public Exception Error { get; private set; }

        private readonly Dictionary<string, ProgressBar> bars = new Dictionary<string, ProgressBar>();
        private readonly Dictionary<string, IProgress<double>> sinks = new Dictionary<string, IProgress<double>>();

        public BinaryDownloadProgressForm(string title, BinaryProgressJob[] jobs, bool marquee)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;               // not closable mid-download (see §12 risk note)
            ClientSize = new Size(430, 16 + jobs.Length * 56);

            int y = 12;
            foreach (BinaryProgressJob job in jobs)
            {
                Controls.Add(new Label { Text = job.Name, Location = new Point(12, y), AutoSize = true });
                var bar = new ProgressBar
                {
                    Location = new Point(12, y + 22),
                    Size = new Size(406, 18),
                    Minimum = 0,
                    Maximum = 100,
                    Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous,
                };
                Controls.Add(bar);
                bars[job.Name] = bar;
                // Progress<T> posts to the SynchronizationContext captured HERE (the
                // UI thread constructing the dialog), so jobs may report from any thread.
                sinks[job.Name] = new Progress<double>(f => SetBarValue(bar, f));
                y += 56;
            }

            Shown += async (s, e) =>
            {
                try
                {
                    foreach (BinaryProgressJob job in jobs)
                    {
                        IProgress<double> sink = sinks[job.Name];
                        await Task.Run(() => job.Run(sink));
                        SetBarValue(bars[job.Name], 1.0);   // fill even if size was unknown
                    }
                }
                catch (Exception ex)
                {
                    Error = ex;
                }
                finally
                {
                    Close();
                }
            };
        }

        private static void SetBarValue(ProgressBar bar, double fraction)
        {
            if (bar.Style == ProgressBarStyle.Marquee) return;   // indeterminate stays animated
            bar.Value = Math.Max(0, Math.Min(100, (int)(fraction * 100)));
        }
    }
}
```

(Bar sizes/positions are logical-pixel values; the form uses the app's default `AutoScaleMode`, and being code-built there's no designer DPI baggage. Job names double as dictionary keys, so they must be unique within one popup — they always are here.)

---

## 10. Testing

The existing `FakeDownloadUiNotifier` is reused as-is. Add:

### 10.1 `Robin.Tests/Fakes/FakeYtDlpClientAdapter.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;

namespace Robin.Tests.Fakes
{
    internal sealed class FakeYtDlpClientAdapter : IYtDlpClientAdapter
    {
        public VideoData Video { get; set; } = new VideoData { Title = "Test Video", ID = "abc123" };
        public bool DownloadSucceeds { get; set; } = true;
        public string OutputPath { get; set; } = @"C:\tmp\Test Video.mp4";
        public List<DownloadProgress> ProgressSequence { get; set; } = new List<DownloadProgress>();
        public List<string> DownloadCalls { get; } = new List<string>();
        public int UpdateCalls { get; private set; }

        public Task<RunResult<VideoData>> FetchMetadataAsync(string url, CancellationToken ct)
            => Task.FromResult(new RunResult<VideoData>(true, new string[0], Video));

        public Task<RunResult<string>> DownloadAsync(
            string url, string format, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            DownloadCalls.Add(url);
            foreach (var p in ProgressSequence) progress?.Report(p);
            return Task.FromResult(new RunResult<string>(DownloadSucceeds, new string[0], OutputPath));
        }

        public Task<string> UpdateAsync()
        {
            UpdateCalls++;
            return Task.FromResult("yt-dlp is up to date (fake)");
        }
    }
}
```

### 10.2 `Robin.Tests/YtDlpVideoDownloaderTests.cs` — orchestration parity

Drive `YtDlpVideoDownloader` through the internal ctor + fakes, mirroring the YoutubeExplode tests. Cover:

- **Happy path**: `DownloadVideo` → `FinishedDownloads` has the state; `FilePath` becomes `OutputPath`; cursor loading/normal balanced (1/1); `RegisterActiveDownload` called once.
- **Metadata failure** (`RunResult.Success == false`) → throws `InvalidOperationException("*metadata fetch failed*")`, cursor reset once.
- **Download failure** (`DownloadSucceeds = false`) → status `Failed`, `CancelProgressBarForVideo` + `DisableCancelButton` called, `CleanupPartialFile` runs, cursor reset.
- **Cancellation** (`ProgressSequence`/adapter throws `OperationCanceledException`) → status `Cancelled`, `CleanupDownload` called, no `FinishedDownloads`.
- **Title**: `GetVideoTitle` returns `Video.Title`.
- **Size (option a)**: feed a `VideoData.Formats` array (mp4 video-only 90 MB + m4a audio-only 10 MB) → `state.SizeInMegabytes ≈ 100`.

```csharp
private YtDlpVideoDownloader CreateSut(FakeYtDlpClientAdapter client)
    => new YtDlpVideoDownloader(tempDir, client);   // internal test ctor (eager fake)
```

### 10.3 `Robin.Tests/YtDlpProgressMapperTests.cs` — the mapping

Pure unit tests, no fakes:

- Video pass: `Downloading` at raw 0.5 → `0.45` (0.5 × 0.90).
- Reset → audio pass: after raw 1.0 then raw 0.0 → `0.90`; raw 1.0 → `0.98`.
- `PostProcessing` → `0.98`; `Success` → `1.0`.
- Monotonic across a full simulated sequence (video 0→1, audio 0→1, merge, success) — values never decrease after mapping.

### 10.4 Base-class test note

`YouTubeExplodeVideoDownloaderTests.MakeValidVideoTitle_StripsInvalidFilenameCharsAndTrims` calls `YouTubeExplodeVideoDownloader.MakeValidVideoTitle(...)` — unchanged and still green (inherited static resolves through the derived type name). Optionally also add the same `[Theory]` against `VideoDownloaderBase.MakeValidVideoTitle` for clarity. The progress-throttle test is unaffected (the refactor preserves exact throttle semantics).

### 10.5 `Robin.Tests/YtDlpBootstrapTests.cs` — the consent/update flows (requirements 7 + 8)

`EnsureReadyAsync` never touches WinForms or the network directly, so it tests headlessly through its three seams: `IYtDlpSetupUi` (fake below), the virtual members of `YtDlpPathResolver` (fake subclass — no interface needed), and the `selfUpdate` delegate (a counting lambda; the real default would spawn yt-dlp). This is why the bootstrap **returns `Failed` instead of showing a `MessageBox`** — a dialog inside the bootstrap would hang the test run.

`Fakes/FakeYtDlpSetupUi.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Robin.Tests.Fakes
{
    internal sealed class FakeYtDlpSetupUi : IYtDlpSetupUi
    {
        public bool ConfirmAnswer { get; set; } = true;
        public int ConfirmCalls { get; private set; }
        public List<(string Title, string[] JobNames, bool Marquee)> ProgressRuns { get; }
            = new List<(string, string[], bool)>();

        public bool ConfirmFirstRunDownload()
        {
            ConfirmCalls++;
            return ConfirmAnswer;
        }

        public async Task RunWithProgressAsync(string title, BinaryProgressJob[] jobs, bool marquee)
        {
            ProgressRuns.Add((title, jobs.Select(j => j.Name).ToArray(), marquee));
            foreach (BinaryProgressJob job in jobs)
                await job.Run(new Progress<double>());   // run inline, discard fractions
        }
    }
}
```

`Fakes/FakeYtDlpPathResolver.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace Robin.Tests.Fakes
{
    internal class FakeYtDlpPathResolver : YtDlpPathResolver
    {
        public bool Installed { get; set; }
        public bool NeedsUpdate { get; set; }
        public bool ThrowOnYtDlpDownload { get; set; }
        public int YtDlpDownloads { get; private set; }
        public int DenoDownloads { get; private set; }
        public int MarkUpdatedCalls { get; private set; }

        public override bool IsInstalled => Installed;
        public override bool ShouldUpdate(TimeSpan? maxAge = null) => NeedsUpdate;
        public override void MarkUpdated() => MarkUpdatedCalls++;   // keep tests off the real stamp file

        public override Task DownloadYtDlpAsync(IProgress<double> progress)
        {
            if (ThrowOnYtDlpDownload) throw new InvalidOperationException("boom");
            YtDlpDownloads++;
            MarkUpdated();          // mirror the real method's stamping
            return Task.CompletedTask;
        }

        public override Task DownloadDenoAsync(IProgress<double> progress)
        {
            DenoDownloads++;
            return Task.CompletedTask;
        }
    }
}
```

Cases (each passes a counting `selfUpdate` lambda):

- **First run, declined** (`Installed = false`, `ConfirmAnswer = false`) → `Declined`; `ConfirmCalls == 1`; no `ProgressRuns`; no downloads; `selfUpdate` not called. (This is also the "switch back to yt-dlp re-asks" behavior: nothing was stamped/downloaded, so the next `EnsureReadyAsync` re-enters the same branch.)
- **First run, accepted** (`ConfirmAnswer = true`) → `Ready`; one **non-marquee** `ProgressRuns` entry titled `"Robin — setting up yt-dlp"` with job names `["yt-dlp", "Deno (JS runtime)"]`; `YtDlpDownloads == 1 && DenoDownloads == 1`; `selfUpdate` not called (fresh binary is already stamped).
- **First run, download throws** (`ThrowOnYtDlpDownload = true`) → `Failed`; `DenoDownloads == 0` (jobs are sequential).
- **Installed, update due** (`Installed = true`, `NeedsUpdate = true`) → `Ready`; one **marquee** run titled `"Robin — updating yt-dlp"`; `selfUpdate` called once; `MarkUpdatedCalls == 1`.
- **Installed, update due, self-update throws** → still `Ready`; `MarkUpdatedCalls == 1` (non-fatal path).
- **Installed, no update due** → `Ready`; `ConfirmCalls == 0`; no `ProgressRuns`.
- **`EngineSettings.DisplayName`**: `"ytdlp"`/unknown → `"yt-dlp"`, `"explode"` → `"YouTube Explode"` (requirement 10).

`BinaryDownloadProgressForm` itself stays untested (WinForms modal loop) — it's covered by the §12 manual smoke test, like the rest of `RobinForm`.

---

## 11. Build, deploy & licensing (from CLAUDE.md)

- **Old-style project**: every new `.cs` in §1.1 must be in `Robin.csproj` by hand — the build won't pick them up otherwise. The SDK-style test project needs no such edits.
- **Build** with the VS 2026 **amd64** MSBuild; run tests with VS 2026 `vstest.console.exe` against `Robin.Tests\bin\Debug\net48\Robin.Tests.dll` (CLAUDE.md item 7). Keep the YoutubeExplode/YoutubeDLSharp versions matched between `Robin` and `Robin.Tests` (item 9) — both are 1.2.0 / 6.6.1 now.
- **First run** asks for consent (requirement 7) before downloading ~17 MB (yt-dlp) + ~40 MB (Deno) into `%LOCALAPPDATA%\Robin`; the fetch runs behind the modal progress popup, off the UI thread. Declining leaves the app on YouTube Explode with nothing fetched. ClickOnce runs per-user, so that path is writable.
- **License notice**: bundle yt-dlp's (GPLv3+ on the built exe) and FFmpeg's notices with the app; Deno is MIT, YoutubeDLSharp is BSD-3-Clause. Fetching at runtime (not redistributing in the installer) keeps the copyleft binary out of the ClickOnce payload.
- No `App.config` change; no new NuGet packages.

---

## 12. Implementation order & risks

**Order (each step compiles and keeps tests green):**

1. `VideoDownloaderBase.cs` + refactor `YouTubeExplodeVideoDownloader` (§3); add to csproj. Run tests — parity check before any yt-dlp code exists.
2. `IYtDlpClientAdapter` + `YtDlpClientAdapter` + `YtDlpProgressMapper` (+ mapper tests) (§4, §6).
3. `YtDlpPathResolver` + `IYtDlpSetupUi`/`BinaryProgressJob` + `YtDlpBootstrap` (§7) + `FakeYtDlpSetupUi`/`FakeYtDlpPathResolver` + `YtDlpBootstrapTests` (§10.5).
4. `YtDlpVideoDownloader` (§4–5) + `FakeYtDlpClientAdapter` + `YtDlpVideoDownloaderTests` (§10).
5. `EngineSettings` (incl. `DisplayName`) + `DownloaderFactory` (§8).
6. UI: Designer + `BinaryDownloadProgressForm` (§9.3) + `RobinForm` handlers, `IYtDlpSetupUi` implementation, field-init/Load/Shown (§9). Manual smoke test both engines **plus the consent flows**: delete `%LOCALAPPDATA%\Robin\yt-dlp.exe`/`deno.exe` and verify (a) startup popup → Yes → two live progress bars; (b) startup popup → No → engine flips to YouTube Explode, File > Download Engine > yt-dlp re-asks; (c) delete the `.ytdlp-update-stamp` file → marquee "updating yt-dlp" popup at startup; (d) both force-update menu items show a determinate progress popup.

**Risks / things to confirm when coding:**

- **No cancel on the progress popup**: `BinaryDownloadProgressForm` disables `ControlBox`, so a stalled network keeps the modal popup up (the infinite `HttpClient` timeout means no automatic abort either — only a dropped connection faults the read). Acceptable for ~57 MB on a first pass; if it bites, add a Cancel button + `CancellationToken` plumbing through `BinaryProgressJob.Run`.
- **Daily self-update popup flash**: when `yt-dlp -U` finds nothing to do it exits in a couple of seconds, so once a day the marquee popup appears briefly. That's requirement 8 working as specified (visibility over silence); `RunUpdate()` returns only a final string, so there's no way to pre-check "would update" and skip the popup.
- **Pinned release endpoints** (§7.1): the GitHub `releases/latest/download/yt-dlp.exe` URL and the `dl.deno.land` version file + `deno-x86_64-pc-windows-msvc.zip` asset name are the same ones YoutubeDLSharp 1.2.0 uses, but they're now Robin's own responsibility — if either project renames its assets, the downloads 404. Symptom: first-run/force-update fails with an `EnsureSuccessStatusCode` HTTP exception.
- **First-run UX**: if the user declines and later pastes a URL while some binary is missing anyway (e.g. deleted mid-session while the engine is yt-dlp), the lazy adapter's `Resolve()` fallback still fetches silently on its background thread — consent covers the normal path (bootstrap + engine switch), not this repair path. Acceptable.
- **Deno discovery**: keep `deno.exe` and `yt-dlp.exe` in the same folder (the resolver does). If a future refactor separates them, add `--js-runtimes deno` via `baseOptions`. Symptom if missing: *"Signature/nsig extraction failed: Some formats may be missing."*
- **Size = 0** when metadata lacks `filesize`/`filesize_approx` (§5) — bar won't animate but completes. Add option (b) only if it bites. (Same degraded look if GitHub/dl.deno.land ever omit `Content-Length`: the popup bar sits at 0 until the completion fill.)
- **Cancellation cleanup**: yt-dlp may leave `*.part`/`*.ytdl` files; `CleanupPartialFile` targets the final `.mp4`. Consider also sweeping `state.FilePath + ".part"` if partials accumulate.
- **`RunResult`/`DownloadProgress`/`FormatData` member names** are pinned to `v.1.2.0` here; re-verify only if you bump YoutubeDLSharp. (`Utils.DownloadYtDlp`/`DownloadDeno` and their URLs were likewise verified against 1.2.0 by reflection, then dropped in favor of the in-house downloader.)
```