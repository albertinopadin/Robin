# WinUI 3 UI + RobinCore/RobinWinForms/RobinWinUI3 Project Split — Feasibility & Implementation Plan

## Verdict up front

**The split is feasible.** The testability refactor already did most of the decoupling work: the download pipeline talks to the UI only through `IDownloadUiNotifier`, and every NuGet dependency the non-UI code needs (YoutubeExplode/Converter 6.6.0, NLog 6.1.4) ships a `netstandard2.0` asset, which is consumable from both .NET Framework 4.8 and modern .NET. Three constraints shape the design:

1. **WinUI 3 cannot run on .NET Framework 4.8.** WinUI 3 ships only via the Windows App SDK, which requires .NET 6+ (current templates default to .NET 10). So `RobinWinUI3` must be a modern-.NET project, which forces `RobinCore` to target something both runtimes can consume: **`netstandard2.0`** (Microsoft's explicitly recommended TFM for sharing code between .NET Framework and modern .NET).
2. **ClickOnce does not support WinUI 3 apps** (microsoft/microsoft-ui-xaml#5158, closed as not planned), and the Visual Studio ClickOnce publish flow does not support SDK-style net48 projects either. Consequence: the WinForms app keeps its **old-style csproj and existing ClickOnce pipeline unchanged**, and the WinUI 3 app ships through a different channel — an unpackaged, self-contained portable folder/zip, exactly how ForzaRivalsMeta ships.
3. **Three small WinForms leaks remain in the "core" code** and must be removed before extraction: `DownloadState` holds `ListViewItem`/`ProgressBar` properties, `IDownloadUiNotifier.UpdateDownloadStatus` takes a `ListViewItem` parameter, and `YouTubeExplodeVideoDownloader` calls `RobinUtils.DisplayAndLogException`, which shows a WinForms `MessageBox`.

The UI selector ("which UI opens next time") is a small shared JSON settings file in `%APPDATA%\Robin\` that both apps read at startup; each app offers a toggle and restarts into the other. This works cleanly because the WinUI 3 app is **unpackaged** — it reads/writes the real `%APPDATA%` with no MSIX virtualization involved.

---

## Context

Robin today: one old-style `Robin/Robin.csproj` (net48, WinForms, packages.config, ClickOnce publish to `published/` served from raw.githubusercontent.com) plus the SDK-style `Robin/Robin.Tests/Robin.Tests.csproj` (net48, xUnit) that already successfully `ProjectReference`s the old-style project.

Reference app for the new UI: [ForzaRivalsMeta](https://github.com/albertinopadin/ForzaRivalsMeta) — WinUI 3, Windows App SDK 1.8, net9.0-windows10.0.19041.0, with these conventions (verified from the repo):

- `src/` split: `ForzaRivalsMeta.Core` (no UI deps) / `Core.Tests` / `ForzaRivalsMeta.App` (WinUI 3) — the same shape requested for Robin.
- **Hand-rolled MVVM**: a tiny `Mvvm.cs` (`ObservableObject` + `RelayCommand`, ~40 lines) instead of CommunityToolkit.Mvvm; no DI container; VMs new up their services; the view exposes `public MainViewModel Vm { get; } = new();` for `{x:Bind}`.
- **Single `Window` whose content is one `MainView` UserControl**; `MainWindow` is chrome-only (Mica backdrop, `ExtendsContentIntoTitleBar = true`, `SetTitleBar(View.TitleBar)`, `AppWindow.SetIcon(...)` for the unpackaged taskbar icon). No NavigationView/Frame; overlays for detail panes.
- Flat file layout with plural-named files (`ViewModels.cs`, `Converters.cs`), `{x:Bind}` compiled bindings, `ThemeResource` brushes, rounded-corner (`CornerRadius="8"`) card styling.
- **Distribution: unpackaged + self-contained portable zip** built by a `publish.ps1` (`dotnet publish -r win-x64 --self-contained true` + `Compress-Archive`); no installer, no auto-update. Runs on Windows 10 1809+ x64 with nothing preinstalled.

## Key feasibility facts (researched July 2026)

| Question | Answer |
|---|---|
| WinUI 3 on .NET Framework 4.8? | **No.** Windows App SDK requires .NET 6+; docs' examples use net8+, current templates net10. |
| Latest Windows App SDK | **2.2.0** (June 2026); 2.1 added VS 2026 build support. Runs on Windows 10 1809+ (build 17763). |
| Unpackaged WinUI 3 app? | Fully supported: `WindowsPackageType=None`; runtime either via `WindowsAppRuntimeInstall.exe` or bundled with `WindowsAppSDKSelfContained=true`. |
| ClickOnce for WinUI 3? | **No** — closed as not planned. MSIX/`.appinstaller`/own installer are the options. |
| ClickOnce for SDK-style net48? | **No** (legacy project system only). Old-style net48 csproj keeps working; modern .NET (8/9/10) WinForms gets a *different* (publish-profile-based) ClickOnce flow without `ApplicationDeployment`. |
| netstandard2.0 lib consumed by net48 + net10 apps? | Yes on both; it is the officially recommended sharing TFM. net48 consumers need binding redirects (already present in `App.config`). |
| Package TFMs | YoutubeExplode 6.6.0 & Converter: netstandard2.0/net6/net7/net10. NLog 6.1.4: netstandard2.0. FFMpegCore 5.4.0: netstandard2.0 — **but it is referenced by zero lines of Robin code and can simply be dropped**. |
| packages.config gotcha | Transitive deps of a referenced SDK-style project do **not** flow to a packages.config consumer. Not a problem here: Robin's packages.config already lists the full closure (it has no transitivity today), so nothing changes at runtime. |

Why keep the WinForms app on net48 rather than upgrading it to .NET 10 too? Upgrading would be possible (WinForms runs fine on modern .NET), but it would (a) break the existing ClickOnce identity/update chain for current installs (`ApplicationDeployment` doesn't exist on modern .NET; the update UX and `RobinUpdater` would need rewriting), and (b) contradict the stated goal of keeping the current UI as a low-risk *backup*. Keeping it untouched on net48/ClickOnce is the conservative choice. It can be revisited later.

---

## Goals

- New `RobinWinUI3` app with **feature parity** with the WinForms UI, implemented idiomatically (MVVM, `{x:Bind}`, ContentDialog/InfoBar instead of MessageBox), in the ForzaRivalsMeta style.
- `RobinCore` (netstandard2.0) holding all non-UI logic; both UIs consume it via ProjectReference.
- WinForms UI preserved as a selectable backup with its ClickOnce pipeline **unchanged** (same assembly name `Robin`, same publish URL, so existing installs keep updating).
- UI choice persisted in shared settings; switching takes effect on app restart.
- `Robin.Tests` retargeted at `RobinCore` and kept green throughout.

## Non-goals

- No upgrade of the WinForms app off net48 (see above).
- No MSIX packaging or Store distribution for the WinUI 3 app (portable zip first; MSIX can come later — see Risks for the AppData-virtualization caveat if that changes).
- No auto-update mechanism for the WinUI 3 app in v1 (matches ForzaRivalsMeta; a "check GitHub for newer release" menu action is a cheap optional add-on).
- No DI container, no CommunityToolkit.Mvvm, no NavigationView — deliberately matching ForzaRivalsMeta's hand-rolled minimalism.

---

## Target structure

```
Robin/                              (repo root)
├── plans/, published/, ...
└── Robin/                          (solution folder, as today)
    ├── Robin.sln                   (gains 2 projects)
    ├── Robin.csproj                (WinForms app — STAYS PUT, old-style, net48, ClickOnce)
    ├── RobinForm.cs, Program.cs, RobinUpdater.cs, ColorBar.cs, RobinUtils.cs, ...
    ├── RobinCore/
    │   ├── RobinCore.csproj        (SDK-style, netstandard2.0)
    │   ├── DownloadState.cs, IDownloadUiNotifier.cs, YouTubeVideoDownloader.cs,
    │   │   YouTubeExplodeVideoDownloader.cs, IYoutubeClientAdapter.cs, YoutubeClientAdapter.cs,
    │   │   IFileSystem.cs, RealFileSystem.cs, FFmpegPathResolver.cs,
    │   │   RobinVideoInfo.cs, RobinVideoStatus.cs, UiPreference.cs (new)
    ├── RobinWinUI3/
    │   ├── RobinWinUI3.csproj      (SDK-style, net10.0-windows10.0.19041.0, WASDK 2.2)
    │   ├── App.xaml(.cs), MainWindow.xaml(.cs), MainView.xaml(.cs),
    │   ├── ViewModels.cs, Mvvm.cs, Converters.cs, app.manifest, publish.ps1
    │   └── Assets/ (robin.ico, robin.png)
    └── Robin.Tests/                (retargeted at RobinCore)
```

**Why `Robin.csproj` stays physically where it is:** its ClickOnce configuration (absolute `PublishUrl`, `Robin_TemporaryKey.pfx`, `Properties\app.manifest`, `AssemblyName=Robin`) is the one thing we must not destabilize. It gets a solution *display* rename to "RobinWinForms" only if desired (cosmetic; optional). Moving/renaming the assembly would orphan existing ClickOnce installs.

The new projects use `RootNamespace`s that keep core types in the existing `Robin` namespace, so moved files need no edits beyond the planned diffs.

---

## Phase 1 — Remove the three WinForms leaks from the pipeline (still in one project)

Do this *before* moving files so each step compiles and tests stay green.

### 1a. `DownloadState`: replace WinForms control references with an opaque UI handle

```diff
-using System.Windows.Forms;
 using YoutubeExplode.Videos.Streams;
 ...
-        public ListViewItem ListViewItem { get; set; }
-        public ProgressBar ProgressBar { get; set; }
+        /// <summary>
+        /// Opaque handle owned by the UI layer. RobinWinForms stores a
+        /// WinFormsRowUi (ListViewItem + ProgressBar pair); RobinWinUI3 stores
+        /// its DownloadItemViewModel. Core code never inspects it.
+        /// </summary>
+        public object UiContext { get; set; }
```

WinForms side gets a tiny holder plus a helper, and every `state.ListViewItem` / `state.ProgressBar` usage in `RobinForm` goes through it:

```csharp
// RobinForm.cs (or a new WinFormsRowUi.cs next to it)
internal sealed class WinFormsRowUi
{
    public ListViewItem ListViewItem;
    public ProgressBar ProgressBar;
}

private static WinFormsRowUi Row(DownloadState state) => (WinFormsRowUi)state.UiContext;
```

```diff
 // AddVideoItemToDownloadsList
-            state.ListViewItem = videoItem;
-            state.ProgressBar = CreateProgressBar(...);
-            listView_downloads.Controls.Add(state.ProgressBar);
+            var row = new WinFormsRowUi { ListViewItem = videoItem };
+            row.ProgressBar = CreateProgressBar(videoItem.SubItems[3].Bounds, state.VideoTitle, videoSize);
+            state.UiContext = row;
+            listView_downloads.Controls.Add(row.ProgressBar);
```

### 1b. `IDownloadUiNotifier`: drop the `ListViewItem` parameter, add error display

```diff
-using System.Windows.Forms;
-
 namespace Robin
 {
-    internal interface IDownloadUiNotifier
+    public interface IDownloadUiNotifier
     {
         void SetCursorLoading();
         void SetCursorNormal();
         void ClearVideoUrlTextbox();
         void SetVideoInfo(RobinVideoInfo info);
         void AddVideoToDownloadsList(DownloadState state, int videoSizeMb);
         void RegisterActiveDownload(string videoTitle, DownloadState state);
-        void UpdateDownloadStatus(ListViewItem item, string status);
+        void UpdateDownloadStatus(DownloadState state, string status);
         void CleanupDownload(string videoTitle);
         void CancelProgressBarForVideo(DownloadState state);
         void DisableCancelButton(string videoTitle);
         void NotifyDownloadFinished(DownloadState state);
         void ReportDownloadProgress(DownloadState state, int progressValue);
+        /// <summary>Surface an error to the user (WinForms: MessageBox; WinUI 3: InfoBar).</summary>
+        void DisplayError(Exception e);
     }
 }
```

(`internal` → `public` because two app projects will implement it. Same for `YouTubeVideoDownloader`, `YouTubeExplodeVideoDownloader`, and its main constructor; the seam-injection constructor stays `internal` + `InternalsVisibleTo("Robin.Tests")`.)

`RobinForm.UpdateDownloadStatus(ListViewItem, string)` keeps its body but the interface entry point becomes `UpdateDownloadStatus(DownloadState state, string status)` → `UpdateDownloadStatus(Row(state).ListViewItem, status)`. `GetVideoTitleFromListItem` is only used form-internally and stays.

### 1c. `YouTubeExplodeVideoDownloader`: stop calling MessageBox-backed `RobinUtils`

```diff
     catch (Exception e)
     {
         ...
             logger.Error(e, $"Download failed for video: {state.VideoId}");
-            RobinUtils.DisplayAndLogException(e);
-
-            notifier.UpdateDownloadStatus(state.ListViewItem, RobinVideoStatus.Failed);
+            notifier.DisplayError(e);
+            notifier.UpdateDownloadStatus(state, RobinVideoStatus.Failed);
```

And the default constructor resolves ffmpeg via the resolver directly instead of `RobinUtils.GetPathToFFMPEG()` (whose MessageBox-on-failure moves to the callers):

```diff
         public YouTubeExplodeVideoDownloader(string baseFilePath)
-            : this(baseFilePath, new YoutubeClientAdapter(sharedHttpClient), RobinUtils.GetPathToFFMPEG())
+            : this(baseFilePath, new YoutubeClientAdapter(sharedHttpClient), FFmpegPathResolver.Default().Resolve())
```

`RobinUtils` stays behind in the WinForms project as pure UI error display (`DisplayAndLogException`); `RobinForm` implements `DisplayError` with it. `RobinForm.RobinDownloadVideoWithChecks`'s own `RobinUtils.DisplayAndLogException` call is WinForms-side code and is unaffected.

Also update the test fakes (`FakeDownloadUiNotifier`) for the two signature changes — mechanical.

**Gate:** build + full test run + a manual smoke download. Behavior must be identical.

## Phase 2 — Extract `RobinCore`

### RobinCore.csproj (new)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <RootNamespace>Robin</RootNamespace>
    <AssemblyName>RobinCore</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="YoutubeExplode" Version="6.6.0" />
    <PackageReference Include="YoutubeExplode.Converter" Version="6.6.0" />
    <PackageReference Include="NLog" Version="6.1.4" />
    <!-- YoutubeExplode dep, pinned: 1.2.1+ is strong-named and breaks net48 binding. -->
    <PackageReference Include="JsonExtensions" Version="[1.2.0]" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Robin.Tests" />
  </ItemGroup>

</Project>
```

Notes:
- **No FFMpegCore** — verified unused in code (`grep FFMpegCore` over `*.cs`: zero hits). Remove it from the WinForms project too while we're here (packages.config + Reference), shrinking the ClickOnce payload.
- Records already compile on netstandard2.0 thanks to the existing `IsExternalInit` shim in `RobinVideoInfo.cs`.
- `ValueTask<string>` comes from `System.Threading.Tasks.Extensions`, transitive via YoutubeExplode's netstandard2.0 dependency set.

### Files that move (git mv, no namespace change)

`DownloadState.cs`, `IDownloadUiNotifier.cs`, `YouTubeVideoDownloader.cs`, `YouTubeExplodeVideoDownloader.cs`, `IYoutubeClientAdapter.cs`, `YoutubeClientAdapter.cs`, `IFileSystem.cs`, `RealFileSystem.cs`, `FFmpegPathResolver.cs`, `RobinVideoInfo.cs`, `RobinVideoStatus.cs`.

Stay in WinForms: `RobinForm.*`, `Program.cs`, `RobinUpdater.cs` (System.Deployment is net48-only), `ColorBar.cs` (Win32 P/Invoke on WinForms ProgressBar), `RobinUtils.cs` (trimmed to error display; the `GetDirectoryThatBeginsWith` shim moves into `FFmpegPathResolver` or is deleted if only tests use it — tests call the resolver directly).

### Robin.csproj (WinForms) diff

```diff
   <ItemGroup>
-    <Compile Include="ColorBar.cs" />
-    <Compile Include="DownloadState.cs" />
-    <Compile Include="FFmpegPathResolver.cs" />
-    <Compile Include="IDownloadUiNotifier.cs" />
-    <Compile Include="IFileSystem.cs" />
-    <Compile Include="IYoutubeClientAdapter.cs" />
-    <Compile Include="RealFileSystem.cs" />
-    <Compile Include="YoutubeClientAdapter.cs" />
+    <Compile Include="ColorBar.cs" />
+    <Compile Include="WinFormsRowUi.cs" />
     <Compile Include="RobinForm.cs">
       <SubType>Form</SubType>
     </Compile>
     ...
-    <Compile Include="RobinVideoInfo.cs" />
-    <Compile Include="RobinVideoStatus.cs" />
-    <Compile Include="YouTubeExplodeVideoDownloader.cs" />
-    <Compile Include="YouTubeVideoDownloader.cs" />
   </ItemGroup>
+  <ItemGroup>
+    <ProjectReference Include="RobinCore\RobinCore.csproj">
+      <Project>{generate-new-guid}</Project>
+      <Name>RobinCore</Name>
+    </ProjectReference>
+  </ItemGroup>
```

**packages.config stays exactly as-is** (minus FFMpegCore/Instances if removed). Because packages.config has no transitivity, it already enumerates the full dependency closure the moved code needs, and the existing `<Reference>`+HintPath items keep copying those DLLs to `bin\` — which is also what the ClickOnce manifest is generated from. This is the zero-risk option for the ClickOnce pipeline. (The cleaner PackageReference migration is deliberately deferred; see Risks.)

**Gate:** `nuget restore` + amd64 MSBuild build + tests + **a ClickOnce test publish** (bump `ApplicationRevision`, publish to a scratch folder, install and launch) to prove the manifest picks up `RobinCore.dll`. ProjectReference outputs are included in ClickOnce manifests as a matter of course, but this is the step to verify it early.

## Phase 3 — Retarget `Robin.Tests`

```diff
   <ItemGroup>
     <Reference Include="System.Net.Http" />
-    <Reference Include="System.Windows.Forms" />
   </ItemGroup>

   <ItemGroup>
-    <ProjectReference Include="..\Robin.csproj" />
+    <ProjectReference Include="..\RobinCore\RobinCore.csproj" />
   </ItemGroup>
```

Tests keep running on net48 with the existing VS 2026 `vstest.console.exe` flow — that's the runtime the WinForms app uses, and netstandard2.0 code is exercised fine from it. Optionally later: `<TargetFrameworks>net48;net10.0</TargetFrameworks>` to also exercise the modern runtime the WinUI app uses (`dotnet test` for the net10 leg). The `RobinUtilsTests` that exercised the shims move to `FFmpegPathResolverTests`; fakes lose their `System.Windows.Forms` usings.

## Phase 4 — `RobinWinUI3` (ForzaRivalsMeta-style)

### RobinWinUI3.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>Robin.WinUI</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x64</Platforms>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>

    <!-- Unpackaged, self-contained: a plain folder you can zip and double-click,
         same as ForzaRivalsMeta. Also means settings live in the real %APPDATA%. -->
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <EnableMsixTooling>true</EnableMsixTooling>

    <ApplicationIcon>Assets\robin.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.2.0" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4654" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RobinCore\RobinCore.csproj" />
  </ItemGroup>

</Project>
```

Choices vs ForzaRivalsMeta: **net10.0** instead of net9.0 (.NET 9 is STS and out of support as of May 2026; .NET 10 is the current LTS), **WASDK 2.2.0** instead of 1.8 (2.1+ is the line with VS 2026 build support). Same TargetPlatformMinVersion → runs on Windows 10 1809+. WinUI 3 apps must pick an architecture (no AnyCPU); x64-only matches ForzaRivalsMeta. Everything builds/publishes with `dotnet build`/`dotnet publish` (no MSBuild-x86 ClickOnce landmine in this project).

`Assets\robin.ico` / `robin.png` are generated from the existing `Robin/robin.ico`.

### Mvvm.cs

Copy the ForzaRivalsMeta pattern verbatim (tiny `ObservableObject` with `Set<T>` + `RelayCommand`) — no CommunityToolkit dependency.

### Window/View skeleton

```csharp
// App.xaml.cs
protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
{
    RobinLogging.Configure();               // same NLog builder as WinForms Program.cs
    _window = new MainWindow();
    _window.Activate();
}
```

```csharp
// MainWindow.xaml.cs — chrome only, ForzaRivalsMeta pattern
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();                       // graceful fallback on Win10
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(View.TitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "robin.ico"));
        Closed += (_, _) => View.Vm.CancelAllDownloads();          // parity with FormClosing
    }
}
```

```xml
<!-- MainWindow.xaml -->
<Window ...>
    <local:MainView x:Name="View"/>
</Window>
```

### MainView.xaml (representative core)

Rows: title bar (48) / URL bar / video info card + InfoBar / downloads list.

```xml
<Grid RowDefinitions="48,Auto,Auto,*">

    <!-- Title bar: app icon, name, version badge, UI-switcher toggle -->
    <Grid x:Name="AppTitleBar" Height="48" Padding="14,0" ColumnDefinitions="Auto,*,Auto">
        <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
            <Image Source="ms-appx:///Assets/robin.png" Width="20" Height="20"/>
            <TextBlock Text="Robin" FontWeight="SemiBold" VerticalAlignment="Center"/>
            <Border Background="{ThemeResource AccentFillColorDefaultBrush}" CornerRadius="4" Padding="6,1">
                <TextBlock Text="{x:Bind Vm.AppVersion}" FontSize="11" FontWeight="Bold" Foreground="White"/>
            </Border>
        </StackPanel>
    </Grid>

    <!-- URL entry -->
    <Grid Grid.Row="1" Padding="16,10" ColumnDefinitions="*,Auto" ColumnSpacing="8">
        <TextBox PlaceholderText="Paste a YouTube URL and press Enter"
                 Text="{x:Bind Vm.VideoUrl, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                 KeyDown="UrlBox_KeyDown"/>
        <Button Grid.Column="1" Content="Download" Style="{StaticResource AccentButtonStyle}"
                Command="{x:Bind Vm.DownloadCommand}"/>
    </Grid>

    <!-- Video info card + status InfoBar -->
    <StackPanel Grid.Row="2" Padding="16,0,16,8" Spacing="8">
        <Border Background="{ThemeResource SubtleFillColorSecondaryBrush}" CornerRadius="8" Padding="12,8"
                Visibility="{x:Bind Vm.HasVideoInfo, Mode=OneWay}">
            <StackPanel>
                <TextBlock Style="{StaticResource OverlineText}" Text="CURRENT VIDEO"/>
                <TextBlock Text="{x:Bind Vm.Info.Title, Mode=OneWay}" FontWeight="SemiBold" TextWrapping="Wrap"/>
                <TextBlock Opacity="0.7" FontSize="12"
                           Text="{x:Bind Vm.InfoLine, Mode=OneWay}"/>
                           <!-- e.g. "mp4 · 1920x1080 · 4.2 Mbps · 812.44 MB" -->
            </StackPanel>
        </Border>
        <InfoBar IsOpen="{x:Bind Vm.HasError, Mode=OneWay}" Severity="Error"
                 Title="Robin Error" Message="{x:Bind Vm.ErrorMessage, Mode=OneWay}"/>
    </StackPanel>

    <!-- Downloads -->
    <ListView Grid.Row="3" Padding="8" ItemsSource="{x:Bind Vm.Downloads}"
              IsItemClickEnabled="True" ItemClick="Download_ItemClick"
              SelectionMode="None">
        <ListView.ItemTemplate>
            <DataTemplate x:DataType="local:DownloadItemViewModel">
                <Grid Background="{ThemeResource SubtleFillColorSecondaryBrush}" CornerRadius="8"
                      Padding="12,10" Margin="0,4" ColumnDefinitions="*,Auto,Auto" ColumnSpacing="12">
                    <StackPanel Spacing="4">
                        <TextBlock Text="{x:Bind Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Text="{x:Bind FilePath, Mode=OneWay}" FontSize="11" Opacity="0.6"/>
                        <ProgressBar Value="{x:Bind ProgressMb, Mode=OneWay}" Maximum="{x:Bind SizeMb}"
                                     Foreground="{x:Bind BarBrush, Mode=OneWay}"/>
                    </StackPanel>
                    <Border Grid.Column="1" VerticalAlignment="Center" CornerRadius="6" Padding="8,3"
                            Background="{x:Bind StatusBrush, Mode=OneWay}">
                        <TextBlock Text="{x:Bind Status, Mode=OneWay}" FontSize="12" FontWeight="SemiBold"/>
                    </Border>
                    <Button Grid.Column="2" Content="Cancel" VerticalAlignment="Center"
                            Command="{x:Bind CancelCommand}"
                            Visibility="{x:Bind CanCancel, Mode=OneWay}"/>
                </Grid>
            </DataTemplate>
        </ListView.ItemTemplate>
    </ListView>
</Grid>
```

(`ListView` + DataTemplate is the direct analog of today's ListView and remains fully idiomatic; `ItemsView` is the newer alternative but adds `ItemContainer` template constraints for no benefit at this list size.)

### ViewModels.cs — the notifier is the view model

`MainViewModel` implements `IDownloadUiNotifier` and marshals to the UI thread with `DispatcherQueue` — the WinUI equivalent of the recursive `InvokeRequired` pattern:

```csharp
public sealed class MainViewModel : ObservableObject, IDownloadUiNotifier
{
    private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly DispatcherQueue _dispatcher;   // captured on the UI thread at construction
    private readonly YouTubeVideoDownloader _downloader;
    private readonly ConcurrentDictionary<string, DownloadState> _activeDownloads = new();

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = new();

    // --- IDownloadUiNotifier (called from worker threads) ---

    public void AddVideoToDownloadsList(DownloadState state, int videoSizeMb)
    {
        // Create + attach the item VM synchronously so UiContext is set before
        // the first progress callback; only the collection mutation needs the UI thread.
        var item = new DownloadItemViewModel(state, title => CancelDownload(title)) { SizeMb = Math.Max(videoSizeMb, 1) };
        state.UiContext = item;
        _dispatcher.TryEnqueue(() => Downloads.Add(item));
    }

    public void ReportDownloadProgress(DownloadState state, int progressValue)
        => _dispatcher.TryEnqueue(() =>
        {
            if (state.UiContext is DownloadItemViewModel item) item.ProgressMb = progressValue;
        });

    public void UpdateDownloadStatus(DownloadState state, string status)
        => _dispatcher.TryEnqueue(() =>
        {
            if (state.UiContext is DownloadItemViewModel item) item.Status = status;
        });

    public void DisplayError(Exception e)
        => _dispatcher.TryEnqueue(() => { ErrorMessage = e.Message; HasError = true; });

    public void SetCursorLoading()  => _dispatcher.TryEnqueue(() => IsBusy = true);   // ProgressRing, not cursor
    public void SetCursorNormal()   => _dispatcher.TryEnqueue(() => IsBusy = false);
    // ... SetVideoInfo, ClearVideoUrlTextbox, RegisterActiveDownload, CleanupDownload,
    //     CancelProgressBarForVideo, DisableCancelButton, NotifyDownloadFinished:
    //     same shapes — flip observable properties on the item VM via TryEnqueue.
}
```

`DownloadItemViewModel` holds `Title`, `Status`, `FilePath`, `SizeMb`, `ProgressMb`, `CanCancel`, a `CancelCommand`, and derived `StatusBrush`/`BarBrush` (accent for Downloading, green for Done, gray for Cancelled, red for Failed — replacing the WinForms row-color hacks and the `ColorBar` P/Invoke, which has no WinUI counterpart or need).

The duplicate-download / cancel-confirmation `MessageBox` flows become `async` `ContentDialog`s in `MainViewModel.DownloadAsync()` — same decision tree as `RobinDownloadVideoWithChecks_OnUIThread`, but naturally async instead of nested `Task.Run` + `Invoke`. Item click opens the file with `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })` (the bare `Process.Start(path)` overload no longer shells out on modern .NET).

Feature-parity checklist (everything in the WinForms UI, mapped):

| WinForms today | RobinWinUI3 |
|---|---|
| URL TextBox + Enter key | TextBox + KeyDown → `DownloadCommand` |
| Download button | Accent Button |
| Video info labels (title/ext/res/bitrate/size) | Info card bound to `RobinVideoInfo` |
| ListView row: name, status, location | ListView DataTemplate: title, status badge, path |
| Per-row ProgressBar overlaid on ListView | ProgressBar inside the row template (native) |
| Per-row Cancel button + confirm MessageBox | Button + `ContentDialog` |
| Already-downloaded / in-progress MessageBox prompts | `ContentDialog` yes/no |
| Row colors for Done/Cancelled/Failed + ColorBar hack | Status badge + ProgressBar `Foreground` brushes |
| Double-click row opens video | `ItemClick` → `Process.Start(UseShellExecute)` |
| Wait cursor during metadata fetch | `ProgressRing` bound to `IsBusy` |
| Error MessageBoxes from pipeline | `InfoBar` (Severity=Error) via `DisplayError` |
| App version label (ClickOnce version) | Title-bar badge: `Assembly.GetExecutingAssembly().GetName().Version` |
| File → Check for Updates (ClickOnce) | Not applicable (unpackaged); optional "check GitHub releases" link — see Phase 6 |
| FormClosing cancels active downloads | `Window.Closed` → `CancelAllDownloads()` |

**Gate:** side-by-side manual test of both UIs against the same URLs (normal video, live stream fallback, cancel mid-download, duplicate re-download, ffmpeg-missing error path).

## Phase 5 — UI switcher

### Shared preference (new `RobinCore/UiPreference.cs`)

Plain JSON at `%APPDATA%\Robin\ui-settings.json` (System.Text.Json is already in the dependency closure on both TFMs; the WinUI app is unpackaged so there is **no MSIX AppData virtualization** — both apps see the same real file):

```csharp
public sealed class UiPreference
{
    public string PreferredUi { get; set; } = "WinForms";   // "WinForms" | "WinUI3"
    public string WinFormsLaunchPath { get; set; }
    public string WinUI3LaunchPath { get; set; }

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Robin", "ui-settings.json");

    public static UiPreference Load() { /* File.ReadAllText + JsonSerializer.Deserialize, defaults on any failure */ }
    public void Save()                { /* Directory.CreateDirectory + atomic-ish write, best-effort try/catch */ }
}
```

### Startup handshake + redirect (both apps, mirrored logic)

Each app, at startup:
1. Records its own launch path into the file (`WinFormsLaunchPath` = the `.appref-ms` under the Start Menu when ClickOnce-deployed — keeps update checks working — else `Application.ExecutablePath`; `WinUI3LaunchPath` = `Environment.ProcessPath`). This "handshake" is how each app learns where the other lives, surviving ClickOnce's per-version install folders.
2. If `PreferredUi` names *the other* UI, the other app's recorded path exists on disk, and `--stay` wasn't passed → launch it and exit. If the path is missing/stale, fall back to running normally (no ping-pong possible: the launched app agrees with the preference and stays up; `--stay` is the manual escape hatch).

```csharp
// WinForms Program.Main(), before Application.Run — WinUI3 App.OnLaunched mirrors this
var prefs = UiPreference.Load();
prefs.WinFormsLaunchPath = RobinLaunchPaths.SelfPath();
prefs.Save();
if (args.FirstOrDefault() != "--stay"
    && prefs.PreferredUi == "WinUI3"
    && File.Exists(prefs.WinUI3LaunchPath))
{
    Process.Start(new ProcessStartInfo(prefs.WinUI3LaunchPath) { UseShellExecute = true });
    return;
}
```

### The toggle in each UI

- **WinForms:** new `File → Use WinUI 3 interface` menu item → confirm dialog → `PreferredUi = "WinUI3"; Save();` → start other exe → `Application.Exit()`. Disabled (with tooltip) when `WinUI3LaunchPath` is unset — i.e., the WinUI app was never run on this machine.
- **WinUI 3:** `ToggleSwitch` ("Use classic interface") in a settings flyout off the title bar → `ContentDialog` confirm ("Robin will restart in the classic interface") → same write-launch-exit dance (launch `.appref-ms` via `UseShellExecute`).

An optional dedicated `RobinLauncher` exe was considered and rejected: with two different install channels (ClickOnce cache vs portable folder) there is no single stable location to install a launcher into, and the handshake file gives the same result with zero extra deployment surface.

## Phase 6 — Distribution & docs

- **WinForms:** unchanged ClickOnce (`published/`, same cert, same URLs). Bump `ApplicationRevision` as usual.
- **WinUI 3:** `RobinWinUI3/publish.ps1` modeled on ForzaRivalsMeta's:

```powershell
dotnet publish RobinWinUI3.csproj -c Release -p:Platform=x64 -r win-x64 `
    --self-contained true -o ..\..\dist\RobinWinUI3
Compress-Archive -Path ..\..\dist\RobinWinUI3\* -DestinationPath ..\..\dist\Robin-WinUI3-portable-win-x64.zip -Force
```

  Publish the zip as a GitHub Release asset. Expect ForzaRivalsMeta-like sizes (~85 MB zipped) — the price of self-contained WASDK; the alternative (smaller download, but the user must run `WindowsAppRuntimeInstall.exe` once) can be revisited.
- Optional v1.1: "Check for updates" in the WinUI app = hit `https://api.github.com/repos/albertinopadin/Robin/releases/latest`, compare tag to assembly version, open the release page in the browser.
- Update `CLAUDE.md` (project map, build commands — the solution now needs `msbuild /restore` or `nuget restore` + amd64 MSBuild for the net48 projects, while `RobinWinUI3` also builds standalone with `dotnet build -p:Platform=x64`) and `README.md`.
- Solution: add both projects to `Robin.sln`; WinUI 3 needs real `x64` solution-platform mappings (it cannot build AnyCPU), e.g. map its `Debug|Any CPU` entries to `Debug|x64` the way VS does automatically when adding the project.

---

## Phasing summary

| Phase | Deliverable | Risk gate |
|---|---|---|
| 1 | WinForms decoupled from pipeline (UiContext, notifier signature, DisplayError) — single project still | Tests green + smoke download |
| 2 | RobinCore extracted; WinForms references it | **ClickOnce test publish installs & runs** |
| 3 | Robin.Tests → RobinCore | Tests green under vstest |
| 4 | RobinWinUI3 feature-complete | Side-by-side parity test |
| 5 | UI switcher + shared settings | Switch both directions, `--stay` escape |
| 6 | publish.ps1 + docs + release | Fresh-machine install test of the zip |

Each phase is a separate commit/PR and leaves `main` shippable.

## Risks & mitigations

1. **ClickOnce manifest misses RobinCore.dll or a NuGet DLL after the split** — mitigated by keeping packages.config/Reference items untouched and by the Phase 2 test-publish gate. If anything goes sideways, the fallback is trivial: ClickOnce manifests are generated from build output references, and a `<PublishFile>` item can force-include a stray file.
2. **JsonExtensions pin (net48 only):** RobinCore pins `[1.2.0]` and the WinForms packages.config keeps 1.2.0. The WinUI/net10 app has no binding-redirect concept and is unaffected. Watch this on any future YoutubeExplode bump.
3. **VS 2026 x86 MSBuild ClickOnce crash** (known toolchain bug, per CLAUDE.md): keep building with the amd64 MSBuild. The WinUI project side-steps MSBuild entirely via `dotnet` CLI.
4. **`IsCliWrapTeardownBug` workaround:** the NotSupportedException-swallowing guard exists for the CliWrap/polyshim teardown bug *on net48*. Keep the guard in core — it is harmless on net10 (the pattern simply won't match), and core stays runtime-agnostic.
5. **`ServicePointManager` tuning in `Program.cs`** (connection limit, TLS 1.2/1.3) is net48-specific and stays in the WinForms entry point; modern .NET's SocketsHttpHandler needs neither. Nothing in core references it.
6. **Duplicate-download detection currently keys off ListView text search** (`FindItemWithText`); the WinUI VM keys off the `Downloads` collection + `_activeDownloads` dictionary instead. Subtle behavior parity to watch in the Phase 4 gate (e.g., truncated-title matching).
7. **If the WinUI app is ever MSIX-packaged later**, `%APPDATA%\Robin` writes become virtualized (files created by the packaged app are invisible to the WinForms app and deleted on uninstall). Escape hatches exist (`unvirtualizedResources` capability / `FileSystemWriteVirtualization` exclusions) — documented here so the constraint isn't rediscovered the hard way.
8. **Two distribution channels drift out of sync** (ClickOnce version vs zip version): acceptable for a personal tool; version both from the same `AssemblyVersion` where possible and show it in both UIs.

## Open questions for review

1. **net10.0 + WASDK 2.2** for the new app (my recommendation: current LTS + current stable), or exactly mirror ForzaRivalsMeta's net9.0 + WASDK 1.8 (out-of-support .NET, but byte-for-byte precedent)?
2. Is x64-only acceptable for the WinUI 3 app (ForzaRivalsMeta precedent), or do you want arm64 in the publish matrix too?
3. Default `PreferredUi` after this ships: keep `WinForms` (conservative; nothing changes for existing installs until opted in) or `WinUI3` (new UI is the face of the app)? Plan assumes `WinForms`.
4. Should the WinUI app get the optional GitHub-releases update check in v1, or ship without any update mechanism first (ForzaRivalsMeta precedent)?

---

## References

### ForzaRivalsMeta (style/structure reference)

- https://github.com/albertinopadin/ForzaRivalsMeta
- https://api.github.com/repos/albertinopadin/ForzaRivalsMeta/git/trees/main?recursive=1
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/ForzaRivalsMeta.App.csproj
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/App.xaml
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/App.xaml.cs
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/MainWindow.xaml
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/MainWindow.xaml.cs
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/MainView.xaml
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/MainView.xaml.cs
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/ViewModels.cs
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/Mvvm.cs
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/DataService.cs
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.App/Converters.cs
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/src/ForzaRivalsMeta.Core/ForzaRivalsMeta.Core.csproj
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/publish.ps1
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/CLAUDE.md
- https://raw.githubusercontent.com/albertinopadin/ForzaRivalsMeta/main/README.md

### WinUI 3 / Windows App SDK

- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/ — overview, OS minimum (Win10 1809+)
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads — WASDK 2.2.0 / 2.1.3 / 2.0.1 versions
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0 — SemVer switch
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-6 — net8 TFM examples
- https://learn.microsoft.com/en-us/windows/apps/get-started/start-here — quickstart, .NET 10 SDK templates
- https://learn.microsoft.com/en-us/windows/apps/get-started/windows-developer-faq — unpackaged support, update channels
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps — ".NET 6 or later is required", runtime installer vs self-contained
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app — WindowsPackageType=None walkthrough
- https://github.com/egvijayanand/winui-templates — community CLI template defaults (net10.0-windows10.0.26100.0)

### ClickOnce support matrix

- https://learn.microsoft.com/en-us/visualstudio/deployment/clickonce-deployment-dotnet?view=vs-2022 — .NET (Core/5+) ClickOnce differences (no ApplicationDeployment, dotnet-mage, publish profiles)
- https://learn.microsoft.com/en-us/visualstudio/deployment/publishing-clickonce-applications?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/deployment/building-dotnet-clickonce-applications-from-the-command-line?view=vs-2022
- https://github.com/microsoft/microsoft-ui-xaml/issues/5158 — ClickOnce for WinUI 3: closed, not planned
- https://github.com/dotnet/project-system/blob/main/docs/feature-comparison.md — ClickOnce publish = legacy project system only
- https://developercommunity.visualstudio.com/t/unable-to-publish-a-windows-forms-app-sdk-style-pr/1400922 — SDK-style net48 ClickOnce publish not supported

### .NET Standard / WinForms project migration

- https://learn.microsoft.com/en-us/dotnet/standard/net-standard — netstandard2.0 implementation matrix and sharing recommendation
- https://learn.microsoft.com/en-us/dotnet/desktop/winforms/migration/ — SDK-style conversion guidance, PackageReference-first

### NuGet package TFMs

- https://www.nuget.org/packages/YoutubeExplode/6.6.0 — netstandard2.0/net6/net7/net10
- https://www.nuget.org/packages/YoutubeExplode.Converter/6.6.0 — netstandard2.0/net6/net7/net10
- https://www.nuget.org/packages/FFMpegCore/5.4.0 — netstandard2.0 (unused by Robin code; being removed)
- https://www.nuget.org/packages/NLog — 6.1.4, netstandard2.0
- https://www.nuget.org/packages/CommunityToolkit.Mvvm — 8.4.2 (evaluated; not used — hand-rolled Mvvm.cs instead, per ForzaRivalsMeta convention)

### WinUI 3 controls & platform behavior

- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/itemsview — ItemsView vs ListView
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview — when NavigationView is (not) warranted
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/title-bar — TitleBar control / ExtendsContentIntoTitleBar
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.titlebar
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops — MicaBackdrop / DesktopAcrylicBackdrop
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/toggles — ToggleSwitch
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar — InfoBar for app-wide status/errors
- https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview
- https://xamlbrewer.wordpress.com/2023/10/20/migrating-from-listview-to-itemsview-in-winui-3/ — ItemsView introduction (WASDK 1.4)

### MSIX AppData virtualization (relevant only if the WinUI app is ever packaged)

- https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes
- https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization
