namespace EasyFind.Api.Services.IServices;


public record StoredFile(string Key, string Url);
public interface IStorageService
{
    // Returns a storage key + a retrievable URL
    Task<StoredFile> UploadAsync(Stream fileStream, string fileName, string contentType,
        string folder, CancellationToken ct = default);
    // Time-limited access URL (for private files)
    Task<string> GetAccessUrlAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}