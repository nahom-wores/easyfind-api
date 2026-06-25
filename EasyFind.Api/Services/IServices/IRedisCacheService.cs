namespace EasyFind.Api.Services.IServices;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan expiration);
    Task<bool> RemoveAsync(string key);
    Task RemoveByPatternAsync(string pattern);
    Task<bool> ExistsAsync(string key);
    Task<long> IncrementAsync(string key, long i = 1);
    Task<List<string>> GetKeysByPatternAsync(string pattern);
}