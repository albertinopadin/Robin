# Performance Improvements Implementation Plan

## Status: Proposed

This plan implements the top five performance wins identified in `research/performance-analysis.md`, plus the FFmpeg path-lookup cache from the honorable-mentions list.

## Scope

The previous `top_three_improvements_plan.md` fixed correctness and thread-safety issues. The original 3-level `Task.Run` nesting is already flattened, `DownloadState` implements `IDisposable`, and `activeDownloads` is a `ConcurrentDictionary`. This plan builds on that foundation and targets **throughput, latency, and CPU/muxing cost** — issues not addressed in the previous round (item 3a was explicitly deferred; this plan closes it out and goes further).

## Files Modified

| File | Purpose |
|------|---------|
| `Robin/Program.cs` | Connection-limit tuning (fix #2) |
| `Robin/YouTubeExplodeVideoDownloader.cs` | Manifest dedup, pre-selected streams, FFmpeg preset, ConfigureAwait, video-info cache (fixes #1, #4, #5) |
| `Robin/YouTubeVideoDownloader.cs` | Interface change if we want to pass cached `Video` through (optional; can be kept internal) |
| `Robin/DownloadState.cs` | Add `ProgressBar`, `SelectedStreams`, progress-throttle tick field (fix #3) |
| `Robin/RobinForm.cs` | Direct `ProgressBar` reference, `BeginInvoke`, collapse recursive Invoke helpers on the hot path (fix #3) |
| `Robin/RobinUtils.cs` | FFmpeg path cache (bonus fix) |

No NuGet changes.

---

## Fix #1 — Eliminate the triple manifest/metadata fetch; pass pre-selected streams

### Problem

Per download we hit YouTube **four** times:
1. `GetVideoTitle` → `GetVideoInfo` → `youtube.Videos.GetAsync` (for duplicate-check)
2. `DownloadBestVideo` → `GetVideoInfo` → `youtube.Videos.GetAsync` again (to get `VideoId`)
3. `GetManifestAsync`
4. Inside `youtube.Videos.DownloadAsync(videoId, …)` — the Converter re-resolves the manifest internally.

The manifest selection on line 101 is also thrown away by #4 since the Converter picks its own streams. Plus the current selection uses `GetVideoStreams()` which may return a muxed 720p stream — silently downgrading quality.

### Before (YouTubeExplodeVideoDownloader.cs)

```csharp
public async ValueTask<string> GetVideoTitle(string url)
{
    var videoInfo = await GetVideoInfo(url);
    return videoInfo.Title;
}

private async ValueTask<YoutubeExplode.Videos.Video> GetVideoInfo(string videoUrl)
{
    return await youtube.Videos.GetAsync(videoUrl);
}

private async Task DownloadBestVideo(RobinForm form, string videoUrl, bool getManifest, DownloadState state)
{
    var videoInfo = await GetVideoInfo(videoUrl);

    state.VideoId = videoInfo.Id;
    state.VideoUrl = videoUrl;
    state.VideoTitle = MakeValidVideoTitle(videoInfo.Title);

    if (getManifest)
    {
        try
        {
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);
            await DownloadBestVideoWithManifest(form, videoUrl, streamManifest, state);
        }
        catch (HttpRequestException ex)
        {
            logger.Error("Error getting stream manifest...");
            logger.Error(ex);
            logger.Info("Trying to download video without first getting stream manifest...");
            await DownloadBestVideo(form, videoUrl, false, state);
        }
    }
    else { /* hardcoded fallback */ }
}

private async Task DownloadBestVideoWithManifest(RobinForm form, string videoUrl,
                                                 StreamManifest streamManifest, DownloadState state)
{
    var videoStreams = streamManifest.GetVideoStreams();
    if (videoStreams.Any())
    {
        var maxVideoQualityStreamInfo = videoStreams.GetWithHighestVideoQuality();
        state.VideoResolution = maxVideoQualityStreamInfo.VideoResolution.ToString();
        state.Bitrate = maxVideoQualityStreamInfo.Bitrate.ToString();
        state.SizeInMegabytes = maxVideoQualityStreamInfo.Size.MegaBytes;
        state.FileExtension = maxVideoQualityStreamInfo.Container.Name;
        await DownloadVideo_Explode(form, youtube, state);
    }
    // ...
}

// Inside DownloadVideoAsync_Explode:
await youtube.Videos.DownloadAsync(state.VideoId,
                                   state.FilePath,
                                   converter => converter.SetFFmpegPath(this.ffmpegPath),
                                   progress,
                                   state.CancellationToken);
```

### After (YouTubeExplodeVideoDownloader.cs)

```csharp
// Short-lived cache so the duplicate-check call and the download chain share one fetch.
private readonly ConcurrentDictionary<string, YoutubeExplode.Videos.Video> videoInfoCache
    = new ConcurrentDictionary<string, YoutubeExplode.Videos.Video>();

public async ValueTask<string> GetVideoTitle(string url)
{
    var videoInfo = await youtube.Videos.GetAsync(url).ConfigureAwait(false);
    videoInfoCache[url] = videoInfo;
    return videoInfo.Title;
}

private async Task DownloadBestVideo(RobinForm form, string videoUrl, DownloadState state)
{
    if (!videoInfoCache.TryRemove(videoUrl, out var videoInfo))
    {
        videoInfo = await youtube.Videos.GetAsync(videoUrl).ConfigureAwait(false);
    }

    state.VideoId = videoInfo.Id;
    state.VideoUrl = videoUrl;
    state.VideoTitle = MakeValidVideoTitle(videoInfo.Title);

    StreamManifest manifest;
    try
    {
        manifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl).ConfigureAwait(false);
    }
    catch (HttpRequestException ex)
    {
        logger.Error(ex, "Manifest fetch failed; falling back to direct live-stream download.");
        await DownloadLiveFallback(form, state).ConfigureAwait(false);
        return;
    }

    // Pick separate audio+video; prefer mp4 so FFmpeg can stream-copy. Falls back to any container.
    var videoStream = manifest.GetVideoOnlyStreams()
                              .Where(s => s.Container == Container.Mp4)
                              .GetWithHighestVideoQuality()
                      ?? manifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();

    var audioStream = manifest.GetAudioOnlyStreams()
                              .Where(s => s.Container == Container.Mp4)
                              .GetWithHighestBitrate()
                      ?? manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

    if (videoStream == null || audioStream == null)
    {
        MessageBox.Show($"No usable streams found for URL {videoUrl}.");
        return;
    }

    state.SelectedStreams = new IStreamInfo[] { videoStream, audioStream };
    state.VideoResolution = videoStream.VideoResolution.ToString();
    state.Bitrate = videoStream.Bitrate.ToString();
    state.SizeInMegabytes = videoStream.Size.MegaBytes + audioStream.Size.MegaBytes;
    state.FileExtension = "mp4"; // muxed output container

    await DownloadVideo_Explode(form, youtube, state).ConfigureAwait(false);
}

private async Task DownloadLiveFallback(RobinForm form, DownloadState state)
{
    state.SelectedStreams = null; // signal: let Converter resolve
    state.VideoResolution = "UNKNOWN_RESOLUTION";
    state.Bitrate = "UNKNOWN_BITRATE";
    state.SizeInMegabytes = 0;
    state.FileExtension = "mp4";
    await DownloadVideo_Explode(form, youtube, state).ConfigureAwait(false);
}

// Inside DownloadVideoAsync_Explode — overload that skips the internal manifest fetch:
if (state.SelectedStreams != null)
{
    await youtube.Videos.DownloadAsync(
        state.SelectedStreams,
        state.FilePath,
        o => o.SetFFmpegPath(this.ffmpegPath)
              .SetContainer(Container.Mp4)
              .SetPreset(ConversionPreset.UltraFast),
        progress,
        state.CancellationToken).ConfigureAwait(false);
}
else
{
    // Live-stream / manifest-failed path — pass id, let Converter resolve.
    await youtube.Videos.DownloadAsync(
        state.VideoUrl,
        state.FilePath,
        o => o.SetFFmpegPath(this.ffmpegPath)
              .SetPreset(ConversionPreset.UltraFast),
        progress,
        state.CancellationToken).ConfigureAwait(false);
}
```

`DownloadBestVideoWithManifest` is deleted (its body is now inlined into `DownloadBestVideo`).

**Net win:** 4 API calls → 2 per download on the happy path. Manifest selection is honored end-to-end. Audio is guaranteed, fixing the hidden-muxing bug.

---

## Fix #2 — Inject a shared, tuned `HttpClient`; raise `DefaultConnectionLimit`

### Problem

.NET Framework 4.8 defaults `ServicePointManager.DefaultConnectionLimit = 2` for non-ASP.NET apps. Every concurrent download and every parallel segment read inside YoutubeExplode queues behind this 2-connection cap. `new YoutubeClient()` also builds a default `HttpClient` with no User-Agent or compression settings.

### Before (Program.cs)

```csharp
[STAThread]
static void Main()
{
    NLog.LogManager.Setup().LoadConfiguration(builder => {
        builder.ForLogger().FilterMinLevel(LogLevel.Info).WriteToConsole();
        builder.ForLogger().FilterMinLevel(LogLevel.Debug).WriteToFile(fileName: "robin_log.txt");
    });

    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new RobinForm());
}
```

### After (Program.cs)

```csharp
[STAThread]
static void Main()
{
    NLog.LogManager.Setup().LoadConfiguration(builder => {
        builder.ForLogger().FilterMinLevel(LogLevel.Info).WriteToConsole();
        builder.ForLogger().FilterMinLevel(LogLevel.Debug).WriteToFile(fileName: "robin_log.txt");
    });

    // MUST run before any HttpClient request fires — the limit is latched per endpoint
    // the first time that endpoint is contacted.
    System.Net.ServicePointManager.DefaultConnectionLimit = 32;
    System.Net.ServicePointManager.SecurityProtocol =
        System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;

    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new RobinForm());
}
```

### Before (YouTubeExplodeVideoDownloader.cs constructor)

```csharp
public YouTubeExplodeVideoDownloader(string baseFilePath)
{
    this.baseFilePath = baseFilePath;
    this.ffmpegPath = RobinUtils.GetPathToFFMPEG();
    youtube = new YoutubeClient();
}
```

### After (YouTubeExplodeVideoDownloader.cs constructor)

```csharp
// One HttpClient for the lifetime of the process. Static so it survives form recreation.
private static readonly HttpClient sharedHttpClient = CreateSharedHttpClient();

private static HttpClient CreateSharedHttpClient()
{
    var handler = new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        UseCookies = false,
    };
    var client = new HttpClient(handler, disposeHandler: true);
    client.Timeout = TimeSpan.FromMinutes(30); // long enough for large muxed downloads
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    return client;
}

public YouTubeExplodeVideoDownloader(string baseFilePath)
{
    this.baseFilePath = baseFilePath;
    this.ffmpegPath = RobinUtils.GetPathToFFMPEG();
    youtube = new YoutubeClient(sharedHttpClient);
}
```

[Tino] - make the client timeout above a full hour, as sometimes this app is used to download very large, multi-hour videos

**Net win:** Unlocks parallelism for multi-download scenarios and for YoutubeExplode's internal segment fetches. Adds compression (saves manifest/metadata bytes). Uses a real-browser UA to dodge issue #489-style blocks.

---

## Fix #3 — Progress hot path: direct ProgressBar ref, BeginInvoke, time-based throttle

### Problem

Three stacked costs fire on every `IProgress<double>.Report`:
- `GetProgressBarForVideo` at `RobinForm.cs:262` does an O(n) LINQ scan of every child control.
- `SafeSetProgressBarValue` at `:276` uses **blocking** `Invoke`, stalling the download thread whenever the UI thread is busy.
- `% 2 == 0` at `:194` is a size-dependent "throttle" — 5 updates on a 9 MB fallback, 1000+ on a 2 GB file.

### Before (DownloadState.cs)

```csharp
public ListViewItem ListViewItem { get; set; }
public DateTime StartTime { get; set; }
public bool IsCompleted { get; set; }
public bool IsCancelled { get; set; }
```

### After (DownloadState.cs)

```csharp
public ListViewItem ListViewItem { get; set; }
public ProgressBar ProgressBar { get; set; }                    // new: direct ref, no O(n) lookup
public IReadOnlyList<IStreamInfo> SelectedStreams { get; set; } // new: pre-selected streams (fix #1)
public int LastProgressTickMs;                                  // new: wall-clock throttle
public DateTime StartTime { get; set; }
public bool IsCompleted { get; set; }
public bool IsCancelled { get; set; }
```

### Before (YouTubeExplodeVideoDownloader.cs — progress callback)

```csharp
var progress = new Progress<double>(p =>
{
    int progressBarValue = (int)(p * state.SizeInMegabytes);
    if (progressBarValue % 2 == 0)
    {
        form.SetProgressBarValue(listItem, progressBarValue);
    }
});
```

### After (YouTubeExplodeVideoDownloader.cs — progress callback)

```csharp
const int ProgressThrottleMs = 100;
var progress = new Progress<double>(p =>
{
    int now = Environment.TickCount;
    bool isFinal = p >= 1.0;
    if (!isFinal && now - state.LastProgressTickMs < ProgressThrottleMs) return;
    state.LastProgressTickMs = now;

    int value = (int)(p * state.SizeInMegabytes);
    var bar = state.ProgressBar;
    if (bar == null || bar.IsDisposed || !bar.IsHandleCreated) return;

    bar.BeginInvoke((Action)(() =>
    {
        if (!bar.IsDisposed) bar.Value = Math.Min(value, bar.Maximum);
    }));
});
```

### Before (RobinForm.cs — progress plumbing)

```csharp
public ListViewItem AddVideoToDownloadsList(string videoTitle, int videoSize)
{
    // ... creates ListViewItem, calls AddVideoItemToDownloadsList, returns item
}

private void AddProgressBar(Rectangle bounds, string videoTitle, int videoSize)
{
    ProgressBar progressBar = new ProgressBar();
    progressBar.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    logger.Info($"[AddProgressBar] Progress bar bounds: ...");
    // ... configures bar, adds to listView_downloads.Controls
}

private ProgressBar GetProgressBarForVideo(string videoName)
{
    return listView_downloads.Controls.OfType<ProgressBar>()
                                      .FirstOrDefault(i => i.Name == videoName);
}

public void SetProgressBarValue(ListViewItem item, int value)
{
    string videoName = item.SubItems[0].Text;
    ProgressBar progressBar = GetProgressBarForVideo(videoName);
    if (progressBar != null) SafeSetProgressBarValue(progressBar, value);
}

private void SafeSetProgressBarValue(ProgressBar progressBar, int value)
{
    if (progressBar.InvokeRequired)
    {
        Action threadsafeCall = delegate { SafeSetProgressBarValue(progressBar, value); };
        progressBar.Invoke(threadsafeCall);  // blocking
    }
    else
    {
        progressBar.Value = value;
    }
}
```

### After (RobinForm.cs — progress plumbing)

```csharp
// Method signature changes: now takes DownloadState and stashes the ProgressBar on it.
public void AddVideoToDownloadsList(DownloadState state, int videoSize)
{
    // ... build ListViewItem as before
    if (listView_downloads.InvokeRequired)
    {
        listView_downloads.Invoke((Action)(() => AddVideoItemToDownloadsList(state, videoSize)));
    }
    else
    {
        AddVideoItemToDownloadsList(state, videoSize);
    }
}

private void AddVideoItemToDownloadsList(DownloadState state, int videoSize)
{
    listView_downloads.BeginUpdate();

    var videoItem = new ListViewItem(state.VideoTitle);
    videoItem.SubItems.Add(RobinVideoStatus.Dowloading);
    videoItem.SubItems.Add("Download path will appear here");
    videoItem.SubItems.Add("");
    videoItem.SubItems.Add("");
    listView_downloads.Items.Add(videoItem);
    state.ListViewItem = videoItem;

    // Create and stash the progress bar directly on state — no O(n) lookup later.
    state.ProgressBar = CreateProgressBar(videoItem.SubItems[3].Bounds, state.VideoTitle, videoSize);
    listView_downloads.Controls.Add(state.ProgressBar);

    AddCancelButton(videoItem.SubItems[4].Bounds, state.VideoTitle);

    listView_downloads.EndUpdate();
}

private ProgressBar CreateProgressBar(Rectangle bounds, string videoTitle, int videoSize)
{
    var bar = new ProgressBar();
    bar.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    bar.Minimum = 0;
    bar.Maximum = Math.Max(videoSize, 1);
    bar.Value = 1;
    bar.Step = 1;
    bar.Name = videoTitle;
    bar.Visible = true;
    return bar;
}

// GetProgressBarForVideo and SetProgressBarValue are DELETED — progress updates go
// directly through state.ProgressBar in the IProgress<double> callback.
// SafeSetProgressBarValue is DELETED.
```

Call site in `YouTubeExplodeVideoDownloader.cs`:

### Before

```csharp
string validVideoTitle = state.VideoTitle;
ListViewItem listItem = form.AddVideoToDownloadsList(validVideoTitle, (int)state.SizeInMegabytes);
string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{state.FileExtension}");
state.FilePath = videoPath;
state.ListViewItem = listItem;
```

### After

```csharp
state.FilePath = Path.Combine(baseFilePath, $"{state.VideoTitle}.{state.FileExtension}");
form.AddVideoToDownloadsList(state, (int)state.SizeInMegabytes); // sets state.ListViewItem + state.ProgressBar
```

Also remove the `logger.Info` at what is currently `RobinForm.cs:330` — it spams the log on every download start. (honorable-mention bonus fix)

**Net win:** Progress updates become O(1) with non-blocking `BeginInvoke` and bounded frequency (10/sec). The download thread no longer stalls on a busy UI thread.

---

## Fix #4 — FFmpeg stream-copy via `ConversionPreset.UltraFast`

Already folded into the Fix #1 code sample — both the pre-selected-streams path and the live-fallback path now pass:

```csharp
o => o.SetFFmpegPath(this.ffmpegPath)
      .SetContainer(Container.Mp4)
      .SetPreset(ConversionPreset.UltraFast)
```

Because Fix #1 also picks MP4-container audio and video when available, the FFmpeg `-c copy` path kicks in and muxing becomes a near-instant file rewrite instead of a CPU-bound transcode.

**Net win:** Muxing time on a 10-min 1080p video drops from several minutes of high-CPU transcoding to a few seconds of IO.

---

## Fix #5 — `ConfigureAwait(false)` everywhere in the downloader

`YouTubeExplodeVideoDownloader` does not touch UI directly (it calls `form.*` helpers that self-marshal). Every `await` inside it should be `.ConfigureAwait(false)` to keep continuations on the thread pool instead of hopping back to the WinForms sync context.

### Before (sample — occurs at 7 sites)

```csharp
var videoInfo = await GetVideoInfo(videoUrl);
var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);
await DownloadVideo_Explode(form, youtube, state);
await DownloadVideoAsync_Explode(form, listItem, youtube, state);
await youtube.Videos.DownloadAsync(state.VideoId, state.FilePath, ..., state.CancellationToken);
```

### After

```csharp
var videoInfo = await youtube.Videos.GetAsync(videoUrl).ConfigureAwait(false);
var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl).ConfigureAwait(false);
await DownloadVideo_Explode(form, youtube, state).ConfigureAwait(false);
await DownloadVideoAsync_Explode(form, youtube, state).ConfigureAwait(false);
await youtube.Videos.DownloadAsync(state.SelectedStreams, state.FilePath, ...,
                                   state.CancellationToken).ConfigureAwait(false);
```

Also: replace the recursive manifest-failure fallback with a single bounded call (shown in Fix #1 — `DownloadLiveFallback` is linear, no recursion, no depth risk).

Do **not** add `.ConfigureAwait(false)` inside `RobinForm.RobinDownloadVideoWithChecks` before the `this.Invoke(...)` call — the UI-context return there is intentional for the duplicate-check dialog.

**Net win:** Fewer thread-pool → UI-thread hops under concurrent load; tiny per-call, compounding across many videos.

---

## Fix #6 — Cache the FFmpeg path lookup across app restarts

### Problem

`RobinUtils.GetPathToFFMPEG` enumerates `%LOCALAPPDATA%\Microsoft\WinGet\Packages` at every app launch with `Directory.GetDirectories`. On machines with many WinGet packages or roaming AppData on slow storage, this blocks form init.

### Before (RobinUtils.cs)

```csharp
public static string GetPathToFFMPEG()
{
    try
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        logger.Info("[GetPathToFFMPEG] app data path: {0}", appDataPath);
        string fullWinGetPackagesPath = Path.Combine(appDataPath, wingetPackagesPath);
        string ffmpegBaseWinGetFolderName = GetDirectoryThatBeginsWith(GyanFFmpegWingetDirName, fullWinGetPackagesPath);
        string ffmpegWinGetPkgPath = Path.Combine(fullWinGetPackagesPath, ffmpegBaseWinGetFolderName);
        string ffmpegVersionFolderName = GetDirectoryThatBeginsWith("ffmpeg", ffmpegWinGetPkgPath);
        string ffmpegExePath = Path.Combine(ffmpegWinGetPkgPath, ffmpegVersionFolderName, "bin", ffmpegExeFilename);
        return ffmpegExePath;
    }
    catch (Exception e)
    {
        DisplayAndLogException(e);
        throw;
    }
}
```

### After (RobinUtils.cs)

```csharp
private static readonly string FFmpegCachePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Robin", "ffmpeg_path.txt");

public static string GetPathToFFMPEG()
{
    // Try the cache first. If it points at a file that still exists, trust it.
    try
    {
        if (File.Exists(FFmpegCachePath))
        {
            string cached = File.ReadAllText(FFmpegCachePath).Trim();
            if (File.Exists(cached))
            {
                logger.Info("[GetPathToFFMPEG] using cached path: {0}", cached);
                return cached;
            }
            logger.Info("[GetPathToFFMPEG] cached path {0} is stale; re-resolving.", cached);
        }
    }
    catch (Exception ex)
    {
        logger.Warn(ex, "[GetPathToFFMPEG] cache read failed; re-resolving.");
    }

    // Fall through to the original enumeration, then persist the result.
    try
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string fullWinGetPackagesPath = Path.Combine(appDataPath, wingetPackagesPath);
        string ffmpegBaseWinGetFolderName = GetDirectoryThatBeginsWith(GyanFFmpegWingetDirName, fullWinGetPackagesPath);
        string ffmpegWinGetPkgPath = Path.Combine(fullWinGetPackagesPath, ffmpegBaseWinGetFolderName);
        string ffmpegVersionFolderName = GetDirectoryThatBeginsWith("ffmpeg", ffmpegWinGetPkgPath);
        string ffmpegExePath = Path.Combine(ffmpegWinGetPkgPath, ffmpegVersionFolderName, "bin", ffmpegExeFilename);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FFmpegCachePath));
            File.WriteAllText(FFmpegCachePath, ffmpegExePath);
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "[GetPathToFFMPEG] cache write failed (non-fatal).");
        }

        return ffmpegExePath;
    }
    catch (Exception e)
    {
        DisplayAndLogException(e);
        throw;
    }
}
```

Cache is self-healing — if FFmpeg moves (e.g. after WinGet upgrade) the first download will fail, the cache entry will be overwritten on the next launch after re-resolution, or we can invalidate immediately on `File.Exists(cached) == false`.

**Net win:** App-startup latency on machines with big WinGet dirs drops from 100–500 ms to ~1 ms. No change to behavior on fresh installs.

---

## Ordering / dependencies between fixes

1. **Fix #2** (connection limit + shared `HttpClient`) — touches `Program.cs` and the downloader constructor. Land first; it's the foundation.
2. **Fix #6** (FFmpeg cache) — isolated to `RobinUtils.cs`. Land independently at any time.
3. **Fix #3** (progress hot path) — requires new fields on `DownloadState` and a signature change on `AddVideoToDownloadsList`. Land before #1 so the new `ProgressBar` field is in place when #1 wires up streams.
4. **Fix #1 + Fix #4 + Fix #5** (manifest dedup + FFmpeg preset + ConfigureAwait) — all in `YouTubeExplodeVideoDownloader.cs`, naturally bundled. Land last.

Single PR or split 2-ways (#2+#6, then #3+#1+#4+#5) — either works.

---

## Verification

Manual runs (repo has no test framework):

| Fix | How to verify |
|-----|---------------|
| #1 | Add temporary `logger.Info` at each remaining YouTube round-trip. Expect exactly 2 info lines per download on the happy path (was 4). |
| #2 | Queue 5 concurrent downloads. Compare aggregate MB/s in Task Manager vs pre-change. Expect ≥2× on the first pass with #2 alone. |
| #3 | Run a long download, hook up PerfView and confirm the progress callback is called ≤11 times/sec and never blocks on UI. Visually: progress bar updates smoothly without UI jitter. |
| #4 | Time a 10-minute 1080p download end-to-end. Watch Task Manager's FFmpeg process — CPU should spike briefly then idle (stream-copy) instead of pegging a core for minutes (transcode). |
| #5 | Enable `dotnet-trace` threading view during a download; confirm continuations land on thread-pool threads rather than the WinForms UI thread. |
| #6 | First launch: `robin_log.txt` shows the "re-resolving" path. Second launch: shows "using cached path". Delete FFmpeg folder and relaunch: confirm re-resolution kicks in correctly. |

Smoke-test matrix: short video (≤5 min), long video (≥30 min), live stream (triggers live fallback), age-restricted video (stresses UA), invalid URL (error handling), 3 concurrent downloads, cancel mid-download.
