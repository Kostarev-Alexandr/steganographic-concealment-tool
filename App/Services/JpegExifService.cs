using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using WpfApp2.Interfaces;

namespace WpfApp2.Services
{
    public class JpegExifService : IExifService
    {
        //EXIF теги для стеганографии
        private static class ExifTags
        {
            public const int UserComment = 0x9286;  //будем брать для текста
            public const int ImageDescription = 0x010E;
            public const int Make = 0x010F;
            public const int Model = 0x0110;
            public const int Software = 0x0131;
            public const int Artist = 0x013B;
            public const int Copyright = 0x8298;
        }

        private readonly Dictionary<string, int> _tagNameToId = new()//типа быстрый доступ к тегам
        {
            ["UserComment"] = ExifTags.UserComment,
            ["ImageDescription"] = ExifTags.ImageDescription,
            ["Make"] = ExifTags.Make,
            ["Model"] = ExifTags.Model,
            ["Software"] = ExifTags.Software,
            ["Artist"] = ExifTags.Artist,
            ["Copyright"] = ExifTags.Copyright
        };

        public Dictionary<string, string> ExtractExifData(string imagePath)
        {
            var exifData = new Dictionary<string, string>();
            try
            {
                using (var image = Image.FromFile(imagePath))
                {
                    foreach (var propertyItem in image.PropertyItems)
                    {
                        try
                        {
                            var value = ExtractPropertyValue(propertyItem);
                            var tagName = GetTagName(propertyItem.Id);
                            exifData[$"{tagName} (0x{propertyItem.Id:X})"] = value;
                        }
                        catch
                        {
                            // Пропускаем проблемные теги
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                exifData["Error"] = $"Ошибка чтения EXIF: {ex.Message}";
            }

            return exifData;
        }

        public bool WriteHiddenData(string imagePath, byte[] data, string exifTag = "UserComment")
        {
            try
            {
                if (!SupportsExif(Path.GetExtension(imagePath)))
                    return false;

                // бекапчик шобы не убить фотку
                var backupPath = imagePath + ".backup";
                File.Copy(imagePath, backupPath, true);

                try
                {
                    using (var image = Image.FromFile(imagePath))
                    {
                        int tagId = GetExifTagId(exifTag);

                        // Создаем или получаем существующий PropertyItem
                        var propertyItem = GetOrCreatePropertyItem(image, tagId);

                        // Подготавливаем данные
                        propertyItem.Id = tagId;
                        propertyItem.Type = 7; // Undefined (для бинарных данных)
                        propertyItem.Value = data;
                        propertyItem.Len = data.Length;

                        image.SetPropertyItem(propertyItem);

                        // Сохраняем с сохранением качества
                        SaveJpegWithQuality(image, imagePath);
                    }

                    // Удаляем резервную копию при успехе
                    File.Delete(backupPath);
                    return true;
                }
                catch
                {
                    // Восстанавливаем из резервной копии
                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, imagePath, true);
                        File.Delete(backupPath);
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public byte[]? ReadHiddenData(string imagePath, string exifTag = "UserComment")
        {
            try
            {
                using (var image = Image.FromFile(imagePath))
                {
                    int tagId = GetExifTagId(exifTag);

                    try
                    {
                        var propertyItem = image.GetPropertyItem(tagId);
                        return propertyItem.Value;
                    }
                    catch (ArgumentException)
                    {
                        // нету(
                        return null;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public bool HasHiddenData(string imagePath, string exifTag = "UserComment")
        {
            var data = ReadHiddenData(imagePath, exifTag);
            return data != null && data.Length > 0;
        }

        public bool ClearExifData(string imagePath)
        {
            try
            {
                // Загружаем изображение
                using (var image = Image.FromFile(imagePath))
                {
                    // Создаем новый Bitmap без EXIF данных
                    using (var bitmap = new Bitmap(image.Width, image.Height))
                    {
                        using (var g = Graphics.FromImage(bitmap))
                        {
                            g.DrawImage(image, 0, 0, image.Width, image.Height);
                        }

                        // Сохраняем БЕЗ EXIF
                        SaveJpegWithQuality(bitmap, imagePath);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SupportsExif(string fileExtension)
        {
            var supported = new[] { ".jpg", ".jpeg", ".jfif", ".jpe" };
            return supported.Contains(fileExtension.ToLower());
        }

        private string ExtractPropertyValue(PropertyItem item)
        {
            try
            {
                switch (item.Type)
                {
                    case 1: // Byte
                    case 7: // Undefined
                        return BitConverter.ToString(item.Value).Replace("-", " ");

                    case 2: // ASCII string
                        return Encoding.ASCII.GetString(item.Value).TrimEnd('\0');

                    case 3: // Short (16-bit unsigned)
                        var shorts = new ushort[item.Len / 2];
                        Buffer.BlockCopy(item.Value, 0, shorts, 0, item.Len);
                        return string.Join(", ", shorts);

                    case 4: // Long (32-bit unsigned)
                        var longs = new uint[item.Len / 4];
                        Buffer.BlockCopy(item.Value, 0, longs, 0, item.Len);
                        return string.Join(", ", longs);

                    case 5: // Rational (two LONGs)
                        if (item.Len >= 8)
                        {
                            var num = BitConverter.ToUInt32(item.Value, 0);
                            var den = BitConverter.ToUInt32(item.Value, 4);
                            return den != 0 ? $"{(double)num / den:F3}" : "∞";
                        }
                        return "Invalid";

                    case 10: // SRational (two SLONGs)
                        if (item.Len >= 8)
                        {
                            var num = BitConverter.ToInt32(item.Value, 0);
                            var den = BitConverter.ToInt32(item.Value, 4);
                            return den != 0 ? $"{(double)num / den:F3}" : "∞";
                        }
                        return "Invalid";

                    default:
                        return $"Type {item.Type}: {item.Len} bytes";
                }
            }
            catch
            {
                return "[Ошибка чтения]";
            }
        }

        private string GetTagName(int tagId)
        {
            return _tagNameToId.FirstOrDefault(x => x.Value == tagId).Key
                   ?? $"Tag_{tagId:X}";
        }

        private int GetExifTagId(string tagName)
        {
            if (_tagNameToId.TryGetValue(tagName, out int tagId))
                return tagId;

            // Если тег не найден в словаре, пробуем распарсить как hex
            if (tagName.StartsWith("0x") && int.TryParse(tagName[2..],
                System.Globalization.NumberStyles.HexNumber, null, out int parsedId))
                return parsedId;

            // По умолчанию используем UserComment
            return ExifTags.UserComment;
        }

        private PropertyItem GetOrCreatePropertyItem(Image image, int tagId)
        {
            try
            {
                // Пробуем получить существующий
                return image.GetPropertyItem(tagId);
            }
            catch (ArgumentException)
            {
                // Создаем новый через рефлексию
                return CreateNewPropertyItem();
            }
        }

        private PropertyItem CreateNewPropertyItem()
        {
            // Создание PropertyItem через рефлексию
            // Стандартного публичного конструктора нет
            var constructor = typeof(PropertyItem)
                .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);

            if (constructor != null)
                return (PropertyItem)constructor.Invoke(null);

            // Fallback: используем существующий PropertyItem как шаблон
            using (var tempImage = new Bitmap(1, 1))
            {
                // Сохраняем и загружаем чтобы получить PropertyItem
                using (var ms = new MemoryStream())
                {
                    tempImage.Save(ms, ImageFormat.Jpeg);
                    ms.Position = 0;
                    using (var img = Image.FromStream(ms))
                    {
                        if (img.PropertyItems.Length > 0)
                        {
                            var template = img.PropertyItems[0];
                            template.Id = 0;
                            template.Type = 0;
                            template.Value = Array.Empty<byte>();
                            template.Len = 0;
                            return template;
                        }
                    }
                }
            }

            throw new InvalidOperationException("Не удалось создать PropertyItem");
        }

        private void SaveJpegWithQuality(Image image, string path, long quality = 95L)
        {
            var encoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

            if (encoder != null)
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, quality);

                image.Save(path, encoder, encoderParams);
            }
            else
            {
                image.Save(path, ImageFormat.Jpeg);
            }
        }
    }
}