using System;
using System.Collections.Generic;
using System.Text;

namespace WpfApp2.Interfaces
{
    internal interface IImageServiceFactory
    {
        IExifService GetExifService(string filePath);
        //IPngMetadataService GetPngService(string filePath); ПАТОМ СДЕЛАТЬ
    }
}
