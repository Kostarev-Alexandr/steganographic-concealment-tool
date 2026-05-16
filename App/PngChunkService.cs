using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using WpfApp2.Interfaces;
using WpfApp2.Models;

namespace WpfApp2.Services;

public class PngChunkService : IExifService
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public string DefaultKey => "HiddenPayload";

    public Dictionary<string, string> ExtractMetadata(string imagePath)
    {
        var metadata = new Dictionary<string, string>();

        try
        {
            var chunks = ReadChunks(imagePath);
            metadata["Format"] = "PNG";
            metadata["Chunks"] = chunks.Count.ToString();

            int textIndex = 1;
            foreach (var chunk in chunks)
            {
                if (chunk.Type == "IHDR" && chunk.Data.Length >= 8)
                {
                    uint width = BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(0, 4));
                    uint height = BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(4, 4));
                    metadata["Resolution"] = $"{width} x {height}";
                }

                if (TryParseTextChunk(chunk, out var keyword, out var value))
                {
                    metadata[$"Text[{textIndex}] {keyword}"] = value;
                    textIndex++;
                }
            }
        }
        catch (Exception ex)
        {
            metadata["Error"] = $"Ошибка чтения PNG metadata: {ex.Message}";
        }

        return metadata;
    }

    public bool WriteHiddenData(string imagePath, byte[] data, string key)
    {
        try
        {
            string effectiveKey = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;
            var chunks = ReadChunks(imagePath);

            chunks.RemoveAll(chunk => IsMatchingTextChunk(chunk, effectiveKey));
            chunks.Insert(chunks.Count - 1, CreateInternationalTextChunk(effectiveKey, Encoding.UTF8.GetString(data)));

            WriteChunks(imagePath, chunks);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public byte[]? ReadHiddenData(string imagePath, string key)
    {
        try
        {
            string effectiveKey = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;
            foreach (var chunk in ReadChunks(imagePath))
            {
                if (TryParseTextChunk(chunk, out var keyword, out var value) &&
                    string.Equals(keyword, effectiveKey, StringComparison.Ordinal))
                {
                    return Encoding.UTF8.GetBytes(value);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public bool ClearHiddenData(string imagePath, string key)
    {
        try
        {
            string effectiveKey = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;
            var chunks = ReadChunks(imagePath);
            chunks.RemoveAll(chunk => IsMatchingTextChunk(chunk, effectiveKey));
            WriteChunks(imagePath, chunks);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SupportsFormat(ImageFormat format) => format == ImageFormat.Png;

    public bool HasHiddenData(string imagePath, string key)
    {
        var data = ReadHiddenData(imagePath, key);
        return data is { Length: > 0 };
    }

    private static List<PngChunk> ReadChunks(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        using var reader = new BinaryReader(stream);

        byte[] signature = reader.ReadBytes(8);
        if (!signature.SequenceEqual(PngSignature))
            throw new InvalidDataException("Файл не является PNG.");

        var chunks = new List<PngChunk>();
        while (stream.Position < stream.Length)
        {
            uint length = ReadUInt32BigEndian(reader);
            string type = Encoding.ASCII.GetString(reader.ReadBytes(4));
            byte[] data = reader.ReadBytes((int)length);
            uint crc = ReadUInt32BigEndian(reader);
            chunks.Add(new PngChunk(type, data, crc));

            if (type == "IEND")
                break;
        }

        return chunks;
    }

    private static void WriteChunks(string imagePath, List<PngChunk> chunks)
    {
        string tempPath = imagePath + ".tmp";

        using (var stream = File.Create(tempPath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(PngSignature);
            foreach (var chunk in chunks)
            {
                WriteUInt32BigEndian(writer, (uint)chunk.Data.Length);
                byte[] typeBytes = Encoding.ASCII.GetBytes(chunk.Type);
                writer.Write(typeBytes);
                writer.Write(chunk.Data);
                WriteUInt32BigEndian(writer, ComputeCrc(typeBytes, chunk.Data));
            }
        }

        File.Copy(tempPath, imagePath, true);
        File.Delete(tempPath);
    }

    private static bool TryParseTextChunk(PngChunk chunk, out string keyword, out string value)
    {
        keyword = string.Empty;
        value = string.Empty;

        if (chunk.Type == "tEXt")
        {
            int separator = Array.IndexOf(chunk.Data, (byte)0);
            if (separator <= 0)
                return false;

            keyword = Encoding.ASCII.GetString(chunk.Data, 0, separator);
            value = Encoding.UTF8.GetString(chunk.Data, separator + 1, chunk.Data.Length - separator - 1);
            return true;
        }

        if (chunk.Type == "zTXt")
        {
            int separator = Array.IndexOf(chunk.Data, (byte)0);
            if (separator <= 0 || separator + 2 > chunk.Data.Length)
                return false;

            keyword = Encoding.ASCII.GetString(chunk.Data, 0, separator);
            if (chunk.Data[separator + 1] != 0)
                return false;

            using var compressed = new MemoryStream(chunk.Data, separator + 2, chunk.Data.Length - separator - 2);
            using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
            using var textReader = new StreamReader(deflate, Encoding.UTF8);
            value = textReader.ReadToEnd();
            return true;
        }

        if (chunk.Type == "iTXt")
            return TryParseInternationalTextChunk(chunk.Data, out keyword, out value);

        return false;
    }

    private static bool TryParseInternationalTextChunk(byte[] data, out string keyword, out string value)
    {
        keyword = string.Empty;
        value = string.Empty;

        int keywordSeparator = Array.IndexOf(data, (byte)0);
        if (keywordSeparator <= 0)
            return false;

        keyword = Encoding.ASCII.GetString(data, 0, keywordSeparator);
        int index = keywordSeparator + 1;
        if (index + 2 > data.Length)
            return false;

        byte compressionFlag = data[index++];
        byte compressionMethod = data[index++];

        int languageEnd = Array.IndexOf(data, (byte)0, index);
        if (languageEnd < 0)
            return false;
        index = languageEnd + 1;

        int translatedEnd = Array.IndexOf(data, (byte)0, index);
        if (translatedEnd < 0)
            return false;
        index = translatedEnd + 1;

        byte[] textBytes = data[index..];
        if (compressionFlag == 1)
        {
            if (compressionMethod != 0)
                return false;

            using var compressed = new MemoryStream(textBytes);
            using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            textBytes = output.ToArray();
        }

        value = Encoding.UTF8.GetString(textBytes);
        return true;
    }

    private static PngChunk CreateInternationalTextChunk(string keyword, string value)
    {
        byte[] keywordBytes = Encoding.ASCII.GetBytes(keyword);
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);

        using var stream = new MemoryStream();
        stream.Write(keywordBytes);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.Write(valueBytes);

        return new PngChunk("iTXt", stream.ToArray(), 0);
    }

    private static bool IsMatchingTextChunk(PngChunk chunk, string key)
    {
        return TryParseTextChunk(chunk, out var keyword, out _) &&
               string.Equals(keyword, key, StringComparison.Ordinal);
    }

    private static uint ReadUInt32BigEndian(BinaryReader reader)
    {
        Span<byte> buffer = stackalloc byte[4];
        int read = reader.Read(buffer);
        if (read != 4)
            throw new EndOfStreamException();
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    private static void WriteUInt32BigEndian(BinaryWriter writer, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        writer.Write(buffer);
    }

    private static uint ComputeCrc(byte[] typeBytes, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in typeBytes)
            crc = UpdateCrc(crc, value);
        foreach (byte value in data)
            crc = UpdateCrc(crc, value);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        return CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint c = i;
            for (int bit = 0; bit < 8; bit++)
                c = (c & 1) == 1 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    private sealed record PngChunk(string Type, byte[] Data, uint Crc);
}
