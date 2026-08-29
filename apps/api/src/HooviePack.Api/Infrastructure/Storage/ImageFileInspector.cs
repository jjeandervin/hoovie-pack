using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using ImageSharpConfiguration = SixLabors.ImageSharp.Configuration;

namespace HooviePack.Api.Infrastructure.Storage;

public sealed record ImageFileMetadata(string Extension, string ContentType, int Width, int Height);

public static class ImageFileInspector
{
    private const int MaxDimension = 12_000;
    private const long MaxPixels = 40_000_000;

    private static readonly ImageSharpConfiguration DecoderConfiguration = CreateDecoderConfiguration();

    public static async Task<ImageFileMetadata> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (decoded, metadata) = await DecodeValidatedAsync(path, cancellationToken);
            using (decoded)
            {
                return metadata;
            }
        }
        catch (Exception exception) when (ShouldNormalize(exception))
        {
            throw new InvalidMediaException("The upload is not a complete, valid JPEG, PNG, or WebP image.");
        }
    }

    public static async Task<ImageFileMetadata> ReencodeWithoutMetadataAsync(
        string sourcePath,
        string destinationPath,
        long maxOutputBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputBytes);

        try
        {
            var (decoded, metadata) = await DecodeValidatedAsync(sourcePath, cancellationToken);
            using (decoded)
            {
                await using var output = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var limitedOutput = new SizeLimitedWriteStream(output, maxOutputBytes);
                await decoded.SaveAsync(limitedOutput, CreateEncoder(metadata.Extension), cancellationToken);
            }

            return metadata;
        }
        catch (Exception exception) when (ShouldNormalize(exception))
        {
            throw new InvalidMediaException("The upload is not a complete, valid JPEG, PNG, or WebP image.");
        }
    }

    private static async Task<(Image Image, ImageFileMetadata Metadata)> DecodeValidatedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var identifyOptions = CreateDecoderOptions();
        var info = await Image.IdentifyAsync(identifyOptions, path, cancellationToken);
        var (extension, contentType) = GetSupportedFormat(info.Metadata.DecodedImageFormat);
        ValidateDimensions(info.Width, info.Height);

        if (info.FrameMetadataCollection.Count > 1)
        {
            throw new InvalidMediaException("Animated images are not supported.");
        }

        var decoded = await Image.LoadAsync(CreateDecoderOptions(), path, cancellationToken);
        try
        {
            if (decoded.Width != info.Width || decoded.Height != info.Height || decoded.Frames.Count != 1)
            {
                throw new InvalidMediaException("The image structure is inconsistent or animated.");
            }

            var decodedFormat = GetSupportedFormat(decoded.Metadata.DecodedImageFormat);
            if (decodedFormat.Extension != extension)
            {
                throw new InvalidMediaException("The image structure is inconsistent.");
            }

            return (decoded, new ImageFileMetadata(extension, contentType, info.Width, info.Height));
        }
        catch
        {
            decoded.Dispose();
            throw;
        }
    }

    private static DecoderOptions CreateDecoderOptions() => new()
    {
        Configuration = DecoderConfiguration,
        SkipMetadata = true,
        MaxFrames = 2
    };

    private static IImageEncoder CreateEncoder(string extension)
    {
        return extension switch
        {
            ".jpg" => new JpegEncoder { Quality = 85, SkipMetadata = true },
            ".png" => new PngEncoder { SkipMetadata = true },
            ".webp" => new WebpEncoder { Quality = 85, SkipMetadata = true },
            _ => throw new InvalidMediaException("Only JPEG, PNG, and WebP images are accepted.")
        };
    }

    private static bool ShouldNormalize(Exception exception) => exception is
            ImageFormatException or
            InvalidImageContentException or
            UnknownImageFormatException or
            NotSupportedException or
            ArgumentException or
            OverflowException or
            InvalidMemoryOperationException;

    private static (string Extension, string ContentType) GetSupportedFormat(IImageFormat? format)
    {
        return format?.Name.ToUpperInvariant() switch
        {
            "JPEG" => (".jpg", "image/jpeg"),
            "PNG" => (".png", "image/png"),
            "WEBP" => (".webp", "image/webp"),
            _ => throw new InvalidMediaException("Only JPEG, PNG, and WebP images are accepted.")
        };
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
        {
            throw new InvalidMediaException("The image dimensions are not supported.");
        }
    }

    private static ImageSharpConfiguration CreateDecoderConfiguration()
    {
        var configuration = ImageSharpConfiguration.Default.Clone();
        configuration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            MaximumPoolSizeMegabytes = 64,
            AllocationLimitMegabytes = 256
        });
        return configuration;
    }

    private sealed class SizeLimitedWriteStream(Stream inner, long maxLength) : Stream
    {
        private long _bytesWritten;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _bytesWritten;

        public override long Position
        {
            get => _bytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            inner.Write(buffer, offset, count);
            _bytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            inner.Write(buffer);
            _bytesWritten += buffer.Length;
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureCapacity(count);
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            _bytesWritten += count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken);
            _bytesWritten += buffer.Length;
        }

        private void EnsureCapacity(int count)
        {
            if (_bytesWritten > maxLength - count)
            {
                throw new InvalidMediaException("The processed image exceeds the configured size limit.");
            }
        }
    }
}
