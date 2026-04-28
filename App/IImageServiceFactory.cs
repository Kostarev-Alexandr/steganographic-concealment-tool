using WpfApp2.Models;

namespace WpfApp2.Interfaces;

public interface IImageServiceFactory
{
    IExifService Create(string filePath);
    IExifService Create(ImageFormat format);
    ImageFormat DetectImageFormat(string filePath);
}
