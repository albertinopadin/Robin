using System;
using System.Collections.Generic;
using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Robin.Tests.Fakes
{
    internal static class YoutubeTestData
    {
        public const long OneMegabyte = 1024 * 1024;

        public static Video CreateVideo(string title = "Test Video")
            => new Video(
                VideoId.Parse("dQw4w9WgXcQ"),
                title,
                new Author(ChannelId.Parse("UCuAXFkgsw1L7xaCfnd5JJOw"), "Test Channel"),
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                "Test description",
                TimeSpan.FromMinutes(3),
                new List<Thumbnail>(),
                new List<string>(),
                new Engagement(0, 0, 0));

        public static VideoOnlyStreamInfo VideoStream(Container container, int height, long sizeBytes = 90 * OneMegabyte)
            => new VideoOnlyStreamInfo(
                "https://stream.test/video-" + container.Name + "-" + height,
                container,
                new FileSize(sizeBytes),
                new Bitrate(1_000_000),
                "avc1",
                new VideoQuality(height, 30),
                new Resolution(height * 16 / 9, height));

        public static AudioOnlyStreamInfo AudioStream(Container container, long bitrateBps = 128_000, long sizeBytes = 10 * OneMegabyte)
            => new AudioOnlyStreamInfo(
                "https://stream.test/audio-" + container.Name + "-" + bitrateBps,
                container,
                new FileSize(sizeBytes),
                new Bitrate(bitrateBps),
                "mp4a",
                null,
                null);

        public static StreamManifest Manifest(params IStreamInfo[] streams)
            => new StreamManifest(streams);
    }
}
