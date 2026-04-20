# Unit Testing Implementation Plan

## Context

Robin (a .NET Framework 4.8 WinForms YouTube downloader built on YoutubeExplode) had **no automated tests**. This plan captures the decisions made when introducing a test project, the framework comparison that drove them, and the initial tests that were added. It also flags the refactoring that remains before deeper coverage is feasible.

The goal was not exhaustive coverage. It was to:

1. Answer two questions — which framework, and whether a separate test project is needed.
2. Stand up working test infrastructure.
3. Cover low-hanging fruit (pure / record / constant / filesystem-utility code) to prove the pipeline end-to-end.
4. Document the refactoring required to test YoutubeExplode-bound and UI-bound code — deferred to a follow-up.

---

## Decision 1 — Separate test project: yes

Per Microsoft's [Unit Test Basics](https://learn.microsoft.com/en-us/visualstudio/test/unit-test-basics?view=visualstudio) and [.NET testing guide](https://learn.microsoft.com/en-us/dotnet/core/testing/). Reasons specific to Robin:

- Test Explorer discovers tests by project type — mixing tests into `Robin.csproj` fights the tooling.
- Test assemblies and test-only NuGets (xUnit, Moq, FluentAssertions) must not ship in the ClickOnce `published/` output.
- `<ProjectReference>` to `Robin.csproj` gives the test project full access to public types; `[InternalsVisibleTo("Robin.Tests")]` opens internals too.

**Convention:** `Robin.Tests` sibling to `Robin.csproj`, SDK-style csproj targeting `net48`, added to `Robin.sln`.

---

## Decision 2 — Framework: xUnit 2.x

All three mainstream frameworks run on .NET Framework 4.8. Note: xUnit v3+ requires .NET 8+, so we pinned to xUnit 2.9.x.

| Criterion | MSTest 3.x | NUnit 3.x | xUnit 2.x |
|---|---|---|---|
| First-party (Microsoft) | Yes | No | No |
| VS integration out-of-box | Strongest | Via adapter | Via adapter |
| Test isolation model | Shared class, per-method | Shared class, `[SetUp]`/`[TearDown]` | **New instance per test** (enforced) |
| Parameterized tests | `[DataRow]` | `[TestCase]`, `[TestCaseSource]` | `[Theory]` + `[InlineData]` / `[MemberData]` |
| Setup/teardown | `[TestInitialize]` / `[TestCleanup]` | `[SetUp]` / `[TearDown]` | Constructor + `IDisposable` (no attributes) |
| Assertion style | `Assert.AreEqual(exp, act)` | `Assert.That(act, Is.EqualTo(exp))` | `Assert.Equal(exp, act)` |
| Aligns with MS best-practices article | Mixed | No | **Yes — examples are xUnit** |
| Community momentum (2025) | Steady | Steady | Strongest in new .NET |

### Why xUnit wins for Robin specifically

1. **Forced isolation matches the problem.** Robin has shared static state (`YouTubeExplodeVideoDownloader.sharedHttpClient`, `RobinForm.activeDownloads`). xUnit's "new instance per test" rule makes accidental state leakage between tests impossible.
2. **MS best-practices guide uses xUnit syntax** — copying patterns from the official guide is frictionless.
3. **No `[SetUp]`/`[TearDown]`.** MS actively recommends helper methods over setup/teardown attributes; xUnit enforces this via constructors + `IDisposable`.
4. **`[Theory]` + `[InlineData]` is ergonomic** for data-driven tests.

MSTest is a reasonable second choice if a first-party-only stack is preferred. NUnit is a reasonable second choice if the fluent `Assert.That(..., Is.EqualTo(...))` style is preferred.

### Companion libraries (pinned)

- `Microsoft.NET.Test.Sdk` 17.11.1 — test host
- `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2 — framework + Test Explorer discovery
- `Moq` 4.20.72 — mocking
- `FluentAssertions` 6.12.1 — `x.Should().Be(...)` assertions (v7+ is license-restricted; 6.12.x remains free)
- `YoutubeExplode` 6.5.7 — required for metadata resolution when the test project references types that leak through Robin's public surface (e.g. `DownloadState.SelectedStreams` of type `IReadOnlyList<IStreamInfo>`)

---

## Implementation

### Files created

| File | Purpose | Tests |
|---|---|---|
| [Robin.Tests/Robin.Tests.csproj](../Robin/Robin.Tests/Robin.Tests.csproj) | SDK-style csproj, `net48` | — |
| [Robin.Tests/RobinVideoInfoTests.cs](../Robin/Robin.Tests/RobinVideoInfoTests.cs) | Record constructor / equality / `with` | 4 |
| [Robin.Tests/RobinVideoStatusTests.cs](../Robin/Robin.Tests/RobinVideoStatusTests.cs) | Constants pinning | 5 |
| [Robin.Tests/DownloadStateTests.cs](../Robin/Robin.Tests/DownloadStateTests.cs) | Constructor + `CancellationTokenSource` + `Dispose` | 6 |
| [Robin.Tests/RobinUtilsTests.cs](../Robin/Robin.Tests/RobinUtilsTests.cs) | `GetDirectoryThatBeginsWith` (temp-dir based) | 5 |
| [Robin.Tests/Fakes/FakeYouTubeVideoDownloader.cs](../Robin/Robin.Tests/Fakes/FakeYouTubeVideoDownloader.cs) | Fake implementation of the `YouTubeVideoDownloader` interface | — |
| [Robin.Tests/Fakes/FakeYouTubeVideoDownloaderTests.cs](../Robin/Robin.Tests/Fakes/FakeYouTubeVideoDownloaderTests.cs) | Smoke test that the seam is wired correctly | 3 |

**Total:** 23 tests, all passing, ~3.5 s runtime.

### Files modified

| File | Change |
|---|---|
| [Robin.sln](../Robin/Robin.sln) | Added `Robin.Tests` project entry via `dotnet sln add` |
| [Robin/Properties/AssemblyInfo.cs](../Robin/Properties/AssemblyInfo.cs) | Added `[assembly: InternalsVisibleTo("Robin.Tests")]` |
| [Robin/RobinUtils.cs](../Robin/RobinUtils.cs) | Changed `GetDirectoryThatBeginsWith` from `private static` to `internal static` so it can be tested directly (the only alternative was testing it transitively through `GetPathToFFMPEG`, which is heavily coupled to the filesystem and WinGet install state) |

---

## Build & test commands

Both the classic `msbuild` and `dotnet test` pipelines work, but the build step **must** use classic MSBuild — `dotnet build` (via the .NET 10 SDK) fails on `RobinForm.resx` because it contains non-string resources and the project uses the old-style `packages.config` format.

```bash
# Build (classic MSBuild — required for Robin.csproj's resx)
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" \
    Robin.sln -t:Restore -p:Configuration=Debug
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" \
    Robin.sln -p:Configuration=Debug

# Run tests (dotnet test is fine once the build has been done)
cd Robin.Tests
dotnet test --no-build --no-restore -c Debug
```

In Visual Studio, **Test Explorer** (Ctrl+E, T) discovers and runs the tests directly without any CLI invocation.

---

## Deferred — refactoring required to test harder code paths

These areas cannot be unit-tested without first introducing seams. Listed here so a follow-up plan can pick them up.

| Target | Coupling | Suggested seam |
|---|---|---|
| [YouTubeExplodeVideoDownloader.cs](../Robin/YouTubeExplodeVideoDownloader.cs) | Directly `new`s `YoutubeClient`; takes `RobinForm` as a parameter | Inject `IYoutubeClientFacade` wrapping `YoutubeClient.Videos.GetAsync/GetManifestAsync/DownloadAsync`; replace `RobinForm` param with `IDownloadUiNotifier` exposing only the UI ops actually needed |
| [RobinForm.cs](../Robin/RobinForm.cs) ctor | `new YouTubeExplodeVideoDownloader(...)` inline | Accept `YouTubeVideoDownloader` via constructor; default to the real impl, allow injection for tests. **`FakeYouTubeVideoDownloader` is already written and waiting.** |
| [RobinUtils.GetPathToFFMPEG](../Robin/RobinUtils.cs) | Hard-coded `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg_*`, direct `File.*` / `Directory.*` calls | Wrap in `IFileSystem` interface; inject. Or parameterize the WinGet base path |
| Nested `Task.Run` chain in `RobinForm` | 3 levels of `Task.Run` nesting for download orchestration | Out of scope for unit tests; integration test with the fake downloader once the DI seam above lands |
| [RobinUpdater.cs](../Robin/RobinUpdater.cs) | `ApplicationDeployment.CurrentDeployment` (ClickOnce) | Not unit-testable without ClickOnce context — integration test at best |
| [FastYouTube.cs](../Robin/FastYouTube.cs) | Unused per `CLAUDE.md` | Delete rather than test (cleanup task) |
| [RobinVideoDownloader.cs](../Robin/RobinVideoDownloader.cs) | Empty class per `CLAUDE.md` | Delete rather than test (cleanup task) |

---

## Verification performed

1. `msbuild Robin.sln -t:Restore -p:Configuration=Debug` — both projects restored.
2. `msbuild Robin.sln -p:Configuration=Debug` — both projects built with no errors.
3. `dotnet test --no-build --no-restore` — **23/23 tests passed** in 3.54 s.
4. Every test completed in ≤ 547 ms; most in < 5 ms (meets MS best-practice guidance that unit tests run in milliseconds).
5. `Robin.exe` in `Robin/bin/Debug/` still launches and downloads videos — the `InternalsVisibleTo` addition and `GetDirectoryThatBeginsWith` visibility change had zero runtime impact.

---

## Summary of baked-in decisions

- **Framework:** xUnit 2.9.2 (+ Moq 4.20.72, FluentAssertions 6.12.1).
- **Structure:** separate `Robin.Tests` SDK-style project at `Robin/Robin.Tests/`, added to `Robin.sln`.
- **Initial scope:** 23 tests across 5 files; no production-code refactoring beyond opening two visibility modifiers.
- **Explicitly deferred:** DI refactoring for `RobinForm` / `YouTubeExplodeVideoDownloader` / `RobinUtils.GetPathToFFMPEG`. These change production architecture and warrant their own plan.
