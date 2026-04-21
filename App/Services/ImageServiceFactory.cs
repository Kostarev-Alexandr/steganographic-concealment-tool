using System;
using System.IO;
using WpfApp2.Interfaces;
using WpfApp2.Services;
using ExifTool.Core.Models;

namespace WpfApp2.Services
{
    public class ImageServiceFactory : IImageServiceFactory
    {
        public IExifService GetExifService(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();

            if (extension == ".jpg" || extension == ".jpeg" || extension == ".jfif")
                return new JpegExifService();

            throw new NotSupportedException($"EXIF не поддерживается для {extension}");
        }

        public ImageFormat DetectImageFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();

            return extension switch
            {
                ".jpg" or ".jpeg" or ".jfif" => ImageFormat.Jpeg,
                ".png" => ImageFormat.Png,
                _ => ImageFormat.Unknown
            };
        }

        /*ПНГШКА ПАТОМ СДЕЛАЮ
        public IPngMetadataService GetPngService(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            
            if (extension == ".png")
                return new PngMetadataService();
            
            throw new NotSupportedException($"PNG metadata не поддерживается для {extension}");
        }
        */
    }
}