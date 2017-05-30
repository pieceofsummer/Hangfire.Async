using Hangfire.Server;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure.Compatibility
{
    internal class BackgroundProcessWrapper : ITaskSource
    {
        private readonly IBackgroundProcess _process;

        public BackgroundProcessWrapper(IBackgroundProcess process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            _process = process;
        }

        public Task CreateTask(BackgroundProcessContext context)
        {
            return Task.Factory.StartNew(
                Execute, context,
                TaskCreationOptions.LongRunning);
        }

        private void Execute(object context)
        {
            try
            {
                Thread.CurrentThread.Name = _process.GetType().Name;
            }
            catch
            {
                // ignored
            }

            _process.Execute((BackgroundProcessContext)context);
        }
    }
}
