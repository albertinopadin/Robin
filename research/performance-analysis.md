# Robin Performance Research — Top 5 Wins

## Context

Robin is a .NET Framework 4.8 WinForms YouTube downloader built on **YoutubeExplode 6.5.7**. This document captures a deep inspection of the current code and external research into how YoutubeExplode (and the surrounding .NET Framework HTTP/threading stack) is best used for speed and throughput.

Findings come from reading every relevant file in `Robin/` (verified via direct file reads, not just a search agent's report) and from the documentation, GitHub issues, and blog posts listed in **Sources** at the end.

---

## Verified Code Facts (line numbers from current HEAD)

- `YouTubeExplodeVideoDownloader.cs:18-27` — single `YoutubeClient` per `RobinForm` instance (good: reuses internal HttpClient).
- `YouTubeExplodeVideoDownloader.cs:58` — `GetVideoInfo` call (roundtrip #1 to fetch video metadata for title).
- `YouTubeExplodeVideoDownloader.cs:68` — `GetManifestAsync` call (roundtrip #2 for the stream list).
- `YouTubeExplodeVideoDownloader.cs:97-101` — picks `GetVideoStreams().GetWithHighestVideoQuality()` (video-only OR muxed; never picks an audio stream).
- `YouTubeExplodeVideoDownloader.cs:202-206` — calls `youtube.Videos.DownloadAsync(videoId, filePath, …)` with the **video ID**, not the pre-selected stream. Per YoutubeExplode.Converter docs, this overload **re-fetches the manifest internally and runs its own stream selection** — roundtrip #3.
- `YouTubeExplodeVideoDownloader.cs:204` — only sets FFmpeg path; no `ConversionPreset`, no container hint. Converter defaults may re-encode.
- `YouTubeExplodeVideoDownloader.cs:191-198` — progress callback: `if (progressBarValue % 2 == 0)` is a crude size-dependent throttle. Fires on every `IProgress<double>.Report`.
- `RobinForm.cs:260-262` — `GetProgressBarForVideo` does `listView_downloads.Controls.OfType<ProgressBar>().FirstOrDefault(i => i.Name == videoName)` — **O(n) scan of all child controls on every progress tick**.
- `RobinForm.cs:276-287` — `SafeSetProgressBarValue` uses **blocking `Invoke`** (not `BeginInvoke`) with a recursive `InvokeRequired` pattern. Called from the progress callback → stalls the download thread while the UI thread is busy.
- `RobinForm.cs:204-215, 217-241, 243-258, 289-308, 374-404, 406-434, 469-490, 492-516, 523-544, 546-562` — every marshaling helper uses the same recursive pattern with blocking `Invoke`.
- No `.ConfigureAwait(false)` anywhere in the async chain (`YouTubeExplodeVideoDownloader.cs` or `RobinForm.cs`).
- No `ServicePointManager.DefaultConnectionLimit` setup anywhere in the project. On .NET Framework 4.8 non-ASP.NET apps, the default is **2 concurrent connections per host**.
- `Program.cs` — only NLog init; no HTTP/threading setup.

## Key YoutubeExplode Findings from Docs/Issues

- **`YoutubeClient` has constructors accepting `HttpClient`**: `YoutubeClient(HttpClient http)` and `YoutubeClient(HttpClient http, IReadOnlyList<Cookie> initialCookies)` — lets you set User-Agent, connection handler, cookies.
- **Muxed streams are deprecated and capped at 720p30**. Only reliable way to get >720p is separate audio-only + video-only streams then mux.
- **FFmpeg stream-copy (`-c copy`) avoids transcoding.** Converter supports `ConversionPreset.UltraFast` and `SetContainer("mp4")`; when streams already match the container codec, muxing becomes a file-rewrite (10–100× faster than transcoding).
- **Pre-fetched streams overload exists**: `DownloadAsync(IEnumerable<IStreamInfo> streams, string path, Action<ConversionRequestBuilder> cfg, IProgress<double>, CancellationToken)` — passes already-selected streams, **skipping the internal manifest re-fetch**.
- **YouTube's `n` parameter throttles to ~50 KB/s** unless deciphered. YoutubeExplode handles deciphering internally in 6.5.x.
- **Range-request workaround**: 10 MB range chunks bypass the DASH URL throttler. The unused `FastYouTube.cs` in this repo implements exactly this pattern — evidence a prior author knew about it. YoutubeExplode 6.5.x also uses chunked segment reads internally.
- `ServicePointManager.DefaultConnectionLimit` must be set **before any request fires** to the endpoint — first fire locks it in.

---

## Top 5 Performance Wins (ranked by impact)

### #1 — Eliminate the double manifest/metadata fetch and pass pre-selected streams to the Converter

**Problem.** The download flow hits YouTube's API 3 times per video:
1. `GetVideoInfo` (line 58) for title + id.
2. `GetManifestAsync` (line 68) to pick a quality.
3. `youtube.Videos.DownloadAsync(videoId, …)` (line 202) — this overload re-resolves the manifest *internally* and re-picks streams, so the selection from step 2 is thrown away.

Issue #459 benchmarked `VideoClient.GetAsync` at ~2.3s; #366 reports `GetManifestAsync` at ~16s in degenerate cases. Duplicating these is a material startup-latency hit, and because the Converter picks its own streams you may silently download a different (lower-quality or wrong-container) set than what the UI advertised.

**Fix.** Resolve the manifest once, pick best **audio-only + video-only** streams in code, then call the `DownloadAsync(IEnumerable<IStreamInfo>, path, cfg, progress, ct)` overload. Drop the separate `GetVideoInfo` call — the title is on the manifest's parent video object (or use `youtube.Videos.GetAsync` just once and reuse).

**Expected win.** Removes 1 full manifest round-trip (often multi-second) per download, guarantees selected-quality honesty, enables the `-c copy` win (#4) because you now control container selection.

---

### #2 — Inject a tuned `HttpClient` into `YoutubeClient` and raise `ServicePointManager.DefaultConnectionLimit`

**Problem.** On .NET Framework 4.8, `ServicePointManager.DefaultConnectionLimit` defaults to **2** for non-ASP.NET apps. Every concurrent download past the second will queue at the TCP/connection-group layer, regardless of how many `Task.Run`s you spawn. Segment downloads inside YoutubeExplode (which parallelizes reads of the DASH stream URL) hit the same cap. The current code uses `new YoutubeClient()` with no customization, so all HTTP goes through the default handler with the 2-connection ceiling.

**Fix.**
- In `Program.cs` before the form starts: `ServicePointManager.DefaultConnectionLimit = 32;`
- Build a shared `HttpClient` with `HttpClientHandler { AutomaticDecompression = All }` and inject into `new YoutubeClient(sharedHttp)`.
- Optional: set a desktop-client User-Agent to reduce rate-limit risk (issue #489 shows Windows DemoConsole being rejected with default UA).

**Expected win.** Unlocks real parallelism for concurrent downloads and for YoutubeExplode's internal segmented reads of a single stream. Often 2–5× throughput when multiple downloads run or when YoutubeExplode issues parallel range requests.

---

### #3 — Fix the progress-reporting hot path (O(n) lookup + blocking `Invoke` + crude throttle)

**Problem.** Every progress tick does:
1. Compute `(int)(p * size)` and test `% 2 == 0`. For a 100 MB file that fires 100+ UI updates; for a 9 MB fallback, it caps at ~5. Not time-based, not FPS-bounded.
2. `GetProgressBarForVideo` scans every child `Control` on the ListView via LINQ (`RobinForm.cs:262`).
3. `SafeSetProgressBarValue` calls **blocking `Invoke`** — the download thread blocks on every tick whenever the UI thread is busy. `BeginInvoke` would suffice.
4. The recursive `InvokeRequired` pattern adds a function call + re-check that is only ever false once (always called from background thread from the `Progress<double>` capture context).

**Fix.**
- Store the `ProgressBar` reference directly on `DownloadState` when the bar is created (remove the O(n) lookup).
- Replace `Invoke` with `BeginInvoke` in the progress path.
- Throttle by wall-clock: skip updates that arrive within ~75–100 ms of the previous one (store last-update `Stopwatch` ticks on `DownloadState`).
- Collapse the recursive marshaling helpers to a single `BeginInvoke` when `InvokeRequired`.

**Expected win.** Eliminates a chronic source of micro-stalls on the download thread; noticeable speedup on large files, multi-download scenarios, and on slow/old machines where UI paint latency is significant.

---

### #4 — Use FFmpeg stream-copy (`ConversionPreset.UltraFast`) and pick codec-matched streams

**Problem.** Line 204 passes only `SetFFmpegPath`. The Converter's default preset may transcode (CPU-bound re-encode) even when the input codecs are already compatible with the output container. For a 1080p H.264 video + AAC audio → MP4 muxing, transcoding vs stream-copy can be the difference between **several minutes and a few seconds** on end-of-pipeline CPU time.

**Fix.**
```csharp
converter => converter
    .SetFFmpegPath(this.ffmpegPath)
    .SetContainer(Container.Mp4)
    .SetPreset(ConversionPreset.UltraFast)
```
And pair this with #1 — pre-select an MP4-compatible H.264 video stream + M4A/AAC audio stream so FFmpeg can `-c copy` both tracks.

**Expected win.** Muxing step drops from CPU-bound to IO-bound; on long videos this is the single biggest wall-clock improvement after the download completes. Also drops CPU load during the mux (important on laptops/battery).

---

### #5 — Add `ConfigureAwait(false)` and flatten the `Task.Run` nesting

**Problem.** Every `await` in `YouTubeExplodeVideoDownloader.cs` (7 sites) and `RobinForm.cs` (the download-trigger lambdas) lacks `.ConfigureAwait(false)`. After each `await`, the continuation hops back to the captured sync context — the WinForms UI thread — even though the continuations don't touch UI. The code also does:
- `RobinForm.cs:106` → `Task.Run` for title fetch.
- `RobinForm.cs:177` → separate `Task.Run` for `DownloadVideo`.
- Inside, `DownloadBestVideo` recurses on manifest failure with no depth limit.

These nested `Task.Run`s pay thread-pool overhead without adding concurrency.

**Fix.**
- Add `.ConfigureAwait(false)` to every `await` in the downloader class and on background-thread awaits in the form.
- Collapse the two `Task.Run` layers: `RobinDownloadVideoWithChecks` can do the title fetch and then directly await the download without a second `Task.Run` on the UI thread.
- Convert the recursive manifest-failure fallback to an iterative `for` with a cap (e.g. 1 retry).

**Expected win.** Less thread-pool pressure per download (important when the user queues many), fewer UI-context hops under load, and bounded failure recovery. Individually small, but they compound when concurrent downloads are running and the UI thread is already doing paint/progress work.

---

## Honorable Mentions (not top 5 but cheap to add)

- **Remove dead code**: `FastYouTube.cs` (unused VideoLibrary path with leaked `HttpResponseMessage`/stream) and confirm `RobinVideoDownloader.cs` doesn't exist anymore.
- **Limit concurrent downloads** with a `SemaphoreSlim(maxParallel)` to keep YoutubeExplode inside YouTube's rate-limit window (issue #453).
- **Cache FFmpeg path lookup** across app restarts (write to settings) — currently it enumerates `%LOCALAPPDATA%\Microsoft\WinGet\Packages` every launch.
- **Remove `logger.Info` inside `AddProgressBar`** (`RobinForm.cs:330`) — runs on every download-add.

---

## Verification (how to confirm each win if implemented)

| Win | Measurement |
|-----|-------------|
| #1 | Wall-clock between URL submit and first byte written; expect −2 to −10 s per video. |
| #2 | Simultaneously queue 3 downloads, compare aggregate MB/s vs baseline. |
| #3 | Run a 1 GB download, measure UI thread CPU % and download thread blocked-time via PerfView. |
| #4 | Time `DownloadAsync` for a 10-minute 1080p video: baseline vs patched. FFmpeg child-process CPU should stay near 0 %. |
| #5 | Enable `dotnet-trace` concurrency view, count thread-pool hops per download. |

All improvements are observable without automated tests; the repo has no test framework.

---

## Sources (URLs visited during this research)

- [YoutubeExplode — main repo README](https://github.com/Tyrrrz/YoutubeExplode)
- [YoutubeExplode/YoutubeClient.cs — constructor overloads](https://github.com/Tyrrrz/YoutubeExplode/blob/master/YoutubeExplode/YoutubeClient.cs)
- [YoutubeExplode.Converter README — ConversionPreset, ConversionRequestBuilder](https://github.com/Tyrrrz/YoutubeExplode/blob/master/YoutubeExplode.Converter/Readme.md)
- [Issue #459 — VideoClient.GetAsync is extremely slow](https://github.com/Tyrrrz/YoutubeExplode/issues/459)
- [Issue #366 — GetManifestAsync method too slow](https://github.com/Tyrrrz/YoutubeExplode/issues/366)
- [Issue #573 — DASH URL transfer-rate slowdown](https://github.com/Tyrrrz/YoutubeExplode/issues/573)
- [Issue #85 — DownloadMediaStreamAsync taking too long](https://github.com/Tyrrrz/YoutubeExplode/issues/85)
- [Issue #497 — inject HttpClient with cookies](https://github.com/Tyrrrz/YoutubeExplode/issues/497)
- [Issue #453 — rate limiting](https://github.com/Tyrrrz/YoutubeExplode/issues/453)
- [Issue #489 — YouTube rejects DemoConsole on Windows (UA)](https://github.com/Tyrrrz/YoutubeExplode/issues/489)
- [Issue #286 — injecting native HttpClient (Xamarin)](https://github.com/Tyrrrz/YoutubeExplode/issues/286)
- [Discussion #570 — rate-limit / inject authenticated HttpClient](https://github.com/Tyrrrz/YoutubeExplode/discussions/570)
- [Discussion #652 — Blazor WASM client creation](https://github.com/Tyrrrz/YoutubeExplode/discussions/652)
- [Issue #483 — response ended prematurely (stack trace shows CopyBufferedToAsync)](https://github.com/Tyrrrz/YoutubeExplode/issues/483)
- [Issue #695 — can't download stream on legacy .NET Framework](https://github.com/Tyrrrz/YoutubeExplode/issues/695)
- [Tyrrrz blog — Reverse-Engineering YouTube, Revisited (10 MB range workaround, client impersonation)](https://tyrrrz.me/blog/reverse-engineering-youtube-revisited)
- [Hacker News — Bypassing YouTube video download throttling (range headers, `range=` query param)](https://news.ycombinator.com/item?id=37117338)
- [yt-dlp issue #6400 — range-fix throttling for some clients](https://github.com/yt-dlp/yt-dlp/issues/6400)
- [youtube-dl PR #30184 — unthrottle via `n` parameter challenge](https://github.com/ytdl-org/youtube-dl/pull/30184)
- [Microsoft Azure SDK blog — .NET Framework Connection Pool Limits](https://devblogs.microsoft.com/azure-sdk/net-framework-connection-pool-limits/)
- [Microsoft Learn — HttpClient guidelines for .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- [Microsoft Learn — ServicePointManager.DefaultConnectionLimit Property](https://learn.microsoft.com/en-us/dotnet/api/system.net.servicepointmanager.defaultconnectionlimit)
- [Steve Gordon — HttpClient Connection Pooling in .NET Core](https://www.stevejgordon.co.uk/httpclient-connection-pooling-in-dotnet-core)
- [WebScraping.AI — Improving HttpClient performance in high-load apps](https://webscraping.ai/faq/httpclient-c/how-do-i-improve-the-performance-of-httpclient-c-in-a-high-load-application)
- [NuGet — YoutubeExplode 6.0.8 package listing (latest behavior notes)](https://www.nuget.org/packages/YoutubeExplode/6.0.8)
- [NuGet — YoutubeExplode.Converter 6.1.0](https://www.nuget.org/packages/YoutubeExplode.Converter/6.1.0)
- [Luis Llamas — How to download YouTube videos from C# with YoutubeExplode](https://www.luisllamas.es/en/csharp-youtube-explode/)
- [Streaming Learning Center — FFmpeg muxing audio and video](https://streaminglearningcenter.com/learning/ffmpeg-to-the-rescue-muxing-audio-and-video-files.html)
- [Mux — How to combine audio and video with FFmpeg](https://www.mux.com/articles/merge-audio-and-video-files-with-ffmpeg)
- [dotnet/runtime #1844 — MaxConnectionsPerServer default](https://github.com/dotnet/runtime/issues/1844)
