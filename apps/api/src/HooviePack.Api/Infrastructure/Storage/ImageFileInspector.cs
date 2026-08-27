using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
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
            var identifyOptions = new DecoderOptions
            {
                Configuration = DecoderConfiguration,
                SkipMetadata = true,
                MaxFrames = 2
            };
            var info = await Image.IdentifyAsync(identifyOptions, path, cancellationToken);
            var format = info.Metadata.DecodedImageFormat;
            var (extension, contentType) = GetSupportedFormat(format);
            ValidateDimensions(info.Width, info.Height);

            if (info.FrameMetadataCollection.Count > 1)
            {
                throw new InvalidMediaException("Animated images are not supported.");
            }

            var decodeOptions = new DecoderOptions
            {
                Configuration = DecoderConfiguration,
                SkipMetadata = true,
                MaxFrames = 2
            };
            using var decoded = await Image.LoadAsync(decodeOptions, path, cancellationToken);
            if (decoded.Width != info.Width || decoded.Height != info.Height || decoded.Frames.Count != 1)
            {
                throw new InvalidMediaException("The image structure is inconsistent or animated.");
            }

            _ = GetSupportedFormat(decoded.Metadata.DecodedImageFormat);
            return new ImageFileMetadata(extension, contentType, info.Width, info.Height);
        }
        catch (InvalidMediaException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ImageFormatException or
            InvalidImageContentException or
            UnknownImageFormatException or
            NotSupportedException or
            ArgumentException or
            OverflowException or
            InvalidMemoryOperationException)
        {
            throw new InvalidMediaException("The upload is not a complete, valid JPEG, PNG, or WebP image.");
        }
    }

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
}
