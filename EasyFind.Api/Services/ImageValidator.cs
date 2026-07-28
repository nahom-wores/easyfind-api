namespace EasyFind.Api.Services;

public class ImageValidator
{
    private const long MaxBytes = 5 * 1024 * 1024; // 5MB

    // (contentType, extension) allowed
    public static readonly string[] AllowedContentTypes =
        { "image/jpeg", "image/png", "image/webp" };

    public static (bool ok, string? error) Validate(IFormFile file)
    {
        if (file.Length == 0) return (false, "File is empty.");
        if (file.Length > MaxBytes) return (false, "Image must be 5MB or smaller.");
        if (!AllowedContentTypes.Contains(file.ContentType))
            return (false, "Only JPEG, PNG, or WebP images are allowed.");
        return (true, null);
    }

    // Magic-byte check — confirms the bytes match an image type, not just the claimed extension
    public static bool HasValidImageSignature(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        var read = stream.Read(header);
        stream.Position = 0; // reset for the actual upload

        if (read < 4) return false;

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;
        // PNG: 89 50 4E 47
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return true;
        // WebP: "RIFF"...."WEBP"
        if (read >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return true;

        return false;
    }
}