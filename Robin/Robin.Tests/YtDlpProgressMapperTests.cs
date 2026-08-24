using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using YoutubeDLSharp;
using YtDlpState = YoutubeDLSharp.DownloadState;

namespace Robin.Tests
{
    public class YtDlpProgressMapperTests
    {
        private static DownloadProgress P(YtDlpState state, float raw = 0)
            => new DownloadProgress(state, raw);

        [Fact]
        public void VideoPass_MapsIntoZeroToNinetyBand()
        {
            var mapper = new YtDlpProgressMapper();

            mapper.ToOverallFraction(P(YtDlpState.Downloading, 0.5f))
                .Should().BeApproximately(0.45, 0.0001);
        }

        [Fact]
        public void ProgressReset_SwitchesToAudioBand()
        {
            var mapper = new YtDlpProgressMapper();

            mapper.ToOverallFraction(P(YtDlpState.Downloading, 1.0f))
                .Should().BeApproximately(0.90, 0.0001);
            mapper.ToOverallFraction(P(YtDlpState.Downloading, 0.0f))
                .Should().BeApproximately(0.90, 0.0001, "the reset marks the start of the audio pass");
            mapper.ToOverallFraction(P(YtDlpState.Downloading, 0.5f))
                .Should().BeApproximately(0.94, 0.0001);
            mapper.ToOverallFraction(P(YtDlpState.Downloading, 1.0f))
                .Should().BeApproximately(0.98, 0.0001);
        }

        [Fact]
        public void PostProcessing_HoldsNearTheEnd()
        {
            var mapper = new YtDlpProgressMapper();

            mapper.ToOverallFraction(P(YtDlpState.PostProcessing))
                .Should().BeApproximately(0.98, 0.0001);
        }

        [Fact]
        public void Success_IsFull()
        {
            var mapper = new YtDlpProgressMapper();

            mapper.ToOverallFraction(P(YtDlpState.Success)).Should().Be(1.0);
        }

        [Fact]
        public void PreProcessing_ReturnsBandStart()
        {
            var mapper = new YtDlpProgressMapper();

            mapper.ToOverallFraction(P(YtDlpState.PreProcessing)).Should().Be(0.0);

            mapper.ToOverallFraction(P(YtDlpState.Downloading, 1.0f));
            mapper.ToOverallFraction(P(YtDlpState.Downloading, 0.0f));   // audio pass begins
            mapper.ToOverallFraction(P(YtDlpState.PreProcessing))
                .Should().BeApproximately(0.90, 0.0001);
        }

        [Fact]
        public void FullTwoPassSequence_IsMonotonic()
        {
            var mapper = new YtDlpProgressMapper();
            var sequence = new List<DownloadProgress>();
            for (int i = 0; i <= 10; i++)
                sequence.Add(P(YtDlpState.Downloading, i / 10f));        // video pass 0..1
            for (int i = 0; i <= 10; i++)
                sequence.Add(P(YtDlpState.Downloading, i / 10f));        // audio pass 0..1
            sequence.Add(P(YtDlpState.PostProcessing));                  // merge
            sequence.Add(P(YtDlpState.Success));

            double last = -1;
            foreach (var p in sequence)
            {
                double overall = mapper.ToOverallFraction(p);
                overall.Should().BeGreaterThanOrEqualTo(last, "the mapped sweep must never move backwards");
                last = overall;
            }
            last.Should().Be(1.0);
        }
    }
}
