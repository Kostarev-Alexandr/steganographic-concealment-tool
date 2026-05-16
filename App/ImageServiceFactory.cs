using WpfApp2.Interfaces;
using WpfApp2.Models;

namespace WpfApp2.Services;

public class ImageServiceFactory : IImageServiceFactory
{
    private readonly IImageValidator _validator;

    public ImageServiceFactory()
        : this(new ImageValidator())
    {
    }

    public ImageServiceFactory(IImageValidator validator)
    {
        _validator = validator;
    }

    public IExifService Create(string filePath)
    {
        var format = DetectImageFormat(filePath);
        if (format == ImageFormat.Unknown)
            throw new NotSupportedException("Формат изображения не поддерживается.");

        return Create(format);
    }

    public IExifService Create(ImageFormat format)
    {
        return format switch
        {
            ImageFormat.Jpeg => new JpegExifService(),
            ImageFormat.Png => new PngChunkService(),
            _ => throw new NotSupportedException("Формат изображения не поддерживается.")
        };
    }

    public ImageFormat DetectImageFormat(string filePath)
    {
        return _validator.IsValid(filePath, out var format)
            ? format
            : ImageFormat.Unknown;
    }
}
