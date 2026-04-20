using System;
using FluentAssertions;
using Xunit;

namespace Robin.Tests
{
    public class DownloadStateTests
    {
        [Fact]
        public void Constructor_InitializesCancellationTokenSource()
        {
            using var state = new DownloadState();

            state.CancellationTokenSource.Should().NotBeNull();
            state.CancellationToken.IsCancellationRequested.Should().BeFalse();
        }

        [Fact]
        public void Constructor_InitializesFlagsToFalse()
        {
            using var state = new DownloadState();

            state.IsCompleted.Should().BeFalse();
            state.IsCancelled.Should().BeFalse();
        }

        [Fact]
        public void Constructor_StartTimeIsRoughlyNow()
        {
            var before = DateTime.Now;
            using var state = new DownloadState();
            var after = DateTime.Now;

            state.StartTime.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        }

        [Fact]
        public void Cancel_ViaTokenSource_TriggersCancellationToken()
        {
            using var state = new DownloadState();

            state.CancellationTokenSource.Cancel();

            state.CancellationToken.IsCancellationRequested.Should().BeTrue();
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var state = new DownloadState();

            state.Dispose();
            Action secondDispose = () => state.Dispose();

            secondDispose.Should().NotThrow();
        }

        [Fact]
        public void Dispose_PreventsFurtherUseOfTokenSource()
        {
            var state = new DownloadState();
            var source = state.CancellationTokenSource;

            state.Dispose();

            Action callAfterDispose = () => source.Cancel();
            callAfterDispose.Should().Throw<ObjectDisposedException>();
        }
    }
}
