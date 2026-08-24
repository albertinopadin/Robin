using System;
using System.Threading.Tasks;
using FluentAssertions;
using Robin.Tests.Fakes;
using Xunit;

namespace Robin.Tests
{
    public class YtDlpBootstrapTests
    {
        private readonly FakeYtDlpSetupUi ui = new FakeYtDlpSetupUi();
        private readonly FakeYtDlpPathResolver resolver = new FakeYtDlpPathResolver();
        private int selfUpdateCalls;

        private Task<YtDlpSetupResult> RunAsync(bool selfUpdateThrows = false)
            => YtDlpBootstrap.EnsureReadyAsync(ui, resolver, selfUpdate: () =>
            {
                selfUpdateCalls++;
                return selfUpdateThrows
                    ? Task.FromException(new InvalidOperationException("yt-dlp -U failed (fake)"))
                    : Task.CompletedTask;
            });

        [Fact]
        public async Task FirstRun_Declined_ReturnsDeclinedWithoutDownloading()
        {
            resolver.Installed = false;
            ui.ConfirmAnswer = false;

            var result = await RunAsync();

            result.Should().Be(YtDlpSetupResult.Declined);
            ui.ConfirmCalls.Should().Be(1);
            ui.ProgressRuns.Should().BeEmpty();
            resolver.YtDlpDownloads.Should().Be(0);
            resolver.DenoDownloads.Should().Be(0);
            selfUpdateCalls.Should().Be(0);
        }

        [Fact]
        public async Task FirstRun_Declined_NextRunAsksAgain()
        {
            resolver.Installed = false;
            ui.ConfirmAnswer = false;

            await RunAsync();
            await RunAsync();

            ui.ConfirmCalls.Should().Be(2, "declining stamps nothing, so the question comes back");
        }

        [Fact]
        public async Task FirstRun_Accepted_DownloadsBothBinariesBehindDeterminateProgressPopup()
        {
            resolver.Installed = false;
            ui.ConfirmAnswer = true;

            var result = await RunAsync();

            result.Should().Be(YtDlpSetupResult.Ready);
            ui.ConfirmCalls.Should().Be(1);
            var run = ui.ProgressRuns.Should().ContainSingle().Subject;
            run.Title.Should().Be("Robin — setting up yt-dlp");
            run.JobNames.Should().Equal("yt-dlp", "Deno (JS runtime)");
            run.Marquee.Should().BeFalse("binary downloads report real percentages");
            resolver.YtDlpDownloads.Should().Be(1);
            resolver.DenoDownloads.Should().Be(1);
            selfUpdateCalls.Should().Be(0, "a freshly downloaded binary needs no same-day -U check");
        }

        [Fact]
        public async Task FirstRun_DownloadThrows_ReturnsFailed()
        {
            resolver.Installed = false;
            resolver.ThrowOnYtDlpDownload = true;

            var result = await RunAsync();

            result.Should().Be(YtDlpSetupResult.Failed);
            resolver.DenoDownloads.Should().Be(0, "jobs run sequentially, so Deno never started");
        }

        [Fact]
        public async Task Installed_UpdateDue_RunsSelfUpdateBehindMarqueePopup()
        {
            resolver.Installed = true;
            resolver.NeedsUpdate = true;

            var result = await RunAsync();

            result.Should().Be(YtDlpSetupResult.Ready);
            ui.ConfirmCalls.Should().Be(0);
            var run = ui.ProgressRuns.Should().ContainSingle().Subject;
            run.Title.Should().Be("Robin — updating yt-dlp");
            run.JobNames.Should().Equal("yt-dlp");
            run.Marquee.Should().BeTrue("yt-dlp -U exposes no percentage");
            selfUpdateCalls.Should().Be(1);
            resolver.MarkUpdatedCalls.Should().Be(1);
        }

        [Fact]
        public async Task Installed_SelfUpdateThrows_IsNonFatalAndStillStamps()
        {
            resolver.Installed = true;
            resolver.NeedsUpdate = true;

            var result = await RunAsync(selfUpdateThrows: true);

            result.Should().Be(YtDlpSetupResult.Ready, "the cached binary still works");
            resolver.MarkUpdatedCalls.Should().Be(1, "a failed -U must not retry on every launch");
        }

        [Fact]
        public async Task Installed_NoUpdateDue_IsSilent()
        {
            resolver.Installed = true;
            resolver.NeedsUpdate = false;

            var result = await RunAsync();

            result.Should().Be(YtDlpSetupResult.Ready);
            ui.ConfirmCalls.Should().Be(0);
            ui.ProgressRuns.Should().BeEmpty();
            selfUpdateCalls.Should().Be(0);
        }
    }

    public class EngineSettingsTests
    {
        [Theory]
        [InlineData(EngineSettings.YtDlp, "yt-dlp")]
        [InlineData(EngineSettings.Explode, "YouTube Explode")]
        public void DisplayName_SpellsTheEngineForHumans(string engine, string expected)
        {
            EngineSettings.DisplayName(engine).Should().Be(expected);
        }
    }
}
