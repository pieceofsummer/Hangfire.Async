using Hangfire.Async.Server.Infrastructure.Compatibility;
using Hangfire.Async.Server.Infrastructure.Tasks;
using Hangfire.Server;
using System;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure
{
    internal static class ExtensionMethods
    {
        public static Task WaitAsync(this BackgroundProcessContext context, TimeSpan timeout)
        {
            return Task.Delay(timeout, context.CancellationToken);
        }

        public static IBackgroundTask Loop(this IBackgroundTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            return new InfiniteLoopTask(new AutomaticRetryTask(task));
        }

        public static IBackgroundProcess Loop(this IBackgroundProcess process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            return new InfiniteLoopProcess(new AutomaticRetryProcess(process));
        }
        
        public static ITaskSource Wrap(this IBackgroundTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            return new BackgroundTaskWrapper(task);
        }

        public static ITaskSource Wrap(this IBackgroundProcess process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            return new BackgroundProcessWrapper(process);
        }

#pragma warning disable CS0618 // Type or member is obsolete
        public static ITaskSource Wrap(this IServerComponent component)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            if (component == null) throw new ArgumentNullException(nameof(component));

            return new ServerComponentWrapper(component);
        }
    }
}
