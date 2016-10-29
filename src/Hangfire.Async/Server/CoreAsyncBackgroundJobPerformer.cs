using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire.Server;
using System.Threading;
using Hangfire.Annotations;
using System.Reflection;
using Hangfire.Common;
using System.Runtime.ExceptionServices;
using Hangfire.Storage;

namespace Hangfire.Async.Server
{
    public class CoreAsyncBackgroundJobPerformer : IAsyncBackgroundJobPerformer
    {
        internal static readonly Dictionary<Type, Func<PerformContext, object>> Substitutions
            = new Dictionary<Type, Func<PerformContext, object>>
            {
                [typeof(IJobCancellationToken)] = x => x.CancellationToken,
                [typeof(CancellationToken)] = x => x.CancellationToken.ShutdownToken,
                [typeof(PerformContext)] = x => x
            };

        private readonly JobActivator _activator;

        public CoreAsyncBackgroundJobPerformer([NotNull] JobActivator activator)
        {
            if (activator == null) throw new ArgumentNullException(nameof(activator));
            _activator = activator;
        }
        
        #region Private constructors

        private static readonly ConstructorInfo JobActivatorContextCtor = typeof(JobActivatorContext)
            .GetTypeInfo().DeclaredConstructors.Where(x => !x.IsStatic).Single();

        private static JobActivatorContext CreateJobActivatorContext(IStorageConnection connection, BackgroundJob backgroundJob, IJobCancellationToken cancenllationToken)
        {
            return (JobActivatorContext)JobActivatorContextCtor.Invoke(new object[] { connection, backgroundJob, cancenllationToken });
        }
        
        #endregion
        
        public async Task<object> PerformAsync(PerformContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var job = context.BackgroundJob.Job;

            if (job == null || job.Type == null || job.Method == null)
                throw new InvalidOperationException("Can't perform a background job with a null/incomplete job.");
            
            var method = job.Method;

            using (var scope = _activator.BeginScope(CreateJobActivatorContext(
                context.Connection, context.BackgroundJob, context.CancellationToken)))
            {
                object instance = null;

                if (!method.IsStatic)
                {
                    instance = scope.Resolve(job.Type);

                    if (instance == null)
                        throw new InvalidOperationException($"JobActivator returned NULL instance of the '{job.Type}' type.");
                }

                var arguments = PrepareArguments(job, context);

                try
                {
                    var result = InvokeMethod(method, instance, arguments);

                    if (typeof(Task).GetTypeInfo().IsAssignableFrom(method.ReturnType.GetTypeInfo()))
                    {
                        var task = (Task)result;

                        if (task.Status != TaskStatus.RanToCompletion)
                        {
                            // task is not finished yet, wait for completion
                            await task;
                        }

                        return GetTaskResult(task, method.ReturnType);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    HandleJobPerformanceException(ex, context.CancellationToken.ShutdownToken);
                    throw;
                }
            }
        }
        
        internal static void HandleJobPerformanceException(Exception exception, CancellationToken shutdownToken)
        {
            if (exception is JobAbortedException)
            {
                // JobAbortedException exception should be thrown as-is to notify
                // a worker that background job was aborted by a state change, and
                // should NOT be re-queued.
                ExceptionDispatchInfo.Capture(exception).Throw();
                throw exception;
            }

            if (exception is OperationCanceledException && shutdownToken.IsCancellationRequested)
            {
                // OperationCanceledException exceptions are treated differently from
                // others, when ShutdownToken's cancellation was requested, to notify
                // a worker that job performance was aborted by a shutdown request,
                // and a job identifier should BE re-queued.
                ExceptionDispatchInfo.Capture(exception).Throw();
                throw exception;
            }

            // Other exceptions are wrapped with JobPerformanceException to preserve a
            // shallow stack trace without Hangfire methods.
            throw new JobPerformanceException("An exception occurred during performance of the job.", exception);
        }

        private static object[] PrepareArguments(Job job, PerformContext context)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var parameters = job.Method.GetParameters();

            if (job.Args.Count != parameters.Length)
                throw new ArgumentException("Wrong number of arguments provided", nameof(job));

            var result = new object[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                object argument;

                Func<PerformContext, object> factory;
                if (Substitutions.TryGetValue(parameters[i].ParameterType, out factory))
                {
                    // parameter has one of the 'special' types
                    argument = factory(context);
                }
                else
                {
                    argument = job.Args[i];
                }
                
                result[i] = argument;
            }

            return result;
        }

        private static object InvokeMethod(MethodInfo method, object instance, object[] arguments)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (AggregateException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        #region GetTaskResult implementation

        private static object GetTaskResult(Task task, Type type)
        {
            if (!type.IsConstructedGenericType || type.GetGenericTypeDefinition() != typeof(Task<>))
            {
                // not a Task<T>, return void
                return null;
            }

            var method = GetTaskResultMethod.MakeGenericMethod(type.GenericTypeArguments);

            return method.Invoke(null, new object[1] { task });
        }
        
        private static T GetTaskResultImpl<T>(Task task) => ((Task<T>)task).Result;

        private static readonly MethodInfo GetTaskResultMethod = typeof(CoreAsyncBackgroundJobPerformer)
            .GetTypeInfo().GetDeclaredMethod(nameof(GetTaskResultImpl));

        #endregion
    }
}
