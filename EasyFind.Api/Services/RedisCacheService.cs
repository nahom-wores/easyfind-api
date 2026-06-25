using System.Text.Json;
using EasyFind.Api.Services.IServices;
using StackExchange.Redis;

namespace EasyFind.Api.Services;

public class RedisCacheService : IRedisCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly IDatabase _db;
    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
        _db = _redis.GetDatabase();
    }
    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            return value.IsNullOrEmpty
                ? default
                : JsonSerializer.Deserialize<T>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache key: {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(value);
            await _db.StringSetAsync(key, serialized,expiration);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error setting cache key: {Key}", key);
        }
    }

    public async Task<bool> RemoveAsync(string key)
    {
        try
        {
            return await _db.KeyDeleteAsync(key);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error removing cache key: {Key}", key);
            return false;
        }
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        try
        {
            var endPoints = _redis.GetEndPoints();
            var server = _redis.GetServer(endPoints[0]);
            
            // Use SCAN instead of KEYS (SCAN is non-blocking)
            // StackExchange.Redis handles SCAN automatically when using an async foreach on IGrouping or similar
            var keys = new List<RedisKey>();
            await foreach(var key in server.KeysAsync(pattern: pattern))
            {
                keys.Add(key);
                if (keys.Count < 1000) continue;
                await _db.KeyDeleteAsync(keys.ToArray());
                keys.Clear();
            }

            if (keys.Count > 0)
                await _db.KeyDeleteAsync(keys.ToArray());
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error removing keys by pattern: {Pattern}", pattern);
        }
        
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return await _db.KeyExistsAsync(key);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error checking cache key existence: {Key}", key);
            return false;
        }
    }
    public async Task<long> IncrementAsync(string key, long i)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.StringIncrementAsync(key, i);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Redis Increment failed for key {Key}. Is Redis running?", key);
            return 0; 
        }
        
    }

    public async Task<List<string>> GetKeysByPatternAsync(string pattern)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern).Select(k => k.ToString()).ToList();
        return keys;
    }
}