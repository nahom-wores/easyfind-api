using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services;

// No Redis configured — every read misses, every write/delete is a no-op.
// Callers fall straight through to the database. Never throws.
public class NoOpCacheService : IRedisCacheService
{
    public Task<T> GetAsync<T>(string key) => Task.FromResult<T>(default);
    
    public Task SetAsync<T>(string key, T value, TimeSpan expiration) => Task.CompletedTask;

    public Task<bool> RemoveAsync(string key) => Task.FromResult(false);

    public Task RemoveByPatternAsync(string pattern) => Task.CompletedTask;
    
    public Task<bool> ExistsAsync(string key) => Task.FromResult(false);

    public Task<long> IncrementAsync(string key, long i = 1) => Task.FromResult(0L);

    public Task<List<string>> GetKeysByPatternAsync(string pattern)
        => Task.FromResult(new List<string>());
}