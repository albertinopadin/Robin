# Robin — YouTube Cookie / Login Authentication Research

**Date:** 2026-08-24
**Author:** Claude (research only — no code in Robin was changed)
**Trigger:** `VideoUnavailableException: Video 'bYXEhi9No-Y' is not available.` thrown from `StreamClient.GetManifestAsync` while video **titles** (`GetVideoAsync`) still work.
**Robin's stack at time of writing:** YoutubeExplode **6.6.1** + YoutubeExplode.Converter 6.6.1, .NET Framework 4.8, WinForms.

> This document answers three questions you asked:
> 1. How do you give YoutubeExplode YouTube account cookies/credentials? (with code)
> 2. How do you actually *get* those cookies (from Chrome, or from inside Robin in C#)?
> 3. Does YouTube ban accounts used for downloading?
>
> It ends with a **bottom-line recommendation** and a **Sources** section listing every URL visited.

---

## 0. TL;DR / Bottom line up front

- **What's happening:** This is not a Robin bug. Since ~2026-08-22 YouTube's anti-bot wall rejects Robin's *anonymous* InnerTube `/player` requests from your IP ("Sign in to confirm you're not a bot" → surfaces as "not available" because the bot-wall response carries no `videoDetails`). Metadata still works because it comes from the watch page, which isn't gated. This matches YoutubeExplode issue [#794](https://github.com/Tyrrrz/YoutubeExplode/issues/794) and the newer Aug-2026 issues [#962](https://github.com/Tyrrrz/YoutubeExplode/issues/962)/[#964](https://github.com/Tyrrrz/YoutubeExplode/issues/964).

- **Can cookies fix it?** *Probably, but with real caveats and no guarantee it lasts.* Authentication is the maintainer's documented remedy, and YoutubeExplode has first-class support for it via `new YoutubeClient(IReadOnlyList<Cookie>)`. But (a) the community consensus on the exact bot-wall issue is that cookies "probably won't work for long if they even work at all," and (b) there is a documented case where valid cookies still failed ([#781](https://github.com/Tyrrrz/YoutubeExplode/issues/781)). Treat cookie auth as *likely-helpful, not certain*.

- **The hard part is getting the cookies, not giving them to YoutubeExplode.** Passing cookies is ~5 lines. *Extracting* Chrome's cookies from C# is now **blocked by Chrome's App-Bound Encryption (v20)** introduced in Chrome 127 (July 2024). The clean, robust way for a desktop app is to **embed a WebView2 login inside Robin** and read cookies from the WebView2 cookie manager — this sidesteps Chrome's on-disk encryption entirely.

- **Ban risk is real but manageable.** yt-dlp's own wiki says: *"By using your account with yt-dlp, you run the risk of it being banned (temporarily or permanently)."* The standard mitigation is **use a dedicated throwaway Google account, never your main one.** Personal/residential use is far lower risk than server/datacenter use.

- **Cheapest things to try before building anything:** drop any VPN / get a fresh IP, wait (per-IP flags decay, often in under an hour per the maintainer), and avoid parallel bursts. If the flag has already lifted, don't over-engineer.

- **If you want a durable fix and are willing to change engines:** the ecosystem's most battle-tested anti-bot handling lives in **yt-dlp**, usable from .NET via **[YoutubeDLSharp](https://github.com/Bluegrams/YoutubeDLSharp)**. Several people on issue #794 migrated to it precisely because it keeps up with YouTube's changes (cookies-from-browser, PO-token providers, OAuth plugin). This is a bigger change than cookie plumbing but is the most robust long-term path. See §5.

---

## 1. Why this is happening (verified against Robin)

Robin calls, in order (see `YouTubeExplodeVideoDownloader.cs`):

1. `client.GetVideoAsync(url)` → title/metadata (watch page). **Still works.**
2. `client.GetManifestAsync(url)` → InnerTube `/youtubei/v1/player` request for the stream list. **This is what's failing.**

YouTube now answers the anonymous `/player` request from flagged IPs with a `playabilityStatus` of `LOGIN_REQUIRED` ("Sign in to confirm you're not a bot") and **no** `videoDetails`. YoutubeExplode interprets a response with no video details as "video not available," which is why the log says `VideoUnavailableException` rather than "sign in."

**Important:** YoutubeExplode already ships the community mitigations. PR [#865](https://github.com/Tyrrrz/YoutubeExplode/pull/865) ("add random visitor data to requests," a port of NewPipe's fix) was **merged and closed #794**, and it's in your 6.6.1. The fact that Robin is *still* walled means that mitigation is no longer sufficient for your IP — which is exactly why authentication (cookies) is the next lever. Note also there is **no newer released fix**: 6.6.1 (published 2026-08-07) is the latest release; the in-progress PR [#965](https://github.com/Tyrrrz/YoutubeExplode/pull/965) targets *age-restricted / "for kids"* videos (a related but separate `playabilityStatus` case) and is not merged or released yet.

---

## 2. Q1 — Giving cookies (or credentials) to YoutubeExplode

### 2.1 There is no username/password login — it is cookies only

YoutubeExplode has **no** email/password login API, and you should not try to build one (Google's login flow has 2FA, device checks, and explicit anti-automation; scripting it gets accounts flagged fast). YoutubeExplode authenticates purely by **cookies from an already-logged-in session**.

### 2.2 The constructor overloads (verified from 6.6.1 source)

From `YoutubeClient.cs` at tag 6.6.1, there are four public constructors:

```csharp
public YoutubeClient(HttpClient http, IReadOnlyList<Cookie> initialCookies) // primary
public YoutubeClient(HttpClient http)                                       // -> empty cookie list
public YoutubeClient(IReadOnlyList<Cookie> initialCookies)                  // -> default Http.Client
public YoutubeClient()                                                      // -> default, no cookies
```

The cookie type is **`System.Net.Cookie`**. Internally the cookies are handed to YoutubeExplode's `YoutubeHttpHandler`, which (a) sends them on every request and (b) computes the `Authorization: SAPISIDHASH …` header that authenticated InnerTube requests require.

### 2.3 The maintainer's canonical example (from the README "Authentication" section)

```csharp
using YoutubeExplode;

// Perform authentication and extract cookies
var cookies = ...;   // IReadOnlyList<System.Net.Cookie>

using var youtube = new YoutubeClient(cookies);
```

The README explicitly suggests obtaining the cookies via **an embedded browser such as WebView**: *"In order to actually perform the authentication, you can use an embedded browser such as WebView to navigate the user to the YouTube login page, let them log in, and then extract the cookies from the browser."* (This is the WebView2 approach in §3.1.)

### 2.4 Which cookies actually matter

Per the maintainer in discussion [#766](https://github.com/Tyrrrz/YoutubeExplode/discussions/766): *"I know for a fact that `APISID`/`SAPISID` are required."* Those two feed the `SAPISIDHASH` auth header (SHA-1 over `timestamp + " " + SAPISID + " " + origin`). The secure variant `__Secure-3PAPISID` is used when present.

To be safe, copy the **whole authenticated cookie set** for `.youtube.com` (order doesn't matter):

| Cookie | Role |
|---|---|
| `SAPISID`, `APISID` | **Required** — used to build the `SAPISIDHASH` Authorization header |
| `__Secure-1PAPISID`, `__Secure-3PAPISID` | Secure variants of the above |
| `SID`, `HSID`, `SSID` | Core session identity |
| `__Secure-1PSID`, `__Secure-3PSID` | Secure session identity (the "3P" variant matters for the bot wall) |
| `LOGIN_INFO` | Marks the request as a logged-in session |
| `PREF`, `VISITOR_INFO1_LIVE`, `YSC` | Visitor/preference cookies YouTube expects to see |

### 2.5 Building the cookie list in C#

```csharp
using System.Net;
using System.Collections.Generic;
using YoutubeExplode;

var cookies = new List<Cookie>
{
    new Cookie("SAPISID",            "<value>", "/", ".youtube.com"),
    new Cookie("APISID",             "<value>", "/", ".youtube.com"),
    new Cookie("__Secure-3PAPISID",  "<value>", "/", ".youtube.com"),
    new Cookie("__Secure-3PSID",     "<value>", "/", ".youtube.com"),
    new Cookie("SID",                "<value>", "/", ".youtube.com"),
    new Cookie("HSID",               "<value>", "/", ".youtube.com"),
    new Cookie("SSID",               "<value>", "/", ".youtube.com"),
    new Cookie("LOGIN_INFO",         "<value>", "/", ".youtube.com"),
    // ...plus PREF, VISITOR_INFO1_LIVE, YSC, __Secure-1PSID, __Secure-1PAPISID
};

using var youtube = new YoutubeClient(cookies);
```

### 2.6 How this slots into Robin specifically

Robin already isolates the client behind a seam (`YoutubeClientAdapter`), so the change is small and localized. Today:

```csharp
// YoutubeClientAdapter.cs (current)
public YoutubeClientAdapter(HttpClient httpClient)
{
    youtube = new YoutubeClient(httpClient);
}
```

Authenticated version:

```csharp
// YoutubeClientAdapter.cs (proposed)
public YoutubeClientAdapter(HttpClient httpClient, IReadOnlyList<Cookie> cookies)
{
    youtube = cookies != null && cookies.Count > 0
        ? new YoutubeClient(httpClient, cookies)
        : new YoutubeClient(httpClient);
}
```

Notes for Robin's exact setup:
- Robin's shared `HttpClient` is created with `UseCookies = false` (`YouTubeExplodeVideoDownloader.cs:34`). That's **fine and correct** — YoutubeExplode's own `YoutubeHttpHandler` manages the cookie header; you don't want `HttpClientHandler` maintaining a second, competing cookie container.
- The seam to inject cookies is the production constructor at `YouTubeExplodeVideoDownloader.cs:44-47`, which currently news up `new YoutubeClientAdapter(sharedHttpClient)`.
- The memory note `youtube-bot-wall-2026-08` already identifies `YoutubeClientAdapter` as the correct place to modify.

### 2.7 Honest caveats about cookie auth

- **Cookies are not a guaranteed fix for the bot wall.** On issue #794, when someone suggested auth cookies, the response (citing youtube-dl) was that *"cookies probably won't work for long if they even work at all"* for the pure IP-flag case. Authentication most reliably unlocks *account-gated* content (private, age-restricted, members-only); its effect on a blanket IP flag is less certain.
- **There is a known case where valid cookies still failed.** In [#781](https://github.com/Tyrrrz/YoutubeExplode/issues/781), a reporter extracted valid logged-in cookies via WebView2, passed them to `YoutubeClient`, and *still* got `401 Unauthorized` for a private video — suggesting edge cases in how YoutubeExplode applies cookies. So budget time to actually validate end-to-end, don't assume.
- **Cookies expire / rotate.** A logged-in YouTube session's cookies are refreshed by the browser over time. Cookies you snapshot will eventually go stale (days-to-weeks, sometimes sooner), so any implementation needs a re-auth path.
- **PO tokens.** For the plain `WEB` client, YouTube increasingly wants a "Proof-of-Origin" token that YoutubeExplode does not generate. If YouTube escalates to requiring a PO token for your account/IP, cookies alone won't be enough (this is the wall yt-dlp addresses with dedicated PO-token providers). YoutubeExplode's current answer to escalation is the random-visitor-data trick, not PO tokens.

---

## 3. Q2 — How to actually *get* the cookies

This is the genuinely hard part in 2026, so it gets the most space. The options, best-first for a desktop app like Robin:

### 3.1 Option A (recommended): Embedded WebView2 login inside Robin

Let the user log into YouTube in an in-app browser window, then read the cookies straight from the WebView2 cookie store. **This completely sidesteps Chrome's on-disk encryption** (§3.2) because you're reading a live, in-process cookie jar, not a file on disk. It's also the exact approach the YoutubeExplode README recommends.

**Mechanics (.NET Framework 4.8 WinForms):**
- Add the **`Microsoft.Web.WebView2`** NuGet package. The **WebView2 Runtime** ships with Windows 11, so end users generally don't need to install anything.
- Host a `WebView2` control in a small login form, navigate to `https://accounts.google.com/ServiceLogin?service=youtube` (or just `https://www.youtube.com` and let the user click "Sign in").
- Use a **persistent user-data folder** so the login survives between runs (`CoreWebView2Environment.CreateAsync(userDataFolder: ...)`).
- After login, pull cookies via `CoreWebView2.CookieManager.GetCookiesAsync(...)`.

**Code (adapted from the pattern in YoutubeExplode issue [#781](https://github.com/Tyrrrz/YoutubeExplode/issues/781)):**

```csharp
using System.Net;
using System.Linq;
using Microsoft.Web.WebView2.Core;
using YoutubeExplode;

// After the user has logged in inside the WebView2 control:
var wv2Cookies = await webView.CoreWebView2.CookieManager
    .GetCookiesAsync("https://www.youtube.com");

var netCookies = wv2Cookies
    .Select(c => new Cookie(c.Name, c.Value, c.Path, c.Domain))
    .ToList();

using var youtube = new YoutubeClient(netCookies);
```

(The issue-#781 sample used a `c.ToSystemNetCookie()` helper; the explicit `Select` above avoids depending on an unofficial extension. `CoreWebView2Cookie` exposes `Name`, `Value`, `Domain`, `Path`, `Expires`, `IsSecure`, `IsHttpOnly`.)

**Pros:** No file decryption, no admin rights, cookies are always current, and you get a clean "Sign in" UX. Works regardless of which browser the user normally uses.
**Cons / things to validate:**
- **WebView2 dependency** on a WinForms/.NET 4.8 app (new NuGet + a login form). Modest but real.
- **Google sometimes blocks logins in embedded webviews** ("this browser or app may not be secure"). This is a known, intermittent friction with embedded browsers. Mitigations people use: set a normal desktop Chrome user-agent on the WebView2, keep a persistent profile, and complete any 2FA in-window. Validate this early — it's the main risk of this approach. If Google hard-blocks the embedded login for your account, fall back to Option D (manual export).
- Issue #781 shows the extracted cookies still need to actually work in YoutubeExplode (see §2.7).

### 3.2 Option B: Read Chrome's cookie database directly in C# — *largely blocked in 2026*

This is the "classic" approach, and it's the first thing most people try. **On current Chrome it no longer works without malware-grade techniques.** Here's the full picture so you can see why.

**Where Chrome stores cookies (Windows):**
- Cookie DB (SQLite): `%LOCALAPPDATA%\Google\Chrome\User Data\<Profile>\Network\Cookies` (default profile is `Default`). Table `cookies`, columns include `host_key`, `name`, `encrypted_value`.
- Key material: `%LOCALAPPDATA%\Google\Chrome\User Data\Local State` (JSON).

**The legacy "v10" scheme (Chrome 80 – 126):**
1. Read `Local State` → `os_crypt.encrypted_key` (base64). Decode, strip the 5-byte `DPAPI` prefix.
2. `ProtectedData.Unprotect(keyBytes, null, DataProtectionScope.CurrentUser)` → 32-byte AES key.
3. Each `encrypted_value` begins with ASCII `v10`: `[3-byte "v10"][12-byte nonce][ciphertext][16-byte GCM tag]`. Decrypt with **AES-256-GCM** using the key from step 2.

```csharp
// Sketch — LEGACY v10 path only. Does NOT decrypt v20 cookies (Chrome 127+).
// Note: System.Security.Cryptography.AesGcm does NOT exist on .NET Framework 4.8.
// You'd need BouncyCastle, or P/Invoke BCrypt, to do AES-GCM here.
string localState = File.ReadAllText(localStatePath);
var encKeyB64 = JObject.Parse(localState)["os_crypt"]["encrypted_key"].Value<string>();
byte[] encKey = Convert.FromBase64String(encKeyB64);
byte[] dpapiBlob = encKey.Skip(5).ToArray();              // strip "DPAPI"
byte[] aesKey = ProtectedData.Unprotect(dpapiBlob, null, DataProtectionScope.CurrentUser);
// ...open the Cookies SQLite DB, and for each row AES-GCM-decrypt encrypted_value with aesKey.
```

**Why this is broken now — App-Bound Encryption ("v20"):**
- **Chrome 127 (July 2024)** introduced **App-Bound Encryption (ABE)** on Windows. Newer cookies are stored with a `v20` prefix and their key is wrapped in `os_crypt.**app_bound_encrypted_key**` instead of (or in addition to) `encrypted_key`.
- Unwrapping the app-bound key requires a **double DPAPI decryption — first as SYSTEM, then as the user** — and the final unwrap uses AES-GCM with **hardcoded key material that lives inside Chrome's `elevation_service.exe`**. In practice you must **run as SYSTEM/admin and impersonate Chrome's COM elevation service** to get the key.
- This is deliberately the same barrier Google built to stop cookie-stealing malware. TechRadar/AlternativeTo covered ABE's rollout; the mechanics are documented in the PoC repo [runassu/chrome_v20_decryption](https://github.com/runassu/chrome_v20_decryption) and tools like SharpChrome/Chlonium.
- **yt-dlp gave up on Chrome for this reason.** Its guidance and the wider community now steer users to Firefox or manual export. A 2026 write-up that tried six extraction methods found Chrome-based approaches all failed on 127+ and only Firefox's plaintext DB worked ([DEV.to: "6 Ways to Get YouTube Cookies … Only 1 Works"](https://dev.to/osovsky/6-ways-to-get-youtube-cookies-for-yt-dlp-in-2026-only-1-works-2cnb)).

**Bottom line on Option B:** For a normal, non-elevated desktop app targeting a user on current Chrome, **don't** build Chrome-DB decryption. It requires admin/SYSTEM and reads as malware to security software. Legacy `v10` decryption still works only for old cookies / old Chrome and for some Chromium forks that haven't adopted ABE.

### 3.3 Option C: Read Firefox's cookie database (plaintext SQLite)

Firefox does **not** encrypt its cookie store. If the user is willing to log into YouTube in Firefox, C# can read cookies with almost no ceremony.

- Location: `%APPDATA%\Mozilla\Firefox\Profiles\<random>.default-release\cookies.sqlite`
- Table `moz_cookies`, columns `name`, `value`, `host`, `path`, `expiry`.

```csharp
// Requires an SQLite reader (System.Data.SQLite or Microsoft.Data.Sqlite).
// Copy cookies.sqlite to a temp path first if Firefox holds a lock.
using var conn = new SQLiteConnection($"Data Source={tempCopyPath};Version=3;Read Only=True;");
conn.Open();
using var cmd = new SQLiteCommand(
    "SELECT name, value, host, path FROM moz_cookies WHERE host LIKE '%youtube.com%'", conn);
using var r = cmd.ExecuteReader();
var cookies = new List<Cookie>();
while (r.Read())
    cookies.Add(new Cookie(r.GetString(0), r.GetString(1), r.GetString(3), r.GetString(2)));
```

**Pros:** Trivial, no admin, no encryption. This is what yt-dlp users fall back to.
**Cons:** Requires the user to use Firefox and stay logged in there; you may need to copy the DB to dodge a file lock.

### 3.4 Option D: Manual export to `cookies.txt`, parsed in C#

A pragmatic middle ground with no admin and no decryption: the user exports YouTube cookies once via a browser extension into the **Netscape `cookies.txt`** format, and Robin parses that file.

- **Security warning:** use a *trusted, open-source* exporter. The popular closed-source "Get cookies.txt" extension was **removed from the Chrome Web Store as malware** (it exfiltrated cookies). Recommend the open-source **"Get cookies.txt LOCALLY"** fork instead. (Source: the DEV.to 2026 write-up above.)
- **Best practice (from yt-dlp's wiki, see §4):** export from a **private/incognito window**, then close it, so the session isn't rotated/invalidated afterward.

`cookies.txt` is tab-separated: `domain \t includeSubdomains \t path \t secure \t expiry \t name \t value`.

```csharp
var cookies = new List<Cookie>();
foreach (var line in File.ReadLines(cookiesTxtPath))
{
    if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
    var f = line.Split('\t');
    if (f.Length < 7) continue;
    cookies.Add(new Cookie(f[5], f[6], f[2], f[0]));
}
using var youtube = new YoutubeClient(cookies);
```

**Pros:** No admin, no WebView2 build, works with any browser. **Cons:** Manual step for the user; must be redone when cookies expire.

### 3.5 Option E (mention only): Chrome remote-debugging / CDP

You can launch Chrome with `--remote-debugging-port=9222` and pull decrypted cookies via the DevTools Protocol (`Network.getAllCookies`). In practice this is flaky (recent Chrome restricts remote debugging on the default profile; the port frequently won't bind), and it forces a special Chrome launch. Not recommended for Robin.

### 3.6 Option comparison

| Option | Admin? | Works on current Chrome? | User friction | Robustness | Fit for Robin |
|---|---|---|---|---|---|
| **A. In-app WebView2 login** | No | N/A (own cookie jar) | Low (sign in once) | High, but validate Google embedded-login block | **Recommended** |
| B. Decrypt Chrome DB | **Yes (SYSTEM)** | **No** (v20/ABE) | None | Broken + malware-like | Avoid |
| C. Read Firefox DB | No | N/A (Firefox) | User must use Firefox | High | Good fallback |
| D. Manual `cookies.txt` | No | Yes (via extension) | Medium (manual re-export) | Medium | Good pragmatic start |
| E. Chrome CDP | No | Partially | High (special launch) | Low | Avoid |

---

## 4. Q3 — Does YouTube ban accounts used for downloading?

**Short answer: yes, it can, and you should assume so — but "ban" usually means the account gets flagged/blocked from the download path or temporarily throttled, not always full Google-account termination. Because the downside case (losing a Google account) is severe, the universal advice is to use a throwaway account.**

**yt-dlp's own wiki (the most authoritative community source) states verbatim:**
> *"By using your account with yt-dlp, you run the risk of it being banned (temporarily or permanently)."*

and recommends **a throwaway account**, plus this export procedure to avoid invalidating the session:
> 1. *"Open a new private browsing/incognito window and log into YouTube"*
> 2. Navigate to `https://www.youtube.com/robots.txt` as the only tab
> 3. *"Export `youtube.com` cookies from the browser, then close the private browsing/incognito window"*

(The private-window trick matters because a normal logged-in window keeps rotating the session; exporting then closing incognito "freezes" a stable cookie set.)

**Evidence YouTube actively flags accounts:** yt-dlp issue [#10085](https://github.com/yt-dlp/yt-dlp/issues/10085) documents accounts that heavily used yt-dlp getting **blocked from the web `/player` endpoint** ("Video unavailable / This content isn't available"), while the *same account* still worked in the Android app. So enforcement is real and account-scoped, though in that report it manifested as the account being blocked from a route rather than deleted.

**Risk calibration for Robin's situation:**
- **Use a dedicated burner Google account.** Never wire your primary account (Gmail, Drive, etc.) into an automated downloader. This single choice removes almost all of the meaningful downside.
- **Residential + personal-scale use is much lower risk** than server/datacenter/CI use. The maintainer of YoutubeExplode observed that public cloud/CI IP ranges are *pre-emptively* flagged, whereas home connections mostly get flagged only after bursty behavior, and the flag decays (often within an hour).
- **Behave like a human:** avoid parallel bursts and rapid-fire queues. YoutubeExplode issue [#813] notes that a small delay with no parallelism helped avoid flagging.
- **Cookies are as sensitive as a password.** A `cookies.txt` / stored cookie set is a full login. Store it locally, never commit it, and treat leakage as an account compromise.

---

## 5. If you want the most durable fix: switch the download engine to yt-dlp

This is out of scope of "cookies for YoutubeExplode," but it's the honest answer to "what actually holds up." Multiple people on issue #794 migrated from YoutubeExplode to **yt-dlp** driven from .NET via **[YoutubeDLSharp](https://github.com/Bluegrams/YoutubeDLSharp)**, because yt-dlp:
- tracks YouTube's anti-bot changes continuously (client rotation, `--cookies-from-browser`, etc.),
- has **PO-token provider** plugins for the hardest cases,
- supports a **[YouTube OAuth login plugin](https://github.com/coletdjnz/yt-dlp-youtube-oauth2)**.

**Trade-off:** it means bundling/shipping the `yt-dlp` binary (and it still uses FFmpeg, which Robin already resolves), and rewriting the download path behind Robin's existing `IYoutubeClientAdapter`-style seam. Bigger than cookie plumbing, but it's the path least likely to break again next month. Worth weighing against investing in cookie auth for a library whose maintainer is, by his own comments, largely reliant on the random-visitor-data workaround.

---

## 6. Recommended decision path

1. **First, confirm it's still broken and is an IP flag, not the video.** Re-run a direct `ANDROID_VR` `/player` replay (as the prior session did) and read `playabilityStatus`. Try after dropping any VPN / from a fresh IP, and after waiting. If it cleared on its own, stop here.
2. **If you want in-app auth:** implement **Option A (WebView2 login)** and inject cookies through `YoutubeClientAdapter` (§2.6). Validate two things early: (a) Google lets your account log in inside the embedded WebView2, and (b) the resulting cookies actually make `GetManifestAsync` succeed for a normally-failing video. Use a **burner account**.
3. **If WebView2's embedded-login block is a problem:** fall back to **Option D (manual `cookies.txt`)** with the open-source "LOCALLY" exporter, or **Option C (Firefox)**.
4. **Do not** invest in **Option B (Chrome DB decryption)** — App-Bound Encryption makes it require SYSTEM elevation and it behaves like malware.
5. **If cookies prove fragile or PO tokens get demanded:** evaluate migrating the engine to **yt-dlp via YoutubeDLSharp** (§5).

**One-line recommendation:** Prototype **WebView2 login → cookies → `new YoutubeClient(httpClient, cookies)`** with a throwaway account; keep **manual `cookies.txt`** as the fallback; hold **yt-dlp/YoutubeDLSharp** in reserve as the durable Plan B. Try the free IP/VPN/wait checks before writing any code.

---

## 7. Open questions to resolve before implementing

- Is `bYXEhi9No-Y` a plain public video (pure IP wall) or age-restricted/"for kids" (the `#965` case)? The remedy is authentication either way, but it affects how you test.
- Does an authenticated `YoutubeClient` actually clear `GetManifestAsync` for you *today*? Given #781 and the "cookies may not last" comments, this needs an end-to-end spike before committing to the WebView2 build-out.
- Cookie lifetime in practice for a burner account with Robin's usage pattern — determines whether you need an automatic re-auth prompt.
- Does Google's embedded-webview login block trigger for your account? Decides between Option A and Options C/D.

---

## 8. Sources (every URL visited during this research)

**Primary sources read directly (YoutubeExplode + yt-dlp + Chrome ABE):**
- YoutubeExplode `YoutubeClient.cs` (constructor overloads), tag 6.6.1 — https://raw.githubusercontent.com/Tyrrrz/YoutubeExplode/6.6.1/YoutubeExplode/YoutubeClient.cs
- YoutubeExplode README (Authentication section) — https://raw.githubusercontent.com/Tyrrrz/YoutubeExplode/master/Readme.md
- YoutubeExplode discussion #766 — "Authentication – which cookies?" — https://github.com/Tyrrrz/YoutubeExplode/discussions/766
- YoutubeExplode issue #794 — the anti-bot blocker on manifests (comments read via `gh`) — https://github.com/Tyrrrz/YoutubeExplode/issues/794
- YoutubeExplode issue #781 — cookies via WebView2 + still-401 on private video — https://github.com/Tyrrrz/YoutubeExplode/issues/781
- YoutubeExplode issue #962 (Aug 2026 "not available") — https://github.com/Tyrrrz/YoutubeExplode/issues/962
- YoutubeExplode issue #964 (Aug 2026 "not available") — https://github.com/Tyrrrz/YoutubeExplode/issues/964
- YoutubeExplode PR #865 — "add random visitor data" (merged, closed #794) — https://github.com/Tyrrrz/YoutubeExplode/pull/865
- YoutubeExplode PR #965 — age-restricted / for-kids fallback (in progress) — https://github.com/Tyrrrz/YoutubeExplode/pull/965
- YoutubeExplode releases (6.6.1 latest, 2026-08-07) — https://github.com/Tyrrrz/YoutubeExplode/releases
- yt-dlp wiki — Extractors (YouTube cookie export + ban warning + private-window steps) — https://github.com/yt-dlp/yt-dlp/wiki/Extractors
- yt-dlp wiki — FAQ (passing cookies) — https://github.com/yt-dlp/yt-dlp/wiki/FAQ
- yt-dlp issue #10085 — YouTube blocking accounts from web `/player` — https://github.com/yt-dlp/yt-dlp/issues/10085
- Chrome App-Bound Encryption / v20 decryption PoC (runassu) — https://github.com/runassu/chrome_v20_decryption
- DEV.to — "6 Ways to Get YouTube Cookies for yt-dlp in 2026 — Only 1 Works" — https://dev.to/osovsky/6-ways-to-get-youtube-cookies-for-yt-dlp-in-2026-only-1-works-2cnb

**Additional sources surfaced during search and used for cross-checking:**
- YoutubeExplode `YoutubeClient.cs` (master) — https://github.com/Tyrrrz/YoutubeExplode/blob/master/YoutubeExplode/YoutubeClient.cs
- YoutubeExplode repo — https://github.com/tyrrrz/youtubeexplode
- YoutubeExplode issue #852 — private video with cookies (400) — https://github.com/Tyrrrz/YoutubeExplode/issues/852
- YoutubeExplode issue #497 — injecting an HttpClient with cookies — https://github.com/Tyrrrz/YoutubeExplode/issues/497
- YoutubeExplode 6.6.0 on Libraries.io — https://libraries.io/nuget/YoutubeExplode
- WebView2 `CoreWebView2CookieManager.GetCookiesAsync` — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2cookiemanager.getcookiesasync
- WebView2 `CoreWebView2.CookieManager` — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.cookiemanager
- WebView2 cookies (Microsoft Q&A, code-only access) — https://learn.microsoft.com/en-us/answers/questions/903113/
- Chrome cookie decryption write-up (Krptyk) — https://krptyk.com/2023/10/28/decrypting-chrome-cookies/
- Chromium cookie parser/decryptor (DPAPI) — https://github.com/lainalie/Chrome-Chromium-cookie-parser-and-decryptor
- SharpChrome state keys (SpecterOps) — https://docs.specterops.io/ghostpack-docs/SharpDPAPI-mdx/sharpchrome-statekeys
- Chlonium (Chromium cookie import/export) — https://github.com/rxwx/chlonium
- TechRadar — Chrome ABE "can be bypassed" — https://www.techradar.com/pro/security/google-chrome-cookie-encryption-system-can-be-easily-bypassed-experts-warn
- AlternativeTo — Chrome app-bound encryption announcement (Aug 2024) — https://alternativeto.net/news/2024/8/google-enhances-chrome-security-on-windows-with-app-bound-encryption-to-fight-cookie-theft
- yt-dlp issue #10927 — `--cookies-from-browser` DPAPI decrypt failure — https://github.com/yt-dlp/yt-dlp/issues/10927
- yt-dlp.net — "Could not copy Chrome cookie database" — https://yt-dlp.net/errors/could-not-copy-chrome-cookie-database
- youtube-dl issue #32905 (cited in #794: "cookies won't work for long") — https://github.com/ytdl-org/youtube-dl/issues/32905
- yt-dlp issue #12264 — solving "not a bot" with cookies/proxies — https://github.com/yt-dlp/yt-dlp/issues/12264
- yt-dlp issue #15392 — still "not a bot" even with `--cookies` — https://github.com/yt-dlp/yt-dlp/issues/15392
- YoutubeDLSharp (.NET wrapper for yt-dlp) — https://github.com/Bluegrams/YoutubeDLSharp
- yt-dlp — https://github.com/yt-dlp/yt-dlp
- yt-dlp YouTube OAuth2 plugin — https://github.com/coletdjnz/yt-dlp-youtube-oauth2
