# Testability Refactoring Plan

## Context

The initial unit-testing work (see [unit_testing_implementation_plan.md](unit_testing_implementation_plan.md)) landed 23 tests covering records, constants, the `DownloadState` type, and a filesystem utility — all code that was unit-testable without production changes. The code that *actually does work* — `YouTubeExplodeVideoDownloader` and `RobinForm` — stayed untested because it is tightly coupled to:

- The `YoutubeExplode.YoutubeClient` concrete class (live network calls).
- The `RobinForm` concrete class (WinForms UI, `BeginInvoke`, `ListViewItem`, `ProgressBar`).
- The WinGet-installed FFmpeg binary resolved via direct `File.*` / `Directory.*` calls.
- `ApplicationDeployment.CurrentDeployment` (ClickOnce runtime).

This plan introduces the minimum set of seams required to unit-test the orchestration logic that matters: stream selection, fallback-to-live behavior, filename sanitization, progress reporting, cancellation handling, the CliWrap teardown-bug workaround, and the FFmpeg path resolver's cache logic. It is deliberately conservative — it does **not** introduce a DI container, does **not** attempt to decouple from YoutubeExplode's domain types, and does **not** restructure the async flow.

### Goals

- Make `DownloadBestVideo`, `MakeValidVideoTitle`, `DownloadVideoAsync_Explode`'s progress / cancellation / CliWrap-bug paths, and `RobinUtils.GetPathToFFMPEG` unit-testable.
- Keep the shipped code's behavior and performance identical to today.
- Keep the refactor incremental — each phase ships independently, each phase leaves `main` green.

### Non-goals

- Full hexagonal architecture. Adapters will still leak YoutubeExplode's `Video`, `StreamManifest`, `IStreamInfo`, `ConversionRequest` types through the port. We are not trying to swap YoutubeExplode for another library.
- A DI container (Microsoft.Extensions.DependencyInjection). Poor-man's-DI via constructor parameters with defaults is enough for a single-form app.
- UI-level integration tests. The `RobinForm` event handlers and the `Task.Run` chain remain integration territory, out of scope here.
- Testing `RobinUpdater` / ClickOnce `ApplicationDeployment` — genuinely untestable without a ClickOnce host.

### Summary of seams introduced

| Seam | Purpose | New types |
|---|---|---|
| `IDownloadUiNotifier` | Replace the `RobinForm form` parameter on the downloader | `IDownloadUiNotifier` interface; `RobinForm` implements it |
| `IYoutubeClientAdapter` | Thin wrapper over the `youtube.Videos.*` calls actually used | `IYoutubeClientAdapter`, `YoutubeClientAdapter` |
| `IFileSystem` | Wrap the 5 `File.*` / `Directory.*` primitives used by `RobinUtils` | `IFileSystem`, `RealFileSystem` |
| `FFmpegPathResolver` | Extract `GetPathToFFMPEG` from `RobinUtils` into a class that takes `IFileSystem` | `FFmpegPathResolver` |

No seam is introduced for the Task.Run nesting in `RobinForm` — that is downstream of these and becomes testable as a side effect.

---

## Phase 1 — Delete dead code

`CLAUDE.md` documents two files as unused. Verify and delete before refactoring so we don't spend effort keeping them alive.

### Files to delete

- `Robin/FastYouTube.cs` — uses `VideoLibrary` for chunked downloads, not referenced in the active download path.
- `Robin/RobinVideoDownloader.cs` — empty class.

### Action

1. Confirm with `grep -r "FastYouTube\|RobinVideoDownloader" Robin/` that neither type is referenced outside its own file.
2. Delete both files.
3. Remove their `<Compile Include=...>` entries from `Robin.csproj`.
4. If `VideoLibrary` is only used by `FastYouTube`, remove it from `packages.config` too.

**Performance effect:** neutral → slightly positive (one fewer assembly reference, smaller publish output).

---

## Phase 2 — Extract `IDownloadUiNotifier` from `RobinForm`

`YouTubeExplodeVideoDownloader` currently takes `RobinForm form` as a parameter and calls 11 distinct methods on it. We extract those 11 methods into an interface. `RobinForm` implements the interface trivially (the methods already exist). The downloader's parameter type changes from `RobinForm` to `IDownloadUiNotifier`.

### Methods on the interface

Derived from [YouTubeExplodeVideoDownloader.cs](../Robin/YouTubeExplodeVideoDownloader.cs) `form.*` usages:

```csharp
internal interface IDownloadUiNotifier
{
    void SetCursorLoading();
    void SetCursorNormal();
    void ClearVideoUrlTextbox();
    void SetVideoInfo(RobinVideoInfo info);
    void AddVideoToDownloadsList(DownloadState state, int videoSizeMb);
    void RegisterActiveDownload(string videoTitle, DownloadState state);
    void UpdateDownloadStatus(ListViewItem item, string status);
    void CleanupDownload(string videoTitle);
    void CancelProgressBarForVideo(DownloadState state);
    void DisableCancelButton(string videoTitle);
    void NotifyDownloadFinished(DownloadState state);
}
```

### Before — `Robin/YouTubeExplodeVideoDownloader.cs:60-71`

```csharp
public async Task DownloadVideo(RobinForm form, string url, DownloadState state)
{
    form.SetCursorLoading();
    try
    {
        await DownloadBestVideo(form, url, state).ConfigureAwait(false);
    }
    finally
    {
        form.SetCursorNormal();
    }
}
```

### After

```csharp
public async Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state)
{
    notifier.SetCursorLoading();
    try
    {
        await DownloadBestVideo(notifier, url, state).ConfigureAwait(false);
    }
    finally
    {
        notifier.SetCursorNormal();
    }
}
```

And in [RobinForm.cs:11](../Robin/RobinForm.cs):

```csharp
// Before
public partial class RobinForm : Form

// After
public partial class RobinForm : Form, IDownloadUiNotifier
```

The `YouTubeVideoDownloader` interface's `DownloadVideo` signature changes the same way:

```csharp
// Before
Task DownloadVideo(RobinForm form, string url, DownloadState state);

// After
Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state);
```

### Call-site change in `RobinForm.DownloadVideo` at [RobinForm.cs:181](../Robin/RobinForm.cs)

```csharp
// Before
await videoDownloader.DownloadVideo(this, videoUrl, downloadState);

// After — `this` still works because RobinForm now implements IDownloadUiNotifier
await videoDownloader.DownloadVideo(this, videoUrl, downloadState);
```

(No textual change at the call site. The type flowing through the parameter just narrows.)

### The progress-bar coupling

One subtlety: `DownloadVideoAsync_Explode` at [YouTubeExplodeVideoDownloader.cs:201-207](../Robin/YouTubeExplodeVideoDownloader.cs) reaches into `state.ProgressBar` (a `ProgressBar` control) directly and calls `bar.BeginInvoke(...)`. That is UI coupling that survives the interface.

**Recommended resolution:** add one more method to `IDownloadUiNotifier`:

```csharp
void ReportDownloadProgress(DownloadState state, int progressValue);
```

…and move the `BeginInvoke` / `IsDisposed` / `IsHandleCreated` guard logic into `RobinForm`'s implementation. The downloader keeps the 100 ms throttle locally (that's a cost optimization, not a UI concern) and calls `notifier.ReportDownloadProgress` once per tick instead of poking the `ProgressBar` directly.

```csharp
// Before — YouTubeExplodeVideoDownloader.cs:193-208
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
        if (!bar.IsDisposed) bar.Value = Math.Min(Math.Max(value, bar.Minimum), bar.Maximum);
    }));
});

// After
var progress = new Progress<double>(p =>
{
    int now = Environment.TickCount;
    bool isFinal = p >= 1.0;
    if (!isFinal && now - state.LastProgressTickMs < ProgressThrottleMs) return;
    state.LastProgressTickMs = now;

    int value = (int)(p * state.SizeInMegabytes);
    notifier.ReportDownloadProgress(state, value);
});
```

`RobinForm.ReportDownloadProgress` holds the dispatch + disposed checks. Now unit tests can substitute a notifier that records progress calls and verify throttling behavior without touching WinForms.

### Performance effect of Phase 2

- **Virtual dispatch on `notifier.*` calls.** Per-download frequency of each method: 1× for most, 1× per progress tick (≤10 Hz after throttle) for `ReportDownloadProgress`. Virtual interface call cost: ~1–5 ns. Total overhead over a download: tens of microseconds at worst. **Not measurable.**
- **Allocations.** Zero extra allocations per call — `notifier` is a reference to the existing form. No boxing. No closures added.
- **JIT devirtualization.** `RobinForm` is a sealed-in-practice single implementation; in the shipped build the JIT will often devirtualize. In tests with a fake, it won't — irrelevant.
- **Progress throttle preserved.** The 100 ms throttle and the `IsDisposed`/`IsHandleCreated` early-exits stay exactly where they are. **Do not move the guards to the interface implementation without keeping the throttle check before the virtual call**, or we'd go from 0 dispatches/sec to 60+ dispatches/sec (YoutubeExplode reports progress roughly per chunk).

**Net:** no regression expected.

---

## Phase 3 — Wrap the YoutubeExplode calls in `IYoutubeClientAdapter`

`YouTubeExplodeVideoDownloader` calls four distinct `YoutubeClient` operations. We wrap them behind an adapter so tests can inject canned videos, canned manifests, and canned download behavior.

### The adapter interface

```csharp
internal interface IYoutubeClientAdapter
{
    ValueTask<YoutubeExplode.Videos.Video> GetVideoAsync(string url, CancellationToken ct = default);
    ValueTask<StreamManifest> GetManifestAsync(string url, CancellationToken ct = default);
    Task DownloadMuxedAsync(
        IReadOnlyList<IStreamInfo> streams,
        ConversionRequest request,
        IProgress<double> progress,
        CancellationToken ct);
    Task DownloadDirectAsync(
        string url,
        string filePath,
        Action<ConversionRequestBuilder> configure,
        IProgress<double> progress,
        CancellationToken ct);
}
```

### The real implementation

```csharp
internal sealed class YoutubeClientAdapter : IYoutubeClientAdapter
{
    private readonly YoutubeClient _youtube;

    public YoutubeClientAdapter(HttpClient httpClient)
    {
        _youtube = new YoutubeClient(httpClient);
    }

    public ValueTask<YoutubeExplode.Videos.Video> GetVideoAsync(string url, CancellationToken ct)
        => _youtube.Videos.GetAsync(url, ct);

    public ValueTask<StreamManifest> GetManifestAsync(string url, CancellationToken ct)
        => _youtube.Videos.Streams.GetManifestAsync(url, ct);

    public Task DownloadMuxedAsync(
        IReadOnlyList<IStreamInfo> streams, ConversionRequest request,
        IProgress<double> progress, CancellationToken ct)
        => _youtube.Videos.DownloadAsync(streams, request, progress, ct);

    public Task DownloadDirectAsync(
        string url, string filePath, Action<ConversionRequestBuilder> configure,
        IProgress<double> progress, CancellationToken ct)
        => _youtube.Videos.DownloadAsync(url, filePath, configure, progress, ct);
}
```

### Before — `Robin/YouTubeExplodeVideoDownloader.cs:46-58`

```csharp
private readonly YoutubeClient youtube;
private readonly string baseFilePath;
private readonly string ffmpegPath;

public YouTubeExplodeVideoDownloader(string baseFilePath)
{
    this.baseFilePath = baseFilePath;
    this.ffmpegPath = RobinUtils.GetPathToFFMPEG();
    youtube = new YoutubeClient(sharedHttpClient);
}

public async ValueTask<string> GetVideoTitle(string url)
{
    var videoInfo = await youtube.Videos.GetAsync(url).ConfigureAwait(false);
    videoInfoCache[url] = videoInfo;
    return videoInfo.Title;
}
```

### After

```csharp
private readonly IYoutubeClientAdapter _client;
private readonly string _baseFilePath;
private readonly string _ffmpegPath;

public YouTubeExplodeVideoDownloader(string baseFilePath)
    : this(baseFilePath, new YoutubeClientAdapter(sharedHttpClient), FFmpegPathResolver.Default()) { }

// Test-visible constructor (internal so [InternalsVisibleTo("Robin.Tests")] grants access)
internal YouTubeExplodeVideoDownloader(
    string baseFilePath,
    IYoutubeClientAdapter client,
    FFmpegPathResolver ffmpegResolver)
{
    _baseFilePath = baseFilePath;
    _client = client;
    _ffmpegPath = ffmpegResolver.Resolve();
}

public async ValueTask<string> GetVideoTitle(string url)
{
    var videoInfo = await _client.GetVideoAsync(url).ConfigureAwait(false);
    videoInfoCache[url] = videoInfo;
    return videoInfo.Title;
}
```

The manifest-fetch site at [YouTubeExplodeVideoDownloader.cs:87](../Robin/YouTubeExplodeVideoDownloader.cs) becomes `_client.GetManifestAsync(videoUrl)`. The two `youtube.Videos.DownloadAsync` calls at lines 214 and 226 become `_client.DownloadMuxedAsync` / `_client.DownloadDirectAsync`.

### Tests this unlocks

```csharp
[Fact]
public async Task DownloadBestVideo_PrefersMp4StreamsOverWebm()
{
    var fakeClient = new FakeYoutubeClientAdapter
    {
        Manifest = BuildManifestWith(
            VideoStream("mp4", height: 720),
            VideoStream("webm", height: 1080),  // higher res but webm
            AudioStream("mp4")),
    };
    var sut = new YouTubeExplodeVideoDownloader("C:\\tmp", fakeClient, new FakeFFmpegResolver());

    await sut.DownloadVideo(new NullNotifier(), "url", new DownloadState());

    fakeClient.DownloadedMuxedStreams.Should().ContainItemsAssignableTo<...Mp4...>();
}

[Fact]
public async Task DownloadBestVideo_ManifestFailure_FallsBackToLiveDownload()
{
    var fakeClient = new FakeYoutubeClientAdapter { ManifestThrows = new HttpRequestException() };
    var sut = new YouTubeExplodeVideoDownloader(...);

    await sut.DownloadVideo(new NullNotifier(), "url", new DownloadState());

    fakeClient.DirectDownloadCalls.Should().ContainSingle();
    fakeClient.MuxedDownloadCalls.Should().BeEmpty();
}

[Theory]
[InlineData("Valid | Title?", "Valid  Title")]
[InlineData("a/b\\c:d*e", "abcde")]
public void MakeValidVideoTitle_StripsInvalidFilenameChars(string raw, string expected)
{
    // requires MakeValidVideoTitle to be made internal or tested indirectly
}
```

### Performance effect of Phase 3

- **One extra virtual call per YoutubeExplode operation.** Frequency: 1× `GetVideoAsync` per download-title fetch, 1× `GetManifestAsync` per download, 1× download operation. Each one is preceded by multi-second network I/O. Overhead of 1–5 ns per network round-trip is **seven orders of magnitude below the I/O cost.** Unmeasurable.
- **Progress flow unchanged.** `IProgress<double>` is still passed straight into YoutubeExplode's `DownloadAsync`. No extra dispatch on the progress hot path.
- **Allocation.** One extra object — the `YoutubeClientAdapter` instance — per `YouTubeExplodeVideoDownloader`. One object per app lifetime. ~40 bytes. Irrelevant.
- **`sharedHttpClient` stays static.** Phase 3 does not touch the HTTP client pooling that landed in the recent perf-overhaul commit. `YoutubeClientAdapter` is constructed once and holds a reference to the same shared `HttpClient`.
- **`videoInfoCache`.** Unchanged — still a per-instance `ConcurrentDictionary` in `YouTubeExplodeVideoDownloader`.
- **Sealed adapter class.** Declare `YoutubeClientAdapter` as `sealed` so the JIT can still devirtualize `_client.*` calls in the shipped build.

**Net:** no regression expected.

---

## Phase 4 — `IFileSystem` + `FFmpegPathResolver`

`RobinUtils.GetPathToFFMPEG` at [RobinUtils.cs:25-72](../Robin/RobinUtils.cs) has five filesystem primitives to abstract:

```csharp
File.Exists(cachePath)
File.ReadAllText(cachePath)
File.WriteAllText(cachePath, value)
Directory.CreateDirectory(dir)
Directory.GetDirectories(baseDir, pattern, SearchOption.TopDirectoryOnly)
```

### The abstraction

```csharp
internal interface IFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    void CreateDirectory(string path);
    string[] GetDirectories(string baseDir, string searchPattern);
}

internal sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public string[] GetDirectories(string baseDir, string pattern)
        => Directory.GetDirectories(baseDir, pattern, SearchOption.TopDirectoryOnly);
}
```

### Extract `FFmpegPathResolver` from `RobinUtils`

`RobinUtils.GetPathToFFMPEG` becomes a thin shim that delegates to a `FFmpegPathResolver` instance. The resolver owns all the path logic and takes `IFileSystem` + `Environment` accessor via its ctor.

### Before — `Robin/RobinUtils.cs:25-72` (abbreviated)

```csharp
public static string GetPathToFFMPEG()
{
    try
    {
        if (File.Exists(FFmpegCachePath))
        {
            string cached = File.ReadAllText(FFmpegCachePath).Trim();
            if (File.Exists(cached)) return cached;
        }
    }
    catch (Exception ex) { logger.Warn(ex, "cache read failed"); }

    try
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string fullWinGetPackagesPath = Path.Combine(appDataPath, wingetPackagesPath);
        string ffmpegBaseWinGetFolderName = GetDirectoryThatBeginsWith(GyanFFmpegWingetDirName, fullWinGetPackagesPath);
        // ...
    }
    catch (Exception e) { DisplayAndLogException(e); throw; }
}
```

### After — new `Robin/FFmpegPathResolver.cs`

```csharp
internal sealed class FFmpegPathResolver
{
    private readonly IFileSystem _fs;
    private readonly string _localAppData;
    private readonly string _cachePath;

    public FFmpegPathResolver(IFileSystem fs, string localAppDataPath)
    {
        _fs = fs;
        _localAppData = localAppDataPath;
        _cachePath = Path.Combine(_localAppData, "Robin", "ffmpeg_path.txt");
    }

    public static FFmpegPathResolver Default()
        => new FFmpegPathResolver(
            new RealFileSystem(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public string Resolve()
    {
        if (TryReadCache(out var cached)) return cached;
        var resolved = ResolveFromWinGet();
        TryWriteCache(resolved);
        return resolved;
    }

    private bool TryReadCache(out string path) { /* uses _fs */ }
    private string ResolveFromWinGet() { /* uses _fs, _localAppData */ }
    private void TryWriteCache(string path) { /* uses _fs */ }
}
```

`RobinUtils.GetPathToFFMPEG` becomes a one-liner:

```csharp
public static string GetPathToFFMPEG() => FFmpegPathResolver.Default().Resolve();
```

…or gets deleted outright once callers (only `YouTubeExplodeVideoDownloader`) inject `FFmpegPathResolver` directly.

### Tests this unlocks

```csharp
[Fact]
public void Resolve_CacheHit_ReturnsCachedPathWithoutScanningWinGet()
{
    var fs = new InMemoryFileSystem();
    fs.AddFile("C:\\localapp\\Robin\\ffmpeg_path.txt", "C:\\cached\\ffmpeg.exe");
    fs.AddFile("C:\\cached\\ffmpeg.exe", "");
    var sut = new FFmpegPathResolver(fs, "C:\\localapp");

    sut.Resolve().Should().Be("C:\\cached\\ffmpeg.exe");
    fs.GetDirectoriesCalls.Should().BeEmpty();  // no scan
}

[Fact]
public void Resolve_CacheStale_ReScansWinGetAndRewritesCache()
{
    var fs = new InMemoryFileSystem();
    fs.AddFile("C:\\localapp\\Robin\\ffmpeg_path.txt", "C:\\gone\\ffmpeg.exe");  // cached path doesn't exist
    fs.AddDirectory("C:\\localapp\\Microsoft\\WinGet\\Packages\\Gyan.FFmpeg_Microsoft.Winget_v1");
    fs.AddDirectory("C:\\localapp\\Microsoft\\WinGet\\Packages\\Gyan.FFmpeg_Microsoft.Winget_v1\\ffmpeg-7.0");
    // ...
    var sut = new FFmpegPathResolver(fs, "C:\\localapp");

    var result = sut.Resolve();

    result.Should().EndWith("ffmpeg.exe");
    fs.WrittenFiles.Should().ContainKey("C:\\localapp\\Robin\\ffmpeg_path.txt");
}

[Fact]
public void Resolve_NoWinGetInstall_ThrowsDirectoryNotFoundException()
{
    var fs = new InMemoryFileSystem();
    var sut = new FFmpegPathResolver(fs, "C:\\localapp");

    Action act = () => sut.Resolve();

    act.Should().Throw<DirectoryNotFoundException>();
}
```

### Performance effect of Phase 4

- **`Resolve()` is called once per `YouTubeExplodeVideoDownloader` construction** (i.e. once per app lifetime in the real code). Virtual dispatch cost on the five `IFileSystem` methods is irrelevant at that frequency.
- **`GetDirectoryThatBeginsWith` can stay in `RobinUtils`** (its tests already cover it). Or fold it into `FFmpegPathResolver` as a private helper and delete the `RobinUtils` version. Either is fine; the existing tests need a trivial update either way.
- **Cache file behavior identical** — `ffmpeg_path.txt` location and format unchanged, so existing installs keep working on first run after upgrade.

**Net:** no regression expected.

---

## Phase 5 (optional) — Wire DI through `RobinForm`

Currently [RobinForm.cs:20](../Robin/RobinForm.cs) instantiates the downloader inline:

```csharp
YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);
```

After phases 2–4, this can change to:

```csharp
// Before
YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);

public RobinForm() { InitializeComponent(); ... }

// After — overload for tests; the zero-arg ctor preserves existing behavior
private readonly YouTubeVideoDownloader videoDownloader;

public RobinForm() : this(new YouTubeExplodeVideoDownloader(baseFilePath)) { }

internal RobinForm(YouTubeVideoDownloader videoDownloader)
{
    this.videoDownloader = videoDownloader;
    InitializeComponent();
    ...
}
```

This is optional because it unlocks `RobinForm` tests that are UI-driven (event handlers, `Task.Run` chains) — territory this plan declared out of scope. Recommend leaving it for a dedicated UI-testing follow-up.

---

## Performance analysis — aggregated

The user's recent `44794ca` commit "Performance overhaul: HTTP pool, stream dedup, progress path, muxing" signals perf is a real concern. Here is the honest accounting.

| Change | Hot path? | Per-call overhead | Per-download overhead | Risk |
|---|---|---|---|---|
| Interface dispatch on `IDownloadUiNotifier.*` | 10× most methods (once per download); **~10 Hz** `ReportDownloadProgress` | ~1–5 ns | < 1 μs | **None** — dwarfed by UI dispatch cost |
| Interface dispatch on `IYoutubeClientAdapter.*` | No — preceded by network I/O | ~1–5 ns | < 20 ns | None |
| `YoutubeClientAdapter` allocation | Construction only | ~40 bytes once | — | None |
| Interface dispatch on `IFileSystem.*` | No — 5 calls at app startup | ~1–5 ns | < 50 ns (one-time) | None |
| `FFmpegPathResolver` allocation | Construction only | ~60 bytes once | — | None |

### Where regressions *could* happen if the refactor is done sloppily

1. **Moving the progress-bar disposal guards across the interface boundary without keeping the 100 ms throttle in front of the virtual call.** Would go from zero virtual calls per progress tick to one per tick. Still negligible, but avoid it.
2. **Constructing a new `YoutubeClientAdapter` per call** instead of once per `YouTubeExplodeVideoDownloader`. Would allocate an adapter (and potentially a new `YoutubeClient`) per download. Keep it as a field, as shown.
3. **Breaking `sharedHttpClient` reuse.** The recent HTTP-pool commit depends on the singleton static `HttpClient`. `YoutubeClientAdapter` must take an `HttpClient` by reference, not construct its own. Unit tests can pass a stub-owned client.
4. **Turning `sealed` into non-`sealed`.** Sealed classes give the JIT inlining opportunities. Keep `YoutubeClientAdapter`, `RealFileSystem`, `FFmpegPathResolver` all `sealed`.
5. **Reading the FFmpeg cache file inside `Resolve()` on every download.** Current code caches the path in the `ffmpegPath` field after the constructor runs; keep that invariant. `Resolve()` is only called from the ctor.

### Benchmark recommendation

For peace of mind, add one BenchmarkDotNet test *before* the refactor and run it again *after* each phase. Target: `DownloadVideoAsync_Explode`'s progress callback throughput with a no-op notifier. Expected result: indistinguishable. If a regression shows up, it'll be in the single-digit-nanoseconds range per call and won't move the wall-clock needle on a real download.

---

## Phased rollout — recommended commit order

1. **Phase 1** (delete `FastYouTube.cs`, `RobinVideoDownloader.cs`, maybe `VideoLibrary` package) — one commit.
2. **Phase 2a** (`IDownloadUiNotifier`, `RobinForm : IDownloadUiNotifier`, change `YouTubeVideoDownloader.DownloadVideo` signature) — one commit.
3. **Phase 2b** (move progress-bar dispatch into `RobinForm.ReportDownloadProgress`) — one commit.
4. **Phase 3** (`IYoutubeClientAdapter`, `YoutubeClientAdapter`, downloader ctor overload) — one commit.
5. **Phase 4** (`IFileSystem`, `RealFileSystem`, `FFmpegPathResolver`, `RobinUtils.GetPathToFFMPEG` → one-liner or deleted) — one commit.
6. **Test batch** (add the unit tests each phase unlocks) — one commit.

Each commit leaves the app fully functional and `Robin.Tests` green.

---

## Critical files to create or modify

**Created:**

- `Robin/IDownloadUiNotifier.cs` — interface
- `Robin/IYoutubeClientAdapter.cs` — interface
- `Robin/YoutubeClientAdapter.cs` — real implementation (sealed)
- `Robin/IFileSystem.cs` — interface
- `Robin/RealFileSystem.cs` — real implementation (sealed)
- `Robin/FFmpegPathResolver.cs` — extracted resolver
- `Robin.Tests/Fakes/FakeDownloadUiNotifier.cs`
- `Robin.Tests/Fakes/FakeYoutubeClientAdapter.cs`
- `Robin.Tests/Fakes/InMemoryFileSystem.cs`
- `Robin.Tests/YouTubeExplodeVideoDownloaderTests.cs` (stream selection, live fallback, CliWrap teardown-bug detection, progress throttle)
- `Robin.Tests/FFmpegPathResolverTests.cs` (cache hit / cache stale / not found)

**Modified:**

- [Robin/YouTubeVideoDownloader.cs](../Robin/YouTubeVideoDownloader.cs) — change `DownloadVideo` parameter type from `RobinForm` to `IDownloadUiNotifier`
- [Robin/YouTubeExplodeVideoDownloader.cs](../Robin/YouTubeExplodeVideoDownloader.cs) — take `IYoutubeClientAdapter` and `FFmpegPathResolver` via new internal ctor; keep zero-arg public ctor for WinForms initialization
- [Robin/RobinForm.cs](../Robin/RobinForm.cs) — implement `IDownloadUiNotifier`; add `ReportDownloadProgress` method holding the progress-bar dispatch
- [Robin/RobinUtils.cs](../Robin/RobinUtils.cs) — `GetPathToFFMPEG` delegates to `FFmpegPathResolver.Default().Resolve()`, or gets removed entirely; decide after Phase 4
- [Robin/Robin.csproj](../Robin/Robin.csproj) — remove `FastYouTube.cs`, `RobinVideoDownloader.cs` from `<Compile Include=...>`

**Deleted:**

- `Robin/FastYouTube.cs`
- `Robin/RobinVideoDownloader.cs`

---

## Verification

1. After each phase: `msbuild Robin.sln -p:Configuration=Debug` must succeed with no new warnings.
2. After each phase: `dotnet test --no-build` must show all previously-passing tests still pass; new phase-specific tests must be green.
3. After all phases: launch `Robin.exe`, download a normal VOD video end-to-end, cancel a download mid-flight, and try a live stream (to exercise the manifest-failure fallback). All three should behave identically to today.
4. Run the ClickOnce published build on a clean machine once; `RobinUpdater` path is out of scope for automated testing but should be smoke-tested by hand.
5. (Optional) Run BenchmarkDotNet on the progress callback before and after — difference should be < 10 ns per call.

---

## Summary of decisions baked into this plan

- **Seams:** four interfaces (`IDownloadUiNotifier`, `IYoutubeClientAdapter`, `IFileSystem`) + one extracted class (`FFmpegPathResolver`). No DI container.
- **YoutubeExplode types leak through the adapter.** Intentional — the goal is unit-testable orchestration, not library swap-out.
- **Progress path stays optimized.** 100 ms throttle stays in the downloader; the virtual call happens at most 10 Hz after throttle.
- **Dead code removed first.** `FastYouTube.cs`, `RobinVideoDownloader.cs` gone before refactoring to reduce the surface we're changing.
- **`sharedHttpClient` stays static.** HTTP pool reuse from the recent perf commit is preserved verbatim.
- **`RobinForm` DI is optional.** Orchestration tests don't need it; UI tests (out of scope) would.
- **Performance:** no expected regression. Every hot path is either unchanged or gated by network/UI costs that dwarf virtual dispatch by 6+ orders of magnitude.
