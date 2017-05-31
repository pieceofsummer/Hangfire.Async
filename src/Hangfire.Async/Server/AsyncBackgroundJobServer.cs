using Hangfire.Annotations;
using Hangfire.Async.Server.Infrastructure;
using Hangfire.Async.Server.Infrastructure.Tasks;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Logging;
using Hangfire.Server;
using Hangfire.States;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hangfire.Async.Server
{
    public class AsyncBackgroundJobServer : IDisposable
    {
        private static readonly ILog Logger = LogProvider.For<AsyncBackgroundJobServer>();

        private readonly BackgroundJobServerOptions _options;
        private readonly AsyncBackgroundProcessingServer _processingServer;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncBackgroundJobServer"/> class
        /// with default options and <see cref="JobStorage.Current"/> storage.
        /// </summary>
        public AsyncBackgroundJobServer()
            : this(new BackgroundJobServerOptions())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncBackgroundJobServer"/> class
        /// with default options and the given storage.
        /// </summary>
        /// <param name="storage">The storage</param>
        public AsyncBackgroundJobServer([NotNull] JobStorage storage)
            : this(new BackgroundJobServerOptions(), storage)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncBackgroundJobServer"/> class
        /// with the given options and <see cref="JobStorage.Current"/> storage.
        /// </summary>
        /// <param name="options">Server options</param>
        public AsyncBackgroundJobServer([NotNull] BackgroundJobServerOptions options)
            : this(options, JobStorage.Current)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncBackgroundJobServer"/> class
        /// with the specified options and the given storage.
        /// </summary>
        /// <param name="options">Server options</param>
        /// <param name="storage">The storage</param>
        public AsyncBackgroundJobServer([NotNull] BackgroundJobServerOptions options, [NotNull] JobStorage storage)
            : this(options, storage, Enumerable.Empty<IBackgroundProcess>())
        {
        }

        public AsyncBackgroundJobServer(
            [NotNull] BackgroundJobServerOptions options,
            [NotNull] JobStorage storage,
            [NotNull] IEnumerable<IBackgroundProcess> additionalProcesses)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (additionalProcesses == null) throw new ArgumentNullException(nameof(additionalProcesses));

            _options = options;

            var processes = new List<ITaskSource>();
            processes.AddRange(GetRequiredProcesses());
            processes.AddRange(additionalProcesses.Select(ExtensionMethods.Loop).Select(ExtensionMethods.Wrap));

            var properties = new Dictionary<string, object>
            {
                { "Queues", options.Queues },
                { "WorkerCount", options.WorkerCount }
            };

            Logger.Info("Starting Hangfire Server");
            Logger.Info($"Using job storage: '{storage}'");

            storage.WriteOptionsToLog(Logger);

            Logger.Info("Using the following options for Hangfire Server:");
            Logger.Info($"    Worker count: {options.WorkerCount}");
            Logger.Info($"    Listening queues: {String.Join(", ", options.Queues.Select(x => "'" + x + "'"))}");
            Logger.Info($"    Shutdown timeout: {options.ShutdownTimeout}");
            Logger.Info($"    Schedule polling interval: {options.SchedulePollingInterval}");

            _processingServer = new AsyncBackgroundProcessingServer(
                storage,
                processes,
                properties,
                GetProcessingServerOptions());
        }

        public void SendStop()
        {
            Logger.Debug("Hangfire Server is stopping...");
            _processingServer.SendStop();
        }

        public void Dispose()
        {
            _processingServer.Dispose();
            Logger.Info("Hangfire Server stopped.");
        }

        private IEnumerable<ITaskSource> GetRequiredProcesses()
        {
            var processes = new List<ITaskSource>();

            var filterProvider = _options.FilterProvider ?? JobFilterProviders.Providers;

            var factory = new BackgroundJobFactory(filterProvider);
            var performer = new AsyncBackgroundJobPerformer(filterProvider, _options.Activator ?? JobActivator.Current);
            var stateChanger = new BackgroundJobStateChanger(filterProvider);

            for (var i = 0; i < _options.WorkerCount; i++)
            {
                processes.Add(new WorkerTask(_options.Queues, performer, stateChanger).Loop().Wrap());
            }
            
            processes.Add(new DelayedJobScheduler(_options.SchedulePollingInterval, stateChanger).Loop().Wrap());
            processes.Add(new RecurringJobScheduler(factory).Loop().Wrap());
            
            return processes;
        }

        private BackgroundProcessingServerOptions GetProcessingServerOptions()
        {
            return new BackgroundProcessingServerOptions
            {
                ShutdownTimeout = _options.ShutdownTimeout,
                HeartbeatInterval = _options.HeartbeatInterval,
#pragma warning disable 618
                ServerCheckInterval = _options.ServerWatchdogOptions?.CheckInterval ?? _options.ServerCheckInterval,
                ServerTimeout = _options.ServerWatchdogOptions?.ServerTimeout ?? _options.ServerTimeout,
                ServerName = _options.ServerName
#pragma warning restore 618
            };
        }
    }
}
