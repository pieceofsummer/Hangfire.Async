using Hangfire.Server;
using System;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure.Tasks
{
    internal class InfiniteLoopTask : IBackgroundTaskWrapper
    {
        public InfiniteLoopTask(IBackgroundTask innerTask)
        {
            if (innerTask == null) throw new ArgumentNullException(nameof(innerTask));

            InnerTask = innerTask;
        }

        public IBackgroundTask InnerTask { get; }

        public string Name => InnerTask.Name;

        public async Task ExecuteAsync(BackgroundProcessContext context)
        {
            while (!context.IsShutdownRequested)
            {
                await InnerTask.ExecuteAsync(context);
            }
        }

        public override string ToString()
        {
            return InnerTask.ToString();
        }
    }
}
