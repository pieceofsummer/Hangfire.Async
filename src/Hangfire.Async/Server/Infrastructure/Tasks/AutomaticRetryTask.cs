using Hangfire.Logging;
using Hangfire.Server;
using System;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure.Tasks
{
    internal class AutomaticRetryTask : IBackgroundTaskWrapper
    {
        private static readonly TimeSpan DefaultMaxAttemptDelay = TimeSpan.FromMinutes(5);
        private const int DefaultMaxRetryAttempts = int.MaxValue;
        
        private readonly ILog _logger;

        public AutomaticRetryTask(IBackgroundTask innerTask)
        {
            if (innerTask == null) throw new ArgumentNullException(nameof(innerTask));

            InnerTask = innerTask;

            _logger = LogProvider.GetLogger($"Hangfire.Async.Server.{innerTask.Name}");

            MaxRetryAttempts = DefaultMaxRetryAttempts;
            MaxAttemptDelay = DefaultMaxAttemptDelay;
            DelayCallback = GetBackOffMultiplier;
        }

        public IBackgroundTask InnerTask { get; }

        public int MaxRetryAttempts { get; set; }
        public TimeSpan MaxAttemptDelay { get; set; }
        public Func<int, TimeSpan> DelayCallback { get; set; }

        public string Name => InnerTask.Name;

        public async Task ExecuteAsync(BackgroundProcessContext context)
        {
            for (var i = 0; i <= MaxRetryAttempts; i++)
            {
                try
                {
                    await InnerTask.ExecuteAsync(context).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (context.IsShutdownRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Break the loop after the retry attempts number exceeded.
                    if (i >= MaxRetryAttempts - 1) throw;

                    var nextTry = DelayCallback(i);
                    var logLevel = GetLogLevel(i);

                    _logger.Log(
                        logLevel,
                        // ReSharper disable once AccessToModifiedClosure
                        () => $"Error occurred during execution of '{InnerTask}' process. Execution will be retried (attempt {i + 1} of {MaxRetryAttempts}) in {nextTry} seconds.",
                        ex);

                    await Task.Delay(nextTry, context.CancellationToken).ConfigureAwait(false);

                    if (context.IsShutdownRequested)
                    {
                        break;
                    }
                }
            }
        }
        
        public override string ToString()
        {
            return InnerTask.ToString();
        }

        private static LogLevel GetLogLevel(int i)
        {
            switch (i)
            {
                case 0:
                    return LogLevel.Debug;
                case 1:
                    return LogLevel.Info;
                case 2:
                    return LogLevel.Warn;
            }

            return LogLevel.Error;
        }

        private TimeSpan GetBackOffMultiplier(int retryAttemptNumber)
        {
            //exponential/random retry back-off.
            var rand = new Random(Guid.NewGuid().GetHashCode());
            var nextTry = rand.Next(
                (int)Math.Pow(retryAttemptNumber, 2), (int)Math.Pow(retryAttemptNumber + 1, 2) + 1);

            return TimeSpan.FromSeconds(Math.Min(nextTry, MaxAttemptDelay.TotalSeconds));
        }
    }
}
