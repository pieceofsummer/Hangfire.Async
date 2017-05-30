using Hangfire.Server;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure
{
    public interface ITaskSource
    {
        Task CreateTask(BackgroundProcessContext context);
    }
}
