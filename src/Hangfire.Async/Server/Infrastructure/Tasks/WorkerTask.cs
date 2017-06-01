using Hangfire.Logging;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure.Tasks
{
    public class WorkerTask : IBackgroundTask
    {
        private static readonly TimeSpan JobInitializationWaitTimeout = TimeSpan.FromMinutes(1);
        private static readonly ILog Logger = LogProvider.For<WorkerTask>();

        private readonly string[] _queues;
        private readonly IAsyncBackgroundJobPerformer _performer;
        private readonly IBackgroundJobStateChanger _stateChanger;
        private readonly string _workerId;

        public WorkerTask(string[] queues, IAsyncBackgroundJobPerformer performer, IBackgroundJobStateChanger stateChanger)
        {
            if (queues == null || queues.Length == 0) throw new ArgumentNullException(nameof(queues));
            if (performer == null) throw new ArgumentNullException(nameof(performer));
            if (stateChanger == null) throw new ArgumentNullException(nameof(stateChanger));

            _queues = queues;
            _performer = performer;
            _stateChanger = stateChanger;
            _workerId = Guid.NewGuid().ToString();
        }

        #region Private constructors

        private static readonly ConstructorInfo ProcessingStateCtor = typeof(ProcessingState)
            .GetTypeInfo().DeclaredConstructors.Single(x => !x.IsStatic);

        private static ProcessingState CreateProcessingState(string serverId, string workerId)
        {
            return (ProcessingState)ProcessingStateCtor.Invoke(new object[] { serverId, workerId });
        }

        private static readonly ConstructorInfo SucceededStateCtor = typeof(SucceededState)
            .GetTypeInfo().DeclaredConstructors.Single(x => !x.IsStatic);
        
        private static SucceededState CreateSucceededState(object result, long latency, long performanceDuration)
        {
            return (SucceededState)SucceededStateCtor.Invoke(new[] { result, latency, performanceDuration });
        }

        #endregion

        public string Name => ToString();

        public async Task ExecuteAsync(BackgroundProcessContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            using (var connection = context.Storage.GetConnection())
            using (var fetchedJob = connection.FetchNextJob(_queues, context.CancellationToken))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using (var timeoutCts = new CancellationTokenSource(JobInitializationWaitTimeout))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, timeoutCts.Token))
                    {
                        // SHOULD BE: var processingState = new ProcessingState(context.ServerId, _workerId);
                        var processingState = CreateProcessingState(context.ServerId, _workerId);

                        var appliedState = _stateChanger.ChangeState(new StateChangeContext(
                            context.Storage,
                            connection,
                            fetchedJob.JobId,
                            processingState,
                            new[] { EnqueuedState.StateName, ProcessingState.StateName },
                            linkedCts.Token));

                        // Cancel job processing if the job could not be loaded, was not in the initial state expected
                        // or if a job filter changed the state to something other than processing state
                        if (appliedState == null || !appliedState.Name.Equals(ProcessingState.StateName, StringComparison.OrdinalIgnoreCase))
                        {
                            // We should re-queue a job identifier only when graceful shutdown
                            // initiated.
                            context.CancellationToken.ThrowIfCancellationRequested();

                            // We should forget a job in a wrong state, or when timeout exceeded.
                            fetchedJob.RemoveFromQueue();
                            return;
                        }
                    }

                    // Checkpoint #3. Job is in the Processing state. However, there are
                    // no guarantees that it was performed. We need to re-queue it even
                    // it was performed to guarantee that it was performed AT LEAST once.
                    // It will be re-queued after the JobTimeout was expired.

                    var state = await PerformJob(context, connection, fetchedJob.JobId).ConfigureAwait(false);

                    if (state != null)
                    {
                        // Ignore return value, because we should not do anything when current state is not Processing.
                        _stateChanger.ChangeState(new StateChangeContext(
                            context.Storage,
                            connection,
                            fetchedJob.JobId,
                            state,
                            ProcessingState.StateName));
                    }

                    // Checkpoint #4. The job was performed, and it is in the one
                    // of the explicit states (Succeeded, Scheduled and so on).
                    // It should not be re-queued, but we still need to remove its
                    // processing information.

                    fetchedJob.RemoveFromQueue();

                    // Success point. No things must be done after previous command
                    // was succeeded.
                }
                catch (Exception ex)
                {
                    if (context.IsShutdownRequested)
                    {
                        Logger.Info(String.Format(
                            "Shutdown request requested while processing background job '{0}'. It will be re-queued.",
                            fetchedJob.JobId));
                    }
                    else
                    {
                        Logger.DebugException("An exception occurred while processing a job. It will be re-queued.", ex);
                    }

                    fetchedJob.Requeue();
                    throw;
                }
            }
        }
        
        public override string ToString()
        {
            return $"Worker #{_workerId.Substring(0, 8)}";
        }

        private async Task<IState> PerformJob(BackgroundProcessContext context, IStorageConnection connection, string jobId)
        {
            try
            {
                var jobData = connection.GetJobData(jobId);
                if (jobData == null)
                {
                    // Job expired just after moving to a processing state. This is an
                    // unreal scenario, but shit happens. Returning null instead of throwing
                    // an exception and rescuing from en-queueing a poisoned jobId back
                    // to a queue.
                    return null;
                }

                jobData.EnsureLoaded();

                var backgroundJob = new BackgroundJob(jobId, jobData.Job, jobData.CreatedAt);

                var jobToken = new ServerJobCancellationToken(connection, jobId, context.ServerId, _workerId, context.CancellationToken);
                var performContext = new PerformContext(connection, backgroundJob, jobToken);

                var latency = (DateTime.UtcNow - jobData.CreatedAt).TotalMilliseconds;
                var duration = Stopwatch.StartNew();

                var result = await _performer.PerformAsync(performContext).ConfigureAwait(false);
                duration.Stop();

                // SHOULD BE: return new SucceededState(result, (long)latency, duration.ElapsedMilliseconds);
                return CreateSucceededState(result, (long)latency, duration.ElapsedMilliseconds);
            }
            catch (JobAbortedException)
            {
                // Background job performance was aborted due to a
                // state change, so it's idenfifier should be removed
                // from a queue.
                return null;
            }
            catch (JobPerformanceException ex)
            {
                return new FailedState(ex.InnerException)
                {
                    Reason = ex.Message
                };
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && context.IsShutdownRequested)
                {
                    throw;
                }

                return new FailedState(ex)
                {
                    Reason = "An exception occurred during processing of a background job."
                };
            }
        }
    }
}
