using System.Text;
using HooviePack.Api.Configuration;
using HooviePack.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;

namespace HooviePack.Api.Tests;

public sealed class ImageStorageSecurityTests
{
    [Fact]
    public async Task Stored_jpeg_is_reencoded_without_exif_or_gps_metadata()
    {
        var rootPath = TempDirectory();
        try
        {
            await using var input = new MemoryStream();
            using (var image = new Image<Rgba32>(64, 48, new Rgba32(40, 80, 120)))
            {
                var exif = new ExifProfile();
                exif.SetValue(ExifTag.ImageDescription, "private-image-description");
                exif.SetValue(ExifTag.GPSLatitudeRef, "N");
                exif.SetValue(ExifTag.GPSLongitudeRef, "W");
                exif.SetValue(ExifTag.GPSLatitude,
                    [new Rational(40), new Rational(42), new Rational(46)]);
                exif.SetValue(ExifTag.GPSLongitude,
                    [new Rational(74), new Rational(0), new Rational(21)]);
                image.Metadata.ExifProfile = exif;
                var iptc = new IptcProfile();
                iptc.SetValue(IptcTag.Caption, "private-iptc-caption", strict: false);
                image.Metadata.IptcProfile = iptc;
                image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(
                    "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">private-xmp-value</x:xmpmeta>"));
                await image.SaveAsync(input, new JpegEncoder { Quality = 90 });
            }

            input.Position = 0;
            using (var original = await Image.LoadAsync(input))
            {
                Assert.NotNull(original.Metadata.ExifProfile);
                Assert.True(original.Metadata.ExifProfile.Values.Count >= 5);
                Assert.NotNull(original.Metadata.IptcProfile);
                Assert.NotNull(original.Metadata.XmpProfile);
            }

            input.Position = 0;
            var storage = CreateStorage(rootPath);
            var stored = await storage.StoreImageAsync(
                input,
                "../../client-supplied.png",
                "avatars");

            Assert.Equal("image/jpeg", stored.ContentType);
            Assert.EndsWith(".jpg", stored.StoragePath, StringComparison.Ordinal);
            Assert.DoesNotContain("client-supplied", stored.StoragePath, StringComparison.Ordinal);
            Assert.True(Guid.TryParseExact(
                Path.GetFileNameWithoutExtension(stored.StoragePath),
                "N",
                out _));

            var opened = Assert.IsType<StoredFile>(await storage.OpenReadAsync(
                stored.StoragePath,
                stored.ContentType));
            await using (opened.Stream)
            using (var persisted = await Image.LoadAsync(opened.Stream))
            {
                Assert.Equal("JPEG", persisted.Metadata.DecodedImageFormat?.Name);
                Assert.Null(persisted.Metadata.ExifProfile);
                Assert.Null(persisted.Metadata.IccProfile);
                Assert.Null(persisted.Metadata.IptcProfile);
                Assert.Null(persisted.Metadata.XmpProfile);
                Assert.Null(persisted.Metadata.CicpProfile);
                Assert.Equal(64, persisted.Width);
                Assert.Equal(48, persisted.Height);
                Assert.Single(persisted.Frames);
            }
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Theory]
    [InlineData("jpeg", ".jpg", "image/jpeg", "JPEG")]
    [InlineData("png", ".png", "image/png", "PNG")]
    [InlineData("webp", ".webp", "image/webp", "Webp")]
    public async Task Storage_reencodes_each_supported_format_without_changing_its_type(
        string format,
        string expectedExtension,
        string expectedContentType,
        string expectedFormatName)
    {
        var rootPath = TempDirectory();
        try
        {
            await using var input = new MemoryStream();
            using (var image = new Image<Rgba32>(24, 16, new Rgba32(20, 40, 60, 180)))
            {
                switch (format)
                {
                    case "jpeg":
                        await image.SaveAsJpegAsync(input);
                        break;
                    case "png":
                        await image.SaveAsPngAsync(input);
                        break;
                    case "webp":
                        await image.SaveAsWebpAsync(input);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported test format.");
                }
            }

            input.Position = 0;
            var storage = CreateStorage(rootPath);
            var stored = await storage.StoreImageAsync(input, "upload.bin", "posts");

            Assert.Equal(expectedContentType, stored.ContentType);
            Assert.EndsWith(expectedExtension, stored.StoragePath, StringComparison.Ordinal);

            var opened = Assert.IsType<StoredFile>(await storage.OpenReadAsync(
                stored.StoragePath,
                stored.ContentType));
            await using (opened.Stream)
            using (var persisted = await Image.LoadAsync(opened.Stream))
            {
                Assert.Equal(expectedFormatName, persisted.Metadata.DecodedImageFormat?.Name);
                Assert.Equal(24, persisted.Width);
                Assert.Equal(16, persisted.Height);
                Assert.Single(persisted.Frames);
            }
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Storage_rejects_animated_webp()
    {
        var rootPath = TempDirectory();
        try
        {
            await using var input = new MemoryStream();
            using (var image = new Image<Rgba32>(12, 8, Color.Red))
            using (var secondFrame = new Image<Rgba32>(12, 8, Color.Blue))
            {
                image.Frames.AddFrame(secondFrame.Frames.RootFrame);
                await image.SaveAsWebpAsync(input);
            }

            input.Position = 0;
            var storage = CreateStorage(rootPath);

            await Assert.ThrowsAsync<InvalidMediaException>(() =>
                storage.StoreImageAsync(input, "animated.webp", "posts"));
            Assert.Empty(Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Storage_rejects_input_larger_than_the_configured_byte_limit()
    {
        var rootPath = TempDirectory();
        try
        {
            await using var input = new MemoryStream(new byte[65]);
            var storage = CreateStorage(rootPath, maxImageBytes: 64);

            await Assert.ThrowsAsync<InvalidMediaException>(() =>
                storage.StoreImageAsync(input, "oversized.png", "posts"));
            Assert.Empty(Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Storage_does_not_persist_a_reencoded_image_over_the_byte_limit()
    {
        var rootPath = TempDirectory();
        try
        {
            await using var input = new MemoryStream();
            using (var image = new Image<Rgba32>(128, 128))
            {
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        image[x, y] = new Rgba32(
                            (byte)((x * 73 + y * 17) % 256),
                            (byte)((x * 29 + y * 97) % 256),
                            (byte)((x * 151 + y * 41) % 256));
                    }
                }

                await image.SaveAsync(input, new JpegEncoder { Quality = 1 });
            }

            var inputLength = input.Length;
            input.Position = 0;
            var storage = CreateStorage(rootPath, inputLength);

            var exception = await Assert.ThrowsAsync<InvalidMediaException>(() =>
                storage.StoreImageAsync(input, "small-input.jpg", "posts"));

            Assert.Contains("size limit", exception.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static LocalFileStorage CreateStorage(string rootPath, long maxImageBytes = 10 * 1024 * 1024) =>
        new(
            Options.Create(new MediaStorageOptions
            {
                RootPath = rootPath,
                MaxImageBytes = maxImageBytes
            }),
            new TestWebHostEnvironment(),
            NullLogger<LocalFileStorage>.Instance);

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hooviepack-image-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HooviePack.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
