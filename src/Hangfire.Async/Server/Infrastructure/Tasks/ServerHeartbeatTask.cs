using Hangfire.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure.Tasks
{
    internal class ServerHeartbeatTask : IBackgroundTask
    {
        public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

        private readonly TimeSpan _heartbeatInterval;

        public ServerHeartbeatTask(TimeSpan heartbeatInterval)
        {
            _heartbeatInterval = heartbeatInterval;
        }

        public string Name => "ServerHeartbeat";

        public Task ExecuteAsync(BackgroundProcessContext context)
        {
            using (var connection = context.Storage.GetConnection())
            {
                connection.Heartbeat(context.ServerId);
            }

            return context.WaitAsync(_heartbeatInterval);
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
