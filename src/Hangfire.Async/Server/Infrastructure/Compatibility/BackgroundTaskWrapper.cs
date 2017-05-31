using Hangfire.Async.Tasks;
using Hangfire.Server;
using System;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure.Compatibility
{
    internal class BackgroundTaskWrapper : ITaskSource, IDisposable
    {
        private readonly IBackgroundTask _task;
        private readonly ThreadBoundTaskScheduler _taskScheduler;

        public BackgroundTaskWrapper(IBackgroundTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            _task = task;
            _taskScheduler = null;
            //_taskScheduler = new ThreadBoundTaskScheduler(_task.Name);
        }

        public Task CreateTask(BackgroundProcessContext context)
        {
            return Task.Factory.StartNew(
                ExecuteAsync, context, 
                context.CancellationToken);
        }
        
        private Task ExecuteAsync(object context)
        {
            return _task.ExecuteAsync((BackgroundProcessContext)context);
        }
        
        public void Dispose()
        {
            _taskScheduler?.Dispose();
        }
    }
}
