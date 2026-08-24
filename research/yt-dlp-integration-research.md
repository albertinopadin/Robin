# Robin — Integrating yt-dlp as a Download Engine

**Date:** 2026-08-24
**Author:** Claude (research + design — no code in Robin was changed)
**Companion doc:** `research/youtube-cookie-authentication-research.md` (why YoutubeExplode is currently bot-walled; cookie auth). This doc answers the follow-up: *is yt-dlp the best/only alternative, and what does integrating it into Robin look like in code?*
**Robin's relevant stack:** .NET Framework 4.8, WinForms, YoutubeExplode 6.6.1, **CliWrap 3.10.5 already present**, Newtonsoft.Json 13.0.4 already present, FFmpeg resolved via `RobinUtils.GetPathToFFMPEG()`.
**Updated 2026-08-24:** added the JavaScript-runtime (**Deno**) requirement YouTube now imposes — §1.1 explains *why* (signatures/nsig/PO tokens), §3 and §7 cover provisioning — and clarified that **cookies are optional** (yt-dlp downloads public videos anonymously by default), reframed in §9.

---

## 0. TL;DR / Bottom line

- **Is yt-dlp the best alternative?** Yes — decisively. For staying ahead of YouTube's anti-bot changes it is the de-facto industry standard, and it's effectively the *only* option that is actively maintained against exactly the breakage Robin is hitting. Every other C# library (YoutubeExplode included) reverse-implements YouTube's private API in-process and breaks whenever YouTube changes it; yt-dlp has a large community shipping fixes within days, plus native `--cookies-from-browser`, PO-token provider plugins, and format control. See the landscape table in §1.

- **Is it the only alternative?** No, but the others are worse for Robin's problem: pure-C# libs (YoutubeExplode, VideoLibrary/libvideo) share YoutubeExplode's fragility; hosted APIs (cobalt, Invidious/Piped) trade the anti-bot problem for a third-party-service dependency, rate limits, and privacy/ToS concerns. yt-dlp is the robust choice.

- **The trade-off you're taking on:** yt-dlp is an **external ~17 MB executable** (Python bundled via PyInstaller), and as of 2026 YouTube downloads also require a **JavaScript runtime — Deno (~40 MB)** — so you provision **three** external binaries: yt-dlp, FFmpeg (Robin already resolves this), and Deno. Robin must ship or fetch them, keep yt-dlp updated (YouTube breaks it often — auto-update matters), and the PyInstaller-built `yt-dlp.exe` is **GPLv3+** (the source is Unlicense, but the bundled exe includes GPL code). Robin invokes it as a separate process, so that's arm's-length "mere aggregation," but you should ship yt-dlp's license notice. Recommended: **download all three at runtime** rather than bundling — sidesteps redistribution *and* keeps them fresh. See §1.1 for *why* the JS runtime is now required and §7 for provisioning.

- **Do you need a YouTube login / cookies?** **No — not by default.** yt-dlp downloads public videos **anonymously**, so unlike the companion doc's cookie-auth route (where cookies were the *only* lever, and a hard one to pull), Robin can ship with **zero credentials** and most videos just work. Cookies become a **fallback** only for a hard per-IP bot-flag or restricted content (age-restricted / members-only / private) — and then it's a single `--cookies-from-browser` flag, not the Chrome-decryption machinery from the companion doc. Mechanics in §1.1; configuration in §9.

- **How hard is the integration?** Small and clean, because Robin already abstracts the downloader behind `YouTubeVideoDownloader` (the interface you pointed at). A new `YtDlpVideoDownloader : YouTubeVideoDownloader` is a drop-in at `RobinForm.cs:20`. The only real impedance mismatch is **progress/size mapping** (yt-dlp reports fractional progress per-file and downloads video+audio in two passes), addressed in §5.

- **Recommended approach:** Use the **YoutubeDLSharp** NuGet wrapper (BSD-3-Clause, targets `netstandard2.0` so it runs on net48, depends on Newtonsoft.Json 13.0.3 which you already have). It gives a typed API, progress parsing, and binary-download helpers for ~⅓ the code of a hand-rolled process wrapper. A **direct-CliWrap** alternative (no new NuGet, since Robin already has CliWrap) is laid out in §6 for comparison.

- **Recommended architecture:** Keep the existing interface; make yt-dlp the primary engine behind it, with a config toggle. Optionally keep YoutubeExplode as a "fast path" fallback (hybrid, §8). Mirror the existing **testability seam** (`IYtDlpClientAdapter` + a fake) so the orchestration stays unit-tested.

---

## 1. Is yt-dlp the best (or only) alternative?

| Option | Type | Keeps up with YT anti-bot? | Cookies / PO tokens | External dep | Verdict for Robin |
|---|---|---|---|---|---|
| **yt-dlp** (via YoutubeDLSharp or CliWrap) | Bundled CLI (Python) | **Yes — best in class**, community fixes in days | `--cookies`, `--cookies-from-browser`, PO-token plugins, OAuth plugin | ~17 MB exe + FFmpeg (already have) + **Deno ~40 MB** (JS runtime, auto-fetch) | **Recommended** |
| YoutubeExplode (current) | Pure C#, in-process | Weak — reimplements InnerTube in-process with **no JS engine**, so it can't solve nsig or mint PO tokens (§1.1); breaks on each change (Robin is broken now) | cookies only (fragile, no PO token) | None | Keep as optional fast-path only |
| VideoLibrary / libvideo | Pure C#, in-process | Weak — historically breaks often, slower to update than YoutubeExplode | No first-class auth | None | Not competitive |
| NYoutubeDL | CLI wrapper (youtube-dl/yt-dlp) | Depends on yt-dlp, but wrapper less maintained than YoutubeDLSharp | via raw options | yt-dlp exe | Redundant with YoutubeDLSharp |
| cobalt (cobalt.tools) | Hosted/self-host API | Good (their problem), but it's a service | Server-side | A running service (public instance is rate-limited/ToS-bound; self-host = infra) | Adds a service dependency |
| Invidious / Piped APIs | Public proxy front-ends | Unreliable; instances rate-limit / go down | N/A | Third-party instance | Not dependable |

**Why yt-dlp specifically:** it directly targets the failure in Robin's companion doc — the anonymous InnerTube `/player` request being bot-walled. yt-dlp rotates player clients, injects visitor data, supports PO tokens (`--extractor-args "youtube:po_token=..."` and provider plugins), and pulls cookies from your browser natively. Crucially, **you offload the anti-bot arms race to yt-dlp's maintainers** instead of chasing it inside Robin. Several projects on YoutubeExplode's own issue tracker migrated to yt-dlp (via YoutubeDLSharp) for exactly this reason (see companion doc).

**Honest downsides** you're accepting: (1) two external binaries beyond FFmpeg — yt-dlp (~17 MB) and a Deno JS runtime (~40 MB) — to ship/fetch and keep updated; (2) GPLv3+ on the built yt-dlp exe (§7); (3) process-spawn latency and stdout-parsed progress rather than an in-process byte stream; (4) yt-dlp needs periodic self-update or it too will break; (5) as of 2026 a JS runtime is mandatory for YouTube — without it, extraction is deprecated and formats go missing (§1.1).

### 1.1 Why yt-dlp wins (and why it now needs Deno): signatures, nsig, and PO tokens

Everything YouTube does to break Robin reduces to one thing: **it hands out data you can only unscramble by executing YouTube's own JavaScript**, and it increasingly demands proof that the request came from a real browser. Three mechanisms are in play, and together they explain both why YoutubeExplode is stuck and why yt-dlp now needs a JS runtime.

**1. The signature cipher + `n` parameter ("nsig") — why a JS runtime (Deno) is required *now*.** Each media stream URL carries a scrambled signature plus a throttling parameter `n`, both of which must be transformed by functions buried in YouTube's obfuscated, frequently-changing player JavaScript. Get `n` wrong and downloads either fail or are throttled to a crawl. yt-dlp used to do this with a tiny built-in JS interpreter (`jsinterp`); YouTube's player JS has outgrown it, producing *"Signature/nsig extraction failed: Some formats may be missing."* So yt-dlp now **offloads that JavaScript to a real engine — Deno by default** (Node, Bun, and QuickJS also accepted via `--js-runtimes`). As of 2026, **YouTube extraction without a JS runtime is deprecated.** Deno specifically because it's **sandboxed by default** (no filesystem or network access), so it's safe to run YouTube's untrusted JS. This is exactly why YoutubeDLSharp added `Utils.DownloadDeno()`.

**2. PO tokens (Proof-of-Origin) — the *next* job of that same runtime.** YouTube separates two questions: *who are you?* (answered by **cookies** — your session identity) and *is this a genuine client?* (answered by a **PO token** — proof of origin). A PO token is a signed blob produced by **BotGuard**, Google's obfuscated attestation JavaScript (same family as reCAPTCHA): a real browser silently runs that JS, it inspects the environment and solves a challenge, and emits a token that says "this request came from something that can execute our attestation code." The token is **bound to the session** it was minted with and it **expires**, so it can't be forged or freely reused. yt-dlp's roadmap is to **generate PO tokens natively using the same JS runtime** (the maintainers note this "far exceeds" what `jsinterp` could do); today you can also get them via a provider plugin (`bgutil-ytdlp-pot-provider`) or lift one from your browser alongside cookies.

**3. Why YoutubeExplode can't keep up.** It's pure in-process C# with **no JavaScript engine**, so it can neither reliably solve the nsig cipher nor produce a PO token. Its InnerTube clients that avoid the PO-token requirement are exactly the ones YouTube is now rejecting (the failure diagnosed in the companion doc) — leaving it no path forward short of adopting a JS runtime of its own. yt-dlp already made that move. **That is the core reason yt-dlp is not just the best option but effectively the only robust one.**

**The practical payoff for Robin:** you **offload the entire signature/nsig/PO-token arms race to yt-dlp + Deno**, and — because yt-dlp handles the origin problem itself — **most public videos download with no cookies at all** (§9). The honest limit: in the most heavily-flagged cases (datacenter/VPN IPs especially), YouTube can now reject requests even *with* valid cookies and PO tokens, so this is the best available bet, not a permanent guarantee — nothing is right now.

---

## 2. How it fits Robin's architecture

Robin already has the right seam. The contract a download engine must satisfy (`YouTubeVideoDownloader.cs`):

```csharp
internal interface YouTubeVideoDownloader
{
    ValueTask<string> GetVideoTitle(string url);
    Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state);
}
```

A yt-dlp engine is a second implementation of this interface. It must drive the **same UI contract** the YoutubeExplode engine drives, via `IDownloadUiNotifier`:

`SetCursorLoading/Normal`, `SetVideoInfo(RobinVideoInfo)`, `AddVideoToDownloadsList(state, sizeMb)`, `RegisterActiveDownload`, `ClearVideoUrlTextbox`, `ReportDownloadProgress(state, valueMb)`, `UpdateDownloadStatus(item, status)`, `CancelProgressBarForVideo`, `DisableCancelButton`, `CleanupDownload`, `NotifyDownloadFinished`.

The single wiring point is `RobinForm.cs:20`:

```csharp
YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);
```

Everything downstream (`RobinDownloadVideoWithChecks` → `GetVideoTitle`, `DownloadVideo` → `videoDownloader.DownloadVideo(this, ...)` at line 181) is engine-agnostic already. That is why this migration is small.

We also mirror the **testability pattern** used for YoutubeExplode: the engine talks to yt-dlp only through an adapter interface (`IYtDlpClientAdapter`), and an internal constructor injects a fake in tests — exactly like `YouTubeExplodeVideoDownloader` uses `IYoutubeClientAdapter` + `FakeYoutubeClientAdapter`.

---

## 3. Dependencies to add

**`packages.config`** (add one line):
```xml
<package id="YoutubeDLSharp" version="1.2.0" targetFramework="net48" />
```

**`Robin.csproj`** — because it's the old-style project, verify/add the reference by hand (NuGet usually adds it, but per CLAUDE.md item 1 the old project needs manual tending). Robin is net48, so NuGet resolves the `netstandard2.0` asset:
```xml
<Reference Include="YoutubeDLSharp, Version=1.2.0.0, Culture=neutral, processorArchitecture=MSIL">
  <HintPath>packages\YoutubeDLSharp.1.2.0\lib\netstandard2.0\YoutubeDLSharp.dll</HintPath>
  <Private>True</Private>
</Reference>
```
And add every new `.cs` file (below) to `Robin.csproj` as `<Compile Include="...">` — the old project does not auto-include files.

**Newtonsoft.Json:** YoutubeDLSharp depends on 13.0.3; Robin already ships **13.0.4**. No new package needed. **Verify** `App.config` has a `bindingRedirect` for `Newtonsoft.Json` with `newVersion="13.0.0.0"` (it almost certainly does). This is the same class of redirect gotcha CLAUDE.md item 9 warns about.

**No FFmpeg change:** reuse `RobinUtils.GetPathToFFMPEG()` for yt-dlp's `--ffmpeg-location`.

**Deno (new runtime dependency — not a NuGet):** as of 2026 yt-dlp needs a JavaScript runtime for YouTube (§1.1). This is **not a package** — it's a separate ~40 MB binary (`deno.exe`) fetched at runtime alongside yt-dlp; YoutubeDLSharp provides `Utils.DownloadDeno()`. Provisioning and how yt-dlp discovers it are covered in §7. (No `App.config`/csproj entry — it's an external tool, like FFmpeg.)

**Test project:** `Robin.Tests.csproj` (SDK-style) — add `<PackageReference Include="YoutubeDLSharp" Version="1.2.0" />` so the fake can reference `DownloadProgress`/`VideoData` types.

---

## 4. The code (Option A — YoutubeDLSharp, recommended)

Three new files, mirroring the YoutubeExplode trio (`YouTubeExplodeVideoDownloader` / `IYoutubeClientAdapter` / `YoutubeClientAdapter`).

### 4.1 `Robin/IYtDlpClientAdapter.cs` (new)

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

        // Self-update: wraps `yt-dlp -U`. Returns yt-dlp's status line
        // (e.g. "yt-dlp is up to date (stable@...)"). Tests no-op this.
        Task<string> UpdateAsync();
    }
}
```

### 4.2 `Robin/YtDlpClientAdapter.cs` (new)

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
                YoutubeDLPath = ytDlpPath,          // absolute path to yt-dlp.exe
                FFmpegPath = ffmpegPath,            // RobinUtils.GetPathToFFMPEG()
                OutputFolder = outputFolder,        // RobinForm.baseFilePath (MyVideos)
                OutputFileTemplate = "%(title)s.%(ext)s",
                RestrictFilenames = false,          // Robin sanitizes titles itself
                OverwriteFiles = true,
            };
            this.baseOptions = baseOptions;         // e.g. cookies (see §9)
        }

        public Task<RunResult<VideoData>> FetchMetadataAsync(string url, CancellationToken ct)
            => ytdl.RunVideoDataFetch(url, ct, overrideOptions: baseOptions);

        public Task<RunResult<string>> DownloadAsync(
            string url, string format, IProgress<DownloadProgress> progress, CancellationToken ct)
            => ytdl.RunVideoDownload(
                   url,
                   format: format,
                   mergeFormat: DownloadMergeFormat.Mp4,   // merge to mp4 container
                   ct: ct,
                   progress: progress,
                   overrideOptions: baseOptions);

        // yt-dlp updates itself in place, downloading only when a newer build
        // exists on its channel — far cheaper than re-pulling the whole exe.
        // Needs the standalone release build in a writable dir (%LOCALAPPDATA%\Robin
        // qualifies). Stays on the *stable* channel; for a hot YouTube break pass
        // --update-to nightly@latest via baseOptions.AddCustomOption (§7).
        public Task<string> UpdateAsync() => ytdl.RunUpdate();
    }
}
```

Verified against YoutubeDLSharp 1.2.0 source — signatures:
`RunVideoDataFetch(string url, CancellationToken ct = default, bool flat = true, bool fetchComments = false, OptionSet overrideOptions = null)` → `RunResult<VideoData>`;
`RunVideoDownload(string url, string format = "bestvideo+bestaudio/best", DownloadMergeFormat mergeFormat = Unspecified, VideoRecodeFormat recodeFormat = None, CancellationToken ct = default, IProgress<DownloadProgress> progress = null, IProgress<string> output = null, OptionSet overrideOptions = null)` → `RunResult<string>` where `.Data` is the **final output file path**.

**Deno discovery (new — see §1.1/§7):** yt-dlp locates its JS runtime on `PATH`, or you point at it explicitly. YoutubeDLSharp 1.2.0 predates YouTube's mandatory-runtime change, so it has no typed option for this — the reliable approaches are: put `deno.exe` on `PATH` or in the **same folder as `yt-dlp.exe`** (the `YtDlpPathResolver` in §7 fetches both into `%LOCALAPPDATA%\Robin`, satisfying this), or pass it through as a raw option, e.g. `baseOptions.AddCustomOption<string>("--js-runtimes", "deno")` (or `deno:<path>`). Confirm the exact current flag against `yt-dlp --help` and whether your YoutubeDLSharp build ships `Utils.DownloadDeno()` when you code this.

### 4.3 `Robin/YtDlpVideoDownloader.cs` (new)

This mirrors `YouTubeExplodeVideoDownloader`'s structure and its exact `IDownloadUiNotifier` call sequence, so UI behavior is identical.

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;

namespace Robin
{
    internal class YtDlpVideoDownloader : YouTubeVideoDownloader
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        // H.264 mp4 video + m4a audio, mp4 fallbacks, then anything — matches Robin's
        // current "prefer mp4, fall back gracefully" stream selection.
        private const string Format =
            "bv*[ext=mp4][vcodec^=avc1]+ba[ext=m4a]/bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b";

        private const int ProgressThrottleMs = 100;

        private readonly IYtDlpClientAdapter client;
        private readonly string baseFilePath;

        public YtDlpVideoDownloader(string baseFilePath)
            : this(baseFilePath, new YtDlpClientAdapter(
                       YtDlpPathResolver.Default().Resolve(),   // §7
                       RobinUtils.GetPathToFFMPEG(),
                       baseFilePath))
        {
        }

        // Test-visible constructor ([InternalsVisibleTo("Robin.Tests")] grants access).
        internal YtDlpVideoDownloader(string baseFilePath, IYtDlpClientAdapter client)
        {
            this.baseFilePath = baseFilePath;
            this.client = client;
        }

        public async ValueTask<string> GetVideoTitle(string url)
        {
            var res = await client.FetchMetadataAsync(url, default).ConfigureAwait(false);
            if (!res.Success || res.Data == null)
                throw new InvalidOperationException(
                    $"yt-dlp could not read video info for {url}: {string.Join("; ", res.ErrorOutput)}");
            return res.Data.Title;
        }

        public async Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state)
        {
            notifier.SetCursorLoading();
            try
            {
                var meta = await client.FetchMetadataAsync(url, state.CancellationToken).ConfigureAwait(false);
                if (!meta.Success || meta.Data == null)
                    throw new InvalidOperationException(
                        $"yt-dlp metadata fetch failed: {string.Join("; ", meta.ErrorOutput)}");

                VideoData info = meta.Data;
                state.VideoId = info.ID;
                state.VideoUrl = url;
                state.VideoTitle = MakeValidVideoTitle(info.Title);
                state.FileExtension = "mp4";
                state.VideoResolution = info.Resolution ?? "UNKNOWN_RESOLUTION";
                state.Bitrate = "UNKNOWN_BITRATE";
                state.SizeInMegabytes = EstimateSizeMb(info);   // see §5
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
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.State != DownloadState_Downloading(p)) { /* see note below */ }

                int now = Environment.TickCount;
                bool isFinal = p.Progress >= 1.0;
                if (!isFinal && now - state.LastProgressTickMs < ProgressThrottleMs) return;
                state.LastProgressTickMs = now;

                int value = (int)(p.Progress * state.SizeInMegabytes);
                notifier.ReportDownloadProgress(state, value);
            });

            RunResult<string> res =
                await client.DownloadAsync(url, Format, progress, state.CancellationToken)
                            .ConfigureAwait(false);

            if (!res.Success)
                throw new InvalidOperationException(
                    $"yt-dlp download failed: {string.Join("; ", res.ErrorOutput)}");

            // yt-dlp reports the real output path; prefer it over our predicted path.
            if (!string.IsNullOrEmpty(res.Data) && File.Exists(res.Data))
                state.FilePath = res.Data;

            notifier.NotifyDownloadFinished(state);
        }

        // Reuse Robin's existing title sanitizer (currently internal static on the
        // YoutubeExplode engine — recommend extracting to a shared helper, §10).
        internal static string MakeValidVideoTitle(string raw)
            => string.Concat(raw.Split(Path.GetInvalidFileNameChars())).Trim();

        private static double EstimateSizeMb(VideoData info) { /* §5 */ return 0; }

        private void CleanupPartialFile(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { logger.Error($"Failed to delete partial file {path}: {ex.Message}"); }
        }
    }
}
```

> The `DownloadState_Downloading(p)` line above is a placeholder to flag the progress-state handling discussed in §5 — in real code you branch on `p.State` (the `DownloadState` enum: `PreProcessing`/`Downloading`/`PostProcessing`/`Success`/`Error`). Remove the placeholder; it's there to mark the decision point.

### 4.4 Wiring — diff to `RobinForm.cs`

Minimal (straight swap):

```diff
- YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);
+ YouTubeVideoDownloader videoDownloader = new YtDlpVideoDownloader(baseFilePath);
```

Better (config toggle so you can A/B without recompiling — reads an app setting, defaults to yt-dlp):

```diff
- YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);
+ YouTubeVideoDownloader videoDownloader = DownloaderFactory.Create(baseFilePath);
```

```csharp
// Robin/DownloaderFactory.cs (new)
using System.Configuration;

namespace Robin
{
    internal static class DownloaderFactory
    {
        public static YouTubeVideoDownloader Create(string baseFilePath)
        {
            string engine = ConfigurationManager.AppSettings["DownloadEngine"] ?? "ytdlp";
            return engine.Equals("explode", System.StringComparison.OrdinalIgnoreCase)
                ? (YouTubeVideoDownloader)new YouTubeExplodeVideoDownloader(baseFilePath)
                : new YtDlpVideoDownloader(baseFilePath);
        }
    }
}
```

---

## 5. The one real impedance mismatch: progress & size

Robin's progress bar is **megabyte-scaled**: `AddVideoToDownloadsList(state, (int)SizeInMegabytes)` sets the bar's max, and `ReportDownloadProgress(state, (int)(fraction * SizeInMegabytes))` sets its value. yt-dlp's model differs in two ways:

1. **Fraction, not bytes.** `DownloadProgress.Progress` is a `float` in `[0,1]`. Mapping is `value = (int)(p.Progress * SizeInMegabytes)` — fine *if* we know the size.
2. **Two passes.** yt-dlp downloads the video-only stream to 100%, then the audio-only stream 0→100%, then merges (`PostProcessing`). So `Progress` sweeps 0→1 **twice**. `DownloadProgress` fields (verified in 1.2.0): `State` (`None/PreProcessing/Downloading/PostProcessing/Error/Success`), `Progress` (float), `TotalDownloadSize` (string, e.g. `"48.20MiB"`), `DownloadSpeed` (string), `ETA` (string), `VideoIndex` (int), `Data` (string).

**Getting the size** (`EstimateSizeMb`): three options, pick one —
- **(a) From metadata up front (preferred):** `RunVideoDataFetch` returns `VideoData.Formats` (a `FormatData[]`); sum the `filesize`/`filesize_approx` of the best mp4 video format + best m4a audio format. *Confirm the exact `FormatData` property names against the package* (they're `Filesize`/`FilesizeApprox`/`Height`/`Extension` in recent versions) — this is the one spot to verify by hand.
- **(b) From the first progress tick:** parse `DownloadProgress.TotalDownloadSize` (`"48.20MiB"` → 48.2 MB) and use it. Simple and robust, but the list item is already added with an initial size, so you'd show an estimate first.
- **(c) Percentage bar:** set `SizeInMegabytes = 100` for the bar math and show the real approx size only in the info text. Simplest; the bar becomes a percent bar.

**Handling the two-pass sweep:** simplest acceptable behavior is to let the bar fill, reset, and fill again (note it in a tooltip). Cleaner: treat the video pass as 0–90% and the merge/audio as 90–100% by branching on `p.State`/`VideoIndex`. Cleanest of all: use the **direct-CliWrap** path (§6) with yt-dlp's `--progress-template` to emit a single normalized `downloaded/total` you control.

A small parser for option (b):
```csharp
private static double ParseYtDlpSizeMb(string s)   // "48.20MiB", "1.10GiB"
{
    if (string.IsNullOrWhiteSpace(s)) return 0;
    s = s.Trim();
    double mult = s.EndsWith("GiB") ? 1024 : s.EndsWith("KiB") ? 1.0/1024 : 1; // MiB→1
    var num = new string(System.Linq.Enumerable.ToArray(
        System.Linq.Enumerable.TakeWhile(s, c => char.IsDigit(c) || c == '.')));
    return double.TryParse(num, out var v) ? v * mult : 0;
}
```

---

## 6. Option B — direct yt-dlp via CliWrap (no new NuGet)

Robin already references **CliWrap 3.10.5** (transitively, via YoutubeExplode.Converter). You can drive yt-dlp yourself and skip the YoutubeDLSharp dependency entirely. Trade-off: you own argument construction and stdout parsing, but you get **full control of progress** and one fewer dependency.

```csharp
using CliWrap;
using CliWrap.EventStream;

// Emit a clean, machine-parseable progress line we fully control:
//   ROBIN 0.4213
var args = new[]
{
    url,
    "-f", Format,
    "--merge-output-format", "mp4",
    "--ffmpeg-location", ffmpegPath,
    "-o", Path.Combine(baseFilePath, "%(title)s.%(ext)s"),
    "--no-playlist",
    "--newline",
    "--progress-template", "download:ROBIN %(progress._percent_str)s %(progress.downloaded_bytes)s/%(progress.total_bytes)s",
    // "--cookies-from-browser", "firefox",   // §9
};

var cmd = Cli.Wrap(ytDlpPath).WithArguments(args).WithValidation(CommandResultValidation.None);

await foreach (var ev in cmd.ListenAsync(state.CancellationToken))
{
    switch (ev)
    {
        case StandardOutputCommandEvent stdout when stdout.Text.StartsWith("ROBIN "):
            // parse "ROBIN 42.1% 12345/29876" → fraction, map to ReportDownloadProgress
            break;
        case ExitedCommandEvent exit when exit.ExitCode != 0:
            throw new InvalidOperationException($"yt-dlp exited {exit.ExitCode}");
    }
}
```

Metadata/title without downloading: `yt-dlp --print "%(title)s" --no-playlist URL` (or `-J` for full JSON, parsed with the Newtonsoft.Json you already ship). This mirrors what `GetVideoTitle` needs.

**Recommendation:** start with **Option A (YoutubeDLSharp)** — it's less code and the typed `VideoData`/`DownloadProgress` are convenient. Drop to Option B only if the two-pass progress bar bothers you enough to want `--progress-template`, or you want to avoid adding a package.

---

## 7. Shipping & updating the yt-dlp binary

Robin now provisions **three** external binaries. FFmpeg is already resolved by `RobinUtils.GetPathToFFMPEG()`; the two new ones are yt-dlp and its JS runtime:

| Binary | Size | Source | YoutubeDLSharp helper | Role |
|---|---|---|---|---|
| `yt-dlp.exe` | ~17.6 MB | PyInstaller bundle of Python | `Utils.DownloadYtDlp(dir)` | the downloader |
| `deno.exe` | ~40 MB | denoland/deno release (zip) | `Utils.DownloadDeno(dir)` | JS runtime for nsig now / PO tokens later (§1.1) |
| `ffmpeg.exe` | (varies) | already resolved externally | — (`RobinUtils.GetPathToFFMPEG()`) | muxing video+audio |

`Utils.DownloadDeno()` is marked *optional* in YoutubeDLSharp's README because that wrapper targets many sites; for Robin — **YouTube-only — it's effectively required.** Without a JS runtime, YouTube extraction is deprecated and formats go missing (the *"Signature/nsig extraction failed"* symptom, §1.1).

Two ways to provide yt-dlp and Deno:

**Bundle it** in the ClickOnce payload — simplest to reason about, but (a) bloats every ClickOnce update, (b) redistributes GPLv3+ (§ below), and (c) goes stale fast.

**Fetch at runtime (recommended)** — mirror the existing `FFmpegPathResolver` design with a `YtDlpPathResolver` that caches under `%LOCALAPPDATA%\Robin\` and downloads if missing. YoutubeDLSharp ships a helper:

```csharp
// Robin/YtDlpPathResolver.cs (new) — sketch, mirrors FFmpegPathResolver's cache pattern.
// Fetches BOTH yt-dlp.exe and deno.exe into the same cache dir so yt-dlp discovers
// the runtime automatically (it checks its own folder / PATH).
internal sealed class YtDlpPathResolver
{
    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Robin");

    public static YtDlpPathResolver Default() => new YtDlpPathResolver();

    // yt-dlp.exe — fetched if missing.
    public string Resolve()
    {
        string exe = Path.Combine(CacheDir, "yt-dlp.exe");
        if (!File.Exists(exe))
        {
            Directory.CreateDirectory(CacheDir);
            // Downloads the latest yt-dlp.exe into CacheDir (returns the path).
            YoutubeDLSharp.Utils.DownloadYtDlp(CacheDir).GetAwaiter().GetResult();
        }
        return exe;
    }

    // deno.exe — the JS runtime (§1.1). Fetched into the SAME folder as yt-dlp.exe
    // so yt-dlp finds it without extra flags; otherwise add CacheDir to the child
    // process PATH, or pass --js-runtimes deno:<this path>.
    public string ResolveDeno()
    {
        string exe = Path.Combine(CacheDir, "deno.exe");
        if (!File.Exists(exe))
        {
            Directory.CreateDirectory(CacheDir);
            YoutubeDLSharp.Utils.DownloadDeno(CacheDir).GetAwaiter().GetResult();
        }
        return exe;
    }

    // --- Update throttle ------------------------------------------------
    // yt-dlp breaks often, so self-update regularly — but not on every launch.
    // A stamp file records the last check; ShouldUpdate() gates it to once/day.
    private string StampPath => Path.Combine(CacheDir, ".ytdlp-update-stamp");

    public bool ShouldUpdate(TimeSpan? maxAge = null)
    {
        TimeSpan window = maxAge ?? TimeSpan.FromHours(24);
        try
        {
            return !File.Exists(StampPath)
                || DateTime.UtcNow - File.GetLastWriteTimeUtc(StampPath) > window;
        }
        catch { return true; }   // if we can't tell, err toward updating
    }

    public void MarkUpdated()
    {
        try { Directory.CreateDirectory(CacheDir); File.WriteAllText(StampPath, ""); }
        catch { /* best-effort: a failed stamp just means we re-check next launch */ }
    }
}
```

Call `ResolveDeno()` once at startup (or lazily before the first download) so the runtime is present; the default `YtDlpVideoDownloader` constructor in §4.3 should trigger both resolves. If you'd rather not rely on same-folder discovery, prepend `CacheDir` to the yt-dlp child process's `PATH`, or thread `--js-runtimes deno:<path>` into `baseOptions` (§4.2).

**Keep it fresh** — YouTube breaks yt-dlp regularly, so self-update is the single most important operational habit for keeping downloads working (it's *why* runtime-fetch beats bundling). Drive it through the adapter's `UpdateAsync()` (wraps `yt-dlp -U`, which downloads only when a newer build exists — far cheaper than re-pulling the whole exe via `Utils.DownloadYtDlp`), throttled by the resolver stamp so it runs about once a day. Wire it into a **one-time startup path** (e.g. `RobinForm_Load`), never the per-download hot path:

```csharp
// Robin/YtDlpBootstrap.cs (new) — await once at startup, e.g. from RobinForm_Load.
internal static class YtDlpBootstrap
{
    private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

    public static async Task EnsureReadyAsync()
    {
        var resolver = YtDlpPathResolver.Default();
        string ytDlp = resolver.Resolve();     // fetch yt-dlp.exe if missing
        resolver.ResolveDeno();                  // fetch deno.exe if missing (§1.1)

        if (!resolver.ShouldUpdate()) return;    // stamp says we checked recently
        try
        {
            // ffmpeg/output are irrelevant to -U, so pass throwaway values.
            var updater = new YtDlpClientAdapter(ytDlp, ffmpegPath: "", outputFolder: ".");
            string status = await updater.UpdateAsync().ConfigureAwait(false);   // yt-dlp -U
            logger.Info($"yt-dlp self-update: {status}");
        }
        catch (Exception e)
        {
            // Never block startup on a failed update — the cached binary still works.
            logger.Warn(e, "yt-dlp self-update failed; continuing on the cached binary");
        }
        finally
        {
            resolver.MarkUpdated();   // stamp even on failure: retry tomorrow, not every launch
        }
    }
}
```

Because `UpdateAsync()` sits behind `IYtDlpClientAdapter`, tests inject a fake that no-ops it, so the bootstrap stays unit-testable. **Deno** changes far less often than yt-dlp, so its one-time fetch is enough — re-pull it only if a yt-dlp release bumps its minimum runtime (rare). The churn is yt-dlp itself; when even the newest *stable* build is still broken for a hot YouTube change, `--update-to nightly@latest` (via `baseOptions.AddCustomOption`) is the escape hatch.

**Licensing (do read this):** yt-dlp's **source** is Unlicense (public domain), but the **built `yt-dlp.exe` is GPLv3+** because PyInstaller bundles GPL components. Robin launches it as a **separate process** (arm's-length), which is "mere aggregation," so Robin's own source is not forced to GPL. Still: ship yt-dlp's license/copyright notice alongside it, and note it in Robin's about/readme. Fetching at runtime (not redistributing the exe in your installer) is the cleanest posture. FFmpeg (already used) is similarly LGPL/GPL depending on build — you already resolve it externally, same reasoning. YoutubeDLSharp itself is **BSD-3-Clause** (safe to reference/redistribute). **Deno** is **MIT** (permissive) — no copyleft concern; fetching it at runtime keeps it out of your installer regardless.

---

## 8. Migration strategy: replace vs. hybrid

**(1) Straight replacement (recommended first step).** Make `YtDlpVideoDownloader` the engine at `RobinForm.cs:20` (via the factory in §4.4). Simplest mental model, most robust, and the interface makes it a one-line swap. Keep `YouTubeExplodeVideoDownloader` in the tree behind the `explode` toggle for fallback/testing.

**(2) Hybrid fast-path.** Try YoutubeExplode first (fast, in-process, no subprocess); on `VideoUnavailableException`/manifest failure, fall back to yt-dlp. Best UX when Explode works, but adds a branch and two code paths to maintain. Implement as a decorator:

```csharp
internal sealed class FallbackVideoDownloader : YouTubeVideoDownloader
{
    private readonly YouTubeVideoDownloader primary, fallback;
    // GetVideoTitle/DownloadVideo: try primary; on known "unavailable" errors, retry with fallback.
}
```

Given YoutubeExplode is currently bot-walled for you and yt-dlp is strictly more capable, I'd ship **(1)** now and only add **(2)** if subprocess latency on working videos proves annoying.

---

## 9. Cookies are optional — anonymous by default (ties to the companion doc)

**You don't need cookies to start.** yt-dlp downloads public videos **anonymously** (§1.1), so ship Robin with **no credentials** — most videos just work. That's a sharp contrast with the companion doc, where cookies were the *only* lever and a hard one to pull. Cookies become a **fallback** for two situations: a **hard per-IP bot-flag** (YouTube rejecting every anonymous request from your IP), or **restricted content** (age-restricted / members-only / private). When you do need them, the companion doc's hardest problem — extracting Chrome cookies past App-Bound Encryption — becomes **yt-dlp's job, not Robin's**. Pass one option:

```csharp
// YoutubeDLSharp OptionSet — either the typed property…
var opts = new OptionSet { CookiesFromBrowser = "firefox" };   // or "chrome", "edge", …
// …or the guaranteed-correct raw flag (source of truth is the yt-dlp CLI):
opts.AddCustomOption<string>("--cookies-from-browser", "firefox");
// Or a cookies.txt exported per the companion doc:
var opts2 = new OptionSet { Cookies = @"C:\path\to\cookies.txt" };
```

Wire it into `YtDlpClientAdapter`'s `baseOptions`. Notes: yt-dlp's `--cookies-from-browser` still hits Chrome's App-Bound Encryption wall on current Chrome (per the companion doc), so **Firefox or a `cookies.txt` remain the reliable inputs** — but now that's a supported yt-dlp code path you configure, not decryption code you write. yt-dlp also supports **PO tokens** and an **OAuth plugin** if YouTube escalates further. Use a **burner account** (companion doc §4).

**Honest caveat:** in the most heavily-flagged cases (datacenter/VPN IPs especially), YouTube can reject requests even *with* valid cookies **and** PO tokens — so cookies improve reliability but are not a guaranteed universal fix (§1.1). On a normal residential IP, anonymous or cookie-assisted yt-dlp is the most reliable option available today. **Recommended posture:** ship **cookieless**, expose an optional *"use my browser cookies"* toggle that maps to `--cookies-from-browser`, and reach for it only when a specific video fails anonymously.

---

## 10. Testing

Mirror the existing seam. Add `Robin.Tests/Fakes/FakeYtDlpClientAdapter.cs`:

```csharp
using System;
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
        public float[] ProgressSequence { get; set; } = new float[0];
        public System.Collections.Generic.List<string> DownloadCalls { get; } = new();

        public Task<RunResult<VideoData>> FetchMetadataAsync(string url, CancellationToken ct)
            => Task.FromResult(new RunResult<VideoData>(true, new string[0], Video));

        public Task<RunResult<string>> DownloadAsync(
            string url, string format, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            DownloadCalls.Add(url);
            foreach (var p in ProgressSequence)
                progress?.Report(new DownloadProgress(DownloadState.Downloading, progress: p));
            return Task.FromResult(new RunResult<string>(DownloadSucceeds, new string[0], OutputPath));
        }

        public int UpdateCalls { get; private set; }
        public Task<string> UpdateAsync()   // no real process in tests
        {
            UpdateCalls++;
            return Task.FromResult("yt-dlp is up to date (fake)");
        }
    }
}
```

Then the existing orchestration tests (title fetch, progress reporting, cancellation, failure → `Failed` status) port directly, driving `YtDlpVideoDownloader` through a fake `IDownloadUiNotifier`. *Confirm the `RunResult<T>` and `DownloadProgress` constructor shapes against 1.2.0 when you write these* — the fields are known (`Success`, `ErrorOutput`, `Data`), but the public constructors may differ slightly by version.

Reminder (CLAUDE.md): new production `.cs` files must be added to `Robin.csproj` by hand; keep the YoutubeExplode/YoutubeDLSharp package versions matched between `Robin` and `Robin.Tests`.

---

## 11. Risks & things to confirm before building

- **Progress UX** (§5): the two-pass sweep and size estimate are the only non-mechanical parts. Decide (a)/(b)/(c) up front.
- **Exact package API names**: `FormatData` filesize/height properties, `OptionSet.CookiesFromBrowser`, and the `RunResult`/`DownloadProgress` constructors — all verified to *exist* in concept from the 1.2.0 source, but pin the exact member names when coding (or use `AddCustomOption` + `--print`, which don't depend on generated names).
- **First-run download**: fetching yt-dlp (~17 MB) **plus Deno (~40 MB) ≈ 57 MB total** needs network + a progress/error path; ClickOnce apps run per-user so `%LOCALAPPDATA%\Robin` is writable.
- **Deno provisioning & discovery** (§1.1/§7): the second ~40 MB fetch, and yt-dlp must be able to *find* the runtime (same folder as `yt-dlp.exe`, on `PATH`, or via `--js-runtimes`). Without a JS runtime, YouTube extraction is deprecated — you'll see *"Signature/nsig extraction failed"* and missing formats. Confirm the exact `--js-runtimes` flag when you build. **YoutubeDLSharp 1.2.0 (Jan 5 2026) does ship `Utils.DownloadDeno()`** plus a `Utils.DownloadBinaries(bool skipExisting = true, string directoryPath = "", bool downloadJSRuntime = true)` convenience that fetches yt-dlp + Deno together — verified against the `v.1.2.0` tag source — so no newer release or manual Deno fetch is required; denoland/deno releases are only a fallback if you want to pin a specific Deno build.
- **Subprocess & AV**: spawning `yt-dlp.exe` can trip aggressive antivirus/SmartScreen on first run; bundling-then-fetching-updates can mitigate the very first launch.
- **Cancellation**: `CancellationToken` propagates to YoutubeDLSharp, which kills the yt-dlp process; verify partial `.part`/`.ytdl` files get cleaned (yt-dlp usually resumes/cleans, but Robin's `CleanupPartialFile` targets the final path — you may want to also sweep `*.part`).
- **License notice**: include yt-dlp's (GPLv3+) and FFmpeg's notices with the app.

---

## 12. References (URLs visited during this research)

**YoutubeDLSharp — read directly (source of the API details and code samples):**
- README — https://raw.githubusercontent.com/Bluegrams/YoutubeDLSharp/master/README.md
- `YoutubeDL.cs` (method signatures) — https://raw.githubusercontent.com/Bluegrams/YoutubeDLSharp/master/YoutubeDLSharp/YoutubeDL.cs
- `YoutubeDLSharp.csproj` (target frameworks: `netstandard2.0;net45;net6.0;net8.0`; Newtonsoft.Json 13.0.3) — https://raw.githubusercontent.com/Bluegrams/YoutubeDLSharp/master/YoutubeDLSharp/YoutubeDLSharp.csproj
- `DownloadProgress.cs` (progress fields + `DownloadState` enum) — https://raw.githubusercontent.com/Bluegrams/YoutubeDLSharp/master/YoutubeDLSharp/DownloadProgress.cs
- `VideoData.cs` (metadata fields) — https://raw.githubusercontent.com/Bluegrams/YoutubeDLSharp/master/YoutubeDLSharp/Metadata/VideoData.cs
- GitHub repo — https://github.com/Bluegrams/YoutubeDLSharp
- NuGet (v1.2.0) — https://www.nuget.org/packages/YoutubeDLSharp
- Libraries.io (license: BSD-3-Clause; versions) — https://libraries.io/nuget/YoutubeDLSharp

**yt-dlp — engine, binary, licensing:**
- Repo (features, options) — https://github.com/yt-dlp/yt-dlp
- Usage & options — https://github.com/yt-dlp/yt-dlp#usage-and-options
- Releases (current 2026.08.19) — https://github.com/yt-dlp/yt-dlp/releases
- VideoHelp (binary size / version) — https://www.videohelp.com/software/yt-dlp
- Softpedia (yt-dlp overview) — https://www.softpedia.com/get/Internet/Download-Managers/yt-dlp.shtml
- Chocolatey package — https://community.chocolatey.org/packages/yt-dlp

**yt-dlp JS runtime / PO tokens (the Deno requirement — added 2026-08-24):**
- [Announcement] Upcoming new requirements for YouTube downloads (Deno default; nsig now, PO tokens future) — https://github.com/yt-dlp/yt-dlp/issues/14404
- Signature/nsig extraction failed: Some formats may be missing (the no-runtime symptom) — https://github.com/yt-dlp/yt-dlp/issues/14707
- yt-dlp unable to find JS runtime — https://github.com/yt-dlp/yt-dlp/issues/15197
- DeepWiki — JavaScript Challenge Solving in yt-dlp — https://deepwiki.com/yt-dlp/yt-dlp/3.4.2-javascript-challenge-solving
- bashonly/yt-dlp-YTNSigDeno (obsolete Deno nsig plugin, folded into core) — https://github.com/bashonly/yt-dlp-YTNSigDeno
- bgutil-ytdlp-pot-provider (automatic PO-token generation) — https://pypi.org/project/bgutil-ytdlp-pot-provider
- yt-dlp Wiki FAQ (cookies optional for public content) — https://github.com/yt-dlp/yt-dlp/wiki/FAQ
- yt-dlp.net — PO Token required — https://yt-dlp.net/errors/po-token-required
- Deno (MIT; runtime binary) — https://github.com/denoland/deno

**Cookies-from-browser (cross-reference to companion doc):**
- ytdlp.org — Using cookies with yt-dlp — https://ytdlp.org/guides/yt-dlp-cookies
- yt-dlp cookies-from-browser on Windows 11 — https://trajdash.usc.edu/ytdlp-cookiesfrombrowser-windows11
- Using yt-dlp cookies from a browser — https://suther.land/how-to-use-yt-dlp-with-cookies-from-arc-browser/

**Landscape / alternatives (surfaced in search):**
- YoutubeExplode alternatives — https://alternativeto.net/software/youtubeexplode/
- yt-dlp alternatives (2026) — https://vidkraken.com/blog/yt-dlp-alternatives
- Downloading with YoutubeExplode (C#) — https://www.luisllamas.es/en/csharp-youtube-explode/
- Open-source C# video downloaders — https://sourceforge.net/directory/video-downloaders/c-sharp/

*(Cookie-extraction, Chrome App-Bound Encryption, PO-token, and ban-risk URLs are listed in the companion doc `research/youtube-cookie-authentication-research.md`.)*
