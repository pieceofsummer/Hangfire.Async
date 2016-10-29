using Hangfire.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hangfire.Async.Tasks
{
    internal class ThreadBoundTaskScheduler : TaskScheduler, IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly BlockingCollection<Task> _tasks;
        private readonly Thread _thread;

        public ThreadBoundTaskScheduler(string threadName)
        {
            _cts = new CancellationTokenSource();
            _tasks = new BlockingCollection<Task>();
            _thread = new Thread(ThreadWorker) { Name = threadName, IsBackground = true };
            _thread.Start();
        }

        public override int MaximumConcurrencyLevel => 1;

        protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();
        
        private void ThreadWorker()
        {
            try
            {
                foreach (var task in _tasks.GetConsumingEnumerable(_cts.Token))
                {
                    TryExecuteTask(task);

                    if (task.IsCanceled)
                    {
                        var context = task.AsyncState as BackgroundProcessContext;
                        if (context != null && context.IsShutdownRequested)
                        {
                            // TODO: or break? or what?
                            _cts.Cancel();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        protected override void QueueTask(Task task)
        {
            _tasks.Add(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            // disallow task inlining, always queue them
            return false;
        }

        public void Dispose()
        {
            _cts.Cancel();

            if (_thread.IsAlive)
            {
                _thread.Join();
            }

            _tasks.Dispose();
            _cts.Dispose();
        }
    }
}
