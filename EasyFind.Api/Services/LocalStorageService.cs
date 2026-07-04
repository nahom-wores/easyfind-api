using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services;

public class LocalStorageService(IWebHostEnvironment env, IConfiguration config) : IStorageService
{
    private readonly string _root = Path.Combine(env.ContentRootPath, "uploads");
    private readonly string _baseUrl = config["App:BaseUrl"] ?? "https://localhost:7111";

    public async Task<StoredFile> UploadAsync(Stream fileStream, string fileName, string contentType, string folder, CancellationToken ct = default)
    {
        var folderPath = Path.Combine(_root, folder);
        Directory.CreateDirectory(folderPath);

        // Never trust the uploaded filename — generate our own
        var ext = Path.GetExtension(fileName);
        var key = $"{folder}/{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_root, key);

        await using (var fs = new FileStream(fullPath, FileMode.Create))
            await fileStream.CopyToAsync(fs, ct);

        var url = $"{_baseUrl}/uploads/{key}";
        return new StoredFile(key, url);
    }

    public Task<string> GetAccessUrlAsync(string key, CancellationToken ct = default)
        => Task.FromResult($"{_baseUrl}/uploads/{key}");

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_root, key);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }
}