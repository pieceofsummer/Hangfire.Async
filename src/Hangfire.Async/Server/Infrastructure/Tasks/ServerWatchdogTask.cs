using Hangfire.Logging;
using Hangfire.Server;
using System;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure.Tasks
{
    internal class ServerWatchdogTask : IBackgroundTask
    {
        public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan DefaultServerTimeout = TimeSpan.FromMinutes(5);

        private static readonly ILog Logger = LogProvider.For<ServerWatchdogTask>();

        private readonly TimeSpan _checkInterval;
        private readonly TimeSpan _serverTimeout;

        public ServerWatchdogTask(TimeSpan checkInterval, TimeSpan serverTimeout)
        {
            _checkInterval = checkInterval;
            _serverTimeout = serverTimeout;
        }

        public string Name => "ServerWatchdog";

        public Task ExecuteAsync(BackgroundProcessContext context)
        {
            using (var connection = context.Storage.GetConnection())
            {
                var serversRemoved = connection.RemoveTimedOutServers(_serverTimeout);
                if (serversRemoved != 0)
                {
                    Logger.Info($"{serversRemoved} servers were removed due to timeout");
                }
            }

            return context.WaitAsync(_checkInterval);
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
