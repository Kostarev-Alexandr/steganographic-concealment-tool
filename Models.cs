namespace ExifTool.Core.Models;

public enum ImageFormat { Unknown, Jpeg, Png }

public record EmbedRequest(string FilePath, string Payload);

public record ExtractResult(bool Success, string? Text, string? Error);
