using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Shared.Database;

public class MemoryCache : ICache
{
    private readonly IMemoryCache memoryCache;

    public MemoryCache(IMemoryCache memoryCache)
    {
        this.memoryCache = memoryCache;
    }

    public async Task<T?> GetOrSet<T>(string key, Func<Task<T>> factory, TimeSpan expiration)
        where T : class
    {
        if (memoryCache.TryGetValue(key, out T? cachedValue))
        {
            return cachedValue;
        }

        var value = await factory();
        memoryCache.Set(key, value, expiration);
        return value;
    }
}
