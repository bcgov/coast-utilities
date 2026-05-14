using System;
using System.Threading.Tasks;

namespace Shared.Database;

public interface ICache
{
    Task<T?> GetOrSet<T>(string key, Func<Task<T>> factory, TimeSpan expiration)
        where T : class;
}
