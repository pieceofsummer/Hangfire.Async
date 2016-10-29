using Hangfire.Annotations;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using System;
using System.Threading;

namespace Hangfire.Async.Server.Infrastructure
{
    internal class ServerJobCancellationToken : IJobCancellationToken
    {
        private readonly string _jobId;
        private readonly string _serverId;
        private readonly string _workerId;
        private readonly IStorageConnection _connection;
        private readonly CancellationToken _shutdownToken;

        public ServerJobCancellationToken(
            [NotNull] IStorageConnection connection,
            [NotNull] string jobId,
            [NotNull] string serverId,
            [NotNull] string workerId,
            CancellationToken shutdownToken)
        {
            if (jobId == null) throw new ArgumentNullException(nameof(jobId));
            if (serverId == null) throw new ArgumentNullException(nameof(serverId));
            if (workerId == null) throw new ArgumentNullException(nameof(workerId));
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            _jobId = jobId;
            _serverId = serverId;
            _workerId = workerId;
            _connection = connection;
            _shutdownToken = shutdownToken;
        }

        public CancellationToken ShutdownToken => _shutdownToken;

        public void ThrowIfCancellationRequested()
        {
            _shutdownToken.ThrowIfCancellationRequested();

            if (IsJobAborted())
            {
                throw new JobAbortedException();
            }
        }

        private bool IsJobAborted()
        {
            var state = _connection.GetStateData(_jobId);

            if (state == null)
            {
                return true;
            }

            if (!state.Name.Equals(ProcessingState.StateName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!state.Data.ContainsKey("ServerId"))
            {
                return true;
            }

            if (!state.Data["ServerId"].Equals(_serverId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!state.Data.ContainsKey("WorkerId"))
            {
                return true;
            }

            if (!state.Data["WorkerId"].Equals(_workerId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
