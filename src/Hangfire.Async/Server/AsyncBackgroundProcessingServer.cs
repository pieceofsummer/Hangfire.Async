using Hangfire.Annotations;
using Hangfire.Async.Server.Infrastructure;
using Hangfire.Async.Server.Infrastructure.Compatibility;
using Hangfire.Async.Server.Infrastructure.Tasks;
using Hangfire.Logging;
using Hangfire.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hangfire.Async.Server
{
    /// <summary>
    /// Responsible for running the given collection background processes.
    /// </summary>
    /// 
    /// <remarks>
    /// Immediately starts the processes in a background thread.
    /// Responsible for announcing/removing a server, bound to a storage.
    /// Wraps all the processes with a infinite loop and automatic retry.
    /// Executes all the processes in a single context.
    /// Uses timeout in dispose method, waits for all the components, cancel signals shutdown
    /// Contains some required processes and uses storage processes.
    /// Generates unique id.
    /// Properties are still bad.
    /// </remarks>
    public sealed class AsyncBackgroundProcessingServer : IBackgroundProcess, IDisposable
    {
        public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(15);
        private static readonly ILog Logger = LogProvider.For<AsyncBackgroundProcessingServer>();

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<ITaskSource> _processes = new List<ITaskSource>();

        private readonly BackgroundProcessingServerOptions _options;
        private readonly Task _bootstrapTask;

        public AsyncBackgroundProcessingServer([NotNull] IEnumerable<ITaskSource> processes)
            : this(JobStorage.Current, processes)
        {
        }

        public AsyncBackgroundProcessingServer(
            [NotNull] IEnumerable<ITaskSource> processes,
            [NotNull] IDictionary<string, object> properties)
            : this(JobStorage.Current, processes, properties)
        {
        }

        public AsyncBackgroundProcessingServer(
            [NotNull] JobStorage storage,
            [NotNull] IEnumerable<ITaskSource> processes)
            : this(storage, processes, new Dictionary<string, object>())
        {
        }

        public AsyncBackgroundProcessingServer(
            [NotNull] JobStorage storage,
            [NotNull] IEnumerable<ITaskSource> processes,
            [NotNull] IDictionary<string, object> properties)
            : this(storage, processes, properties, new BackgroundProcessingServerOptions())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncBackgroundProcessingServer"/>
        /// class and immediately starts all the given background processes.
        /// </summary>
        /// <param name="storage"></param>
        /// <param name="processes"></param>
        /// <param name="properties"></param>
        /// <param name="options"></param>
        public AsyncBackgroundProcessingServer(
            [NotNull] JobStorage storage,
            [NotNull] IEnumerable<ITaskSource> processes,
            [NotNull] IDictionary<string, object> properties,
            [NotNull] BackgroundProcessingServerOptions options)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (processes == null) throw new ArgumentNullException(nameof(processes));
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            if (options == null) throw new ArgumentNullException(nameof(options));

            _options = options;

            _processes.AddRange(GetRequiredTasks().Select(ExtensionMethods.Wrap));
            _processes.AddRange(storage.GetComponents().Select(ExtensionMethods.Wrap));
            _processes.AddRange(processes);

            var context = new BackgroundProcessContext(
                GetGloballyUniqueServerId(),
                storage,
                properties,
                _cts.Token);

            _bootstrapTask = new BackgroundProcessWrapper(this).CreateTask(context);
        }

        public void SendStop()
        {
            _cts.Cancel();
        }

        public void Dispose()
        {
            SendStop();

            // TODO: Dispose _cts

            if (!_bootstrapTask.Wait(_options.ShutdownTimeout))
            {
                Logger.Warn("Processing server takes too long to shutdown. Performing ungraceful shutdown.");
            }

            _cts.Dispose();

            foreach (var item in _processes.OfType<IDisposable>())
            {
                item.Dispose();
            }
        }

        public override string ToString()
        {
            return GetType().Name;
        }
        
        void IBackgroundProcess.Execute(BackgroundProcessContext context)
        {
            using (var connection = context.Storage.GetConnection())
            {
                var serverContext = GetServerContext(context.Properties);
                connection.AnnounceServer(context.ServerId, serverContext);
            }

            try
            {
                var tasks = _processes
                    .Select(process => process.CreateTask(context))
                    .ToArray();

                Task.WaitAll(tasks);
            }
            finally
            {
                using (var connection = context.Storage.GetConnection())
                {
                    connection.RemoveServer(context.ServerId);
                }
            }
        }

        private IEnumerable<IBackgroundTask> GetRequiredTasks()
        {
            yield return new ServerHeartbeatTask(_options.HeartbeatInterval).Loop();
            yield return new ServerWatchdogTask(_options.ServerCheckInterval, _options.ServerTimeout).Loop();
        }
        
        private string GetGloballyUniqueServerId()
        {
            var serverName = _options.ServerName
                ?? Environment.GetEnvironmentVariable("COMPUTERNAME")
                ?? Environment.GetEnvironmentVariable("HOSTNAME");

            var guid = Guid.NewGuid().ToString();

#if NETFULL
            if (!String.IsNullOrWhiteSpace(serverName))
            {
                serverName += ":" + Process.GetCurrentProcess().Id;
            }
#endif

            return !String.IsNullOrWhiteSpace(serverName)
                ? $"{serverName.ToLowerInvariant()}:{guid}"
                : guid;
        }
        
        private static ServerContext GetServerContext(IReadOnlyDictionary<string, object> properties)
        {
            var serverContext = new ServerContext();

            if (properties.ContainsKey("Queues"))
            {
                var array = properties["Queues"] as string[];
                if (array != null)
                {
                    serverContext.Queues = array;
                }
            }

            if (properties.ContainsKey("WorkerCount"))
            {
                serverContext.WorkerCount = (int)properties["WorkerCount"];
            }

            return serverContext;
        }
    }
}
