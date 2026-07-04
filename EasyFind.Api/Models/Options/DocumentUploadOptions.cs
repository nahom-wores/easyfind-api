namespace EasyFind.Api.Models.Options;

public class DocumentUploadOptions
{
    public const string SectionName = "DocumentUpload";
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".doc", ".docx"];
}