using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using WpfApp2.Interfaces;
using WpfApp2.Models;
using DrawingEncoder = System.Drawing.Imaging.Encoder;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using MetadataImageFormat = WpfApp2.Models.ImageFormat;

namespace WpfApp2.Services;

public class JpegExifService : IExifService
{
    private const string UserCommentKey = "UserComment";

    private static class ExifTags
    {
        public const int UserComment = 0x9286;
        public const int ImageDescription = 0x010E;
        public const int Make = 0x010F;
        public const int Model = 0x0110;
        public const int Software = 0x0131;
        public const int Artist = 0x013B;
        public const int Copyright = 0x8298;
    }

    private readonly Dictionary<string, int> _tagNameToId = new(StringComparer.OrdinalIgnoreCase)
    {
        [UserCommentKey] = ExifTags.UserComment,
        ["ImageDescription"] = ExifTags.ImageDescription,
        ["Make"] = ExifTags.Make,
        ["Model"] = ExifTags.Model,
        ["Software"] = ExifTags.Software,
        ["Artist"] = ExifTags.Artist,
        ["Copyright"] = ExifTags.Copyright
    };

    public string DefaultKey => UserCommentKey;

    public Dictionary<string, string> ExtractMetadata(string imagePath)
    {
        var metadata = new Dictionary<string, string>();

        try
        {
            using var image = Image.FromFile(imagePath);
            foreach (var propertyItem in image.PropertyItems)
            {
                try
                {
                    metadata[$"{GetTagName(propertyItem.Id)} (0x{propertyItem.Id:X})"] =
                        ExtractPropertyValue(propertyItem);
                }
                catch
                {
                    // Skip unreadable tags and keep the rest visible.
                }
            }
        }
        catch (Exception ex)
        {
            metadata["Error"] = $"Ошибка чтения JPEG metadata: {ex.Message}";
        }

        return metadata;
    }

    public bool WriteHiddenData(string imagePath, byte[] data, string key)
    {
        string backupPath = imagePath + ".backup";
        string tempOutputPath = imagePath + ".tmp.jpg";

        try
        {
            File.Copy(imagePath, backupPath, true);

            try
            {
                using (var image = Image.FromFile(imagePath))
                {
                    int tagId = GetExifTagId(key);
                    var propertyItem = GetOrCreatePropertyItem(image, tagId);

                    propertyItem.Id = tagId;
                    propertyItem.Type = 7;
                    propertyItem.Value = data;
                    propertyItem.Len = data.Length;

                    image.SetPropertyItem(propertyItem);

                    // GDI+ is unreliable when saving the loaded JPEG back to the same path.
                    SaveJpegWithQuality(image, tempOutputPath);
                }

                File.Copy(tempOutputPath, imagePath, true);
                File.Delete(tempOutputPath);
                File.Delete(backupPath);
                return true;
            }
            catch
            {
                if (File.Exists(tempOutputPath))
                    File.Delete(tempOutputPath);

                RestoreBackup(backupPath, imagePath);
                return false;
            }
        }
        catch
        {
            if (File.Exists(tempOutputPath))
                File.Delete(tempOutputPath);

            if (File.Exists(backupPath))
                RestoreBackup(backupPath, imagePath);

            return false;
        }
    }

    public byte[]? ReadHiddenData(string imagePath, string key)
    {
        try
        {
            using var image = Image.FromFile(imagePath);
            int tagId = GetExifTagId(key);
            var propertyItem = image.GetPropertyItem(tagId);
            if (propertyItem is null)
                return null;

            var value = propertyItem.Value;
            return value is { Length: > 0 } ? value : null;
        }
        catch
        {
            return null;
        }
    }

    public bool ClearHiddenData(string imagePath, string key)
    {
        return WriteHiddenData(imagePath, Array.Empty<byte>(), key);
    }

    public bool SupportsFormat(MetadataImageFormat format) => format == MetadataImageFormat.Jpeg;

    public bool HasHiddenData(string imagePath, string key)
    {
        var data = ReadHiddenData(imagePath, key);
        return data is { Length: > 0 };
    }

    private static void RestoreBackup(string backupPath, string imagePath)
    {
        if (!File.Exists(backupPath))
            return;

        File.Copy(backupPath, imagePath, true);
        File.Delete(backupPath);
    }

    private string ExtractPropertyValue(PropertyItem item)
    {
        var value = item.Value ?? Array.Empty<byte>();
        return item.Type switch
        {
            1 or 7 => FormatByteValue(value),
            2 => Encoding.ASCII.GetString(value).TrimEnd('\0'),
            3 => string.Join(", ", ToUShortArray(value, item.Len)),
            4 => string.Join(", ", ToUIntArray(value, item.Len)),
            5 => FormatRational(value, signed: false),
            10 => FormatRational(value, signed: true),
            _ => $"Type {item.Type}: {item.Len} bytes"
        };
    }

    private static string FormatByteValue(byte[] value)
    {
        if (value.Length == 0)
            return string.Empty;

        if (value.All(b => b is 0 or >= 32 and <= 126))
            return Encoding.UTF8.GetString(value).TrimEnd('\0');

        return BitConverter.ToString(value).Replace("-", " ");
    }

    private static ushort[] ToUShortArray(byte[] value, int length)
    {
        var result = new ushort[length / 2];
        Buffer.BlockCopy(value, 0, result, 0, length);
        return result;
    }

    private static uint[] ToUIntArray(byte[] value, int length)
    {
        var result = new uint[length / 4];
        Buffer.BlockCopy(value, 0, result, 0, length);
        return result;
    }

    private static string FormatRational(byte[] value, bool signed)
    {
        if (value.Length < 8)
            return "Invalid";

        if (signed)
        {
            int numerator = BitConverter.ToInt32(value, 0);
            int denominator = BitConverter.ToInt32(value, 4);
            return denominator == 0
                ? "∞"
                : (numerator / (double)denominator).ToString("F3", CultureInfo.InvariantCulture);
        }

        uint numeratorUnsigned = BitConverter.ToUInt32(value, 0);
        uint denominatorUnsigned = BitConverter.ToUInt32(value, 4);
        return denominatorUnsigned == 0
            ? "∞"
            : (numeratorUnsigned / (double)denominatorUnsigned).ToString("F3", CultureInfo.InvariantCulture);
    }

    private string GetTagName(int tagId)
    {
        return _tagNameToId.FirstOrDefault(pair => pair.Value == tagId).Key ?? $"Tag_{tagId:X}";
    }

    private int GetExifTagId(string tagName)
    {
        if (_tagNameToId.TryGetValue(tagName, out int tagId))
            return tagId;

        if (tagName.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(tagName[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedId))
        {
            return parsedId;
        }

        return ExifTags.UserComment;
    }

    private static PropertyItem GetOrCreatePropertyItem(Image image, int tagId)
    {
        try
        {
            return image.GetPropertyItem(tagId) ?? CreateNewPropertyItem();
        }
        catch (ArgumentException)
        {
            return CreateNewPropertyItem();
        }
    }

    private static PropertyItem CreateNewPropertyItem()
    {
        var constructor = typeof(PropertyItem).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (constructor is null)
            throw new InvalidOperationException("Не удалось создать JPEG PropertyItem.");

        return (PropertyItem)constructor.Invoke(null);
    }

    private static void SaveJpegWithQuality(Image image, string path, long quality = 95L)
    {
        var encoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.FormatID == DrawingImageFormat.Jpeg.Guid);

        if (encoder is null)
        {
            image.Save(path, DrawingImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(DrawingEncoder.Quality, quality);
        image.Save(path, encoder, parameters);
    }
}
