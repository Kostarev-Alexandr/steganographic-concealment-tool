using System.Collections.Generic;

namespace WpfApp2.Interfaces
{
    public interface IExifService
    {
        /// <summary>
        /// Извлечь все EXIF-данные из изображения
        /// </summary>
        Dictionary<string, string> ExtractExifData(string imagePath);

        /// <summary>
        /// Записать скрытые данные в EXIF
        /// </summary>
        bool WriteHiddenData(string imagePath, byte[] data, string exifTag = "UserComment");

        /// <summary>
        /// Прочитать скрытые данные из EXIF
        /// </summary>
        byte[]? ReadHiddenData(string imagePath, string exifTag = "UserComment");

        /// <summary>
        /// Очистить все EXIF-данные
        /// </summary>
        bool ClearExifData(string imagePath);

        /// <summary>
        /// Проверить, поддерживает ли файл EXIF
        /// </summary>
        bool SupportsExif(string fileExtension);

        /// <summary>
        /// Проверить, содержит ли изображение скрытые данные
        /// </summary>
        bool HasHiddenData(string imagePath, string exifTag = "UserComment");
    }
}