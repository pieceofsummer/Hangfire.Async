using Hangfire.Server;
using System.Threading.Tasks;

namespace Hangfire.Async.Server
{
    public interface IAsyncBackgroundJobPerformer
    {
        Task<object> PerformAsync(PerformContext context);
    }
}
