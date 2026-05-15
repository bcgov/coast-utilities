using System.Threading.Tasks;
using System.Threading.Tasks;

namespace Shared.Database;

public interface ITokenProvider
{
    Task<string> AcquireToken();
}
