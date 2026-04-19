using ExifTool.Core.Models;

namespace ExifTool.Core.Services;

public interface IImageValidator
{
    bool IsValid(string filePath, out ImageFormat format);
    bool IsValid(Stream stream, string fileName, out ImageFormat format);
}

public class ImageValidator : IImageValidator
{
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic  = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public bool IsValid(string filePath, out ImageFormat format)
    {
        format = ImageFormat.Unknown;
        if (!File.Exists(filePath)) return false;

        using var stream = File.OpenRead(filePath);
        var ext = Path.GetExtension(filePath);
        return IsValid(stream, ext, out format);
    }

    public bool IsValid(Stream stream, string fileName, out ImageFormat format)
    {
        format = ImageFormat.Unknown;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png")) return false;

        Span<byte> header = stackalloc byte[8];
        int read = stream.Read(header);
        stream.Position = 0;

        if (read >= 3 && header[..3].SequenceEqual(JpegMagic))
        {
            format = ImageFormat.Jpeg;
            return true;
        }
        if (read >= 8 && header.SequenceEqual(PngMagic))
        {
            format = ImageFormat.Png;
            return true;
        }
        return false;
    }
}
