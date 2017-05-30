using Hangfire.Server;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure
{
    internal interface IBackgroundTask
    {
        string Name { get; }

        Task ExecuteAsync(BackgroundProcessContext context);
    }
}
