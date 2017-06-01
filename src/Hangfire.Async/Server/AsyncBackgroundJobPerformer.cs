using System;
using System.Linq;
using System.Threading.Tasks;
using Hangfire.Server;
using Hangfire.Common;
using Hangfire.Annotations;
using Hangfire.Logging;
using Hangfire.Async.Filters;
using System.Runtime.ExceptionServices;
using System.Diagnostics;

namespace Hangfire.Async.Server
{
    public class AsyncBackgroundJobPerformer : IAsyncBackgroundJobPerformer
    {
        private static readonly ILog _log = LogProvider.For<AsyncBackgroundJobPerformer>();

        private readonly IJobFilterProvider _filterProvider;
        private readonly IAsyncBackgroundJobPerformer _innerPerformer;

        public AsyncBackgroundJobPerformer([NotNull] IJobFilterProvider filterProvider, [NotNull] JobActivator activator)
            : this(filterProvider, new CoreAsyncBackgroundJobPerformer(activator))
        {
        }

        public AsyncBackgroundJobPerformer([NotNull] IJobFilterProvider filterProvider, [NotNull] IAsyncBackgroundJobPerformer innerPerformer)
        {
            if (filterProvider == null) throw new ArgumentNullException(nameof(filterProvider));
            if (innerPerformer == null) throw new ArgumentNullException(nameof(innerPerformer));

            _filterProvider = filterProvider;
            _innerPerformer = innerPerformer;
        }

        public Task<object> PerformAsync(PerformContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var filters = _filterProvider.GetFilters(context.BackgroundJob.Job).Select(x => x.Instance).ToArray();

            return new Performance(context, filters, _innerPerformer).PerformAsync();
        }

        private class Performance
        {
            private readonly PerformContext _context;
            private DualCursor _filters;
            private readonly IAsyncBackgroundJobPerformer _innerPerformer;

            private PerformingContext _performingContext;
            private PerformedContext _performedContext;
            private ServerExceptionContext _exceptionContext;

            private IJobCancellationToken JobCancellationToken => _context.CancellationToken;
            
            public Performance(PerformContext context, object[] filters, IAsyncBackgroundJobPerformer innerPerformer)
            {
                if (context == null) throw new ArgumentNullException(nameof(context));
                if (filters == null) throw new ArgumentNullException(nameof(filters));
                if (innerPerformer == null) throw new ArgumentNullException(nameof(innerPerformer));

                _context = context;
                _filters = new DualCursor(filters);
                _innerPerformer = innerPerformer;
            }

            public async Task<object> PerformAsync()
            {
                State state; object data;

                try
                {
                    // Primary loop to handle job filters and background jobs

                    // SHOULD BE: _performingContext = new PerformingContext(_context);
                    _performingContext = new PerformingContext(_context);

                    try
                    {
                        for (state = State.Begin, data = null; state != State.End; JobCancellationToken.ThrowIfCancellationRequested())
                        {
                            // All processing is performed in a tight loop inside the following method.
                            // We'll only get back here sometimes to wait for the next Task to complete.
                            // For performance's sake, we'll only await for really unfinished jobs 
                            // (i.e. not constanst/cached/finished ones like Task.FromResult() etc.)
                            await JobFilters(ref state, ref data).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        // This is an exception from pre/post-processing phases. 
                        // Though it shouldn't be processed by other job filters,
                        // it still may be handled by exception filters.
                        CoreAsyncBackgroundJobPerformer.HandleJobPerformanceException(ex, JobCancellationToken.ShutdownToken);
                        throw;
                    }

                    if (_performedContext?.Exception != null && !_performedContext.ExceptionHandled)
                    {
                        // This is an exception from job processing phase. 
                        // It has been delivered to all job filters, but still unhandled.
                        // Rethrow it here, so it will be picked up by exception filters.
                        ExceptionDispatchInfo.Capture(_performedContext.Exception).Throw();
                    }
                }
                catch (JobAbortedException)
                {
                    // Never intercept JobAbortException, it is supposed for internal use.
                    throw;
                }
                catch (OperationCanceledException) when (JobCancellationToken.ShutdownToken.IsCancellationRequested)
                {
                    // Don't intercept OperationCancelledException after cancellation was requested.
                    throw;
                }
                catch (Exception ex)
                {
                    // Secondary loop to handle exception filters

                    // SHOULD BE: _exceptionContext = new ServerExceptionContext(_context, ex);
                    _exceptionContext = new ServerExceptionContext(_context, ex);

                    for (state = State.Begin, data = null; state != State.End; JobCancellationToken.ThrowIfCancellationRequested())
                    {
                        await ExceptionFilters(ref state, ref data).ConfigureAwait(false);
                    }

                    if (!_exceptionContext.ExceptionHandled)
                    {
                        // None of the exception filters has handled the exception.
                        // We're out of options, so just re-throw it here.
                        ExceptionDispatchInfo.Capture(_exceptionContext.Exception).Throw();
                    }
                }

                return _performedContext?.Result;
            }
            
            private Task JobFilters(ref State state, ref object data)
            {
                Debug.Assert(state > State.PerformAsyncBegin || _performingContext != null, "performingContext not initialized");
                Debug.Assert(state < State.PerformAsyncEnd   || _performedContext  != null, "performedContext not initialized");

                Dual<IServerFilter, IAsyncServerFilter> filter;
                IServerFilter syncFilter; IAsyncServerFilter asyncFilter;
                Task task;

                switch (state)
                {
                    case State.Begin:
                        {
                            _filters.Reset();
                            goto case State.OnPerformingNext;
                        }

                    case State.OnPerformingNext:
                        {
                            filter = _filters.GetNextFilter<IServerFilter, IAsyncServerFilter>();
                            if (filter.NotFound)
                            {
                                goto case State.PerformAsyncBegin;
                            }
                            else if (filter.Async != null)
                            {
                                data = filter.Async;
                                goto case State.OnPerformingAsyncBegin;
                            }
                            else
                            {
                                data = filter.Sync;
                                goto case State.OnPerformingSync;
                            }
                        }

                    case State.OnPerformingAsyncBegin:
                        {
                            Debug.Assert(data != null);

                            asyncFilter = (IAsyncServerFilter)data;
                            _log.DebugFormat("enter '{0}.OnPerformingAsync'", asyncFilter.GetType().Name);

                            task = asyncFilter.OnPerformingAsync(_performingContext);
                            if (task.Status != TaskStatus.RanToCompletion)
                            {
                                state = State.OnPerformingAsyncEnd;
                                return task;
                            }

                            goto case State.OnPerformingAsyncEnd;
                        }

                    case State.OnPerformingAsyncEnd:
                        {
                            Debug.Assert(data != null);

                            asyncFilter = (IAsyncServerFilter)data;
                            _log.DebugFormat("leave '{0}.OnPerformingAsync'", asyncFilter.GetType().Name);

                            goto case State.OnPerformingCheckCancel;
                        }

                    case State.OnPerformingSync:
                        {
                            Debug.Assert(data != null);

                            syncFilter = (IServerFilter)data;
                            _log.DebugFormat("enter '{0}.OnPerforming'", syncFilter.GetType().Name);

                            syncFilter.OnPerforming(_performingContext);

                            _log.DebugFormat("leave '{0}.OnPerforming'", syncFilter.GetType().Name);

                            goto case State.OnPerformingCheckCancel;
                        }

                    case State.OnPerformingCheckCancel:
                        {
                            if (_performingContext.Canceled)
                            {
                                _performedContext = new PerformedContext(_context, null, true, null);
                                goto case State.OnCancelPrev;
                            }

                            goto case State.OnPerformingNext;
                        }

                    case State.OnCancelPrev:
                        {
                            filter = _filters.GetPrevFilter<IServerFilter, IAsyncServerFilter>();
                            if (filter.NotFound)
                            {
                                goto case State.End;
                            }
                            else if (filter.Async != null)
                            {
                                data = filter.Async;
                                goto case State.OnCancelAsyncBegin;
                            }
                            else
                            {
                                data = filter.Sync;
                                goto case State.OnCancelSync;
                            }
                        }

                    case State.OnCancelAsyncBegin:
                        {
                            Debug.Assert(data != null);

                            asyncFilter = (IAsyncServerFilter)data;
                            _log.DebugFormat("enter '{0}.OnPerformedAsync'", asyncFilter.GetType().Name);

                            task = asyncFilter.OnPerformedAsync(_performedContext);
                            if (task.Status != TaskStatus.RanToCompletion)
                            {
                                state = State.OnCancelAsyncEnd;
                                return task;
                            }

                            goto case State.OnCancelAsyncEnd;
                        }

                    case State.OnCancelAsyncEnd:
                        {
                            Debug.Assert(data != null);

                            asyncFilter = (IAsyncServerFilter)data;
                            _log.DebugFormat("leave '{0}.OnPerformedAsync'", asyncFilter.GetType().Name);

                            goto case State.OnCancelPrev;
                        }

                    case State.OnCancelSync:
                        {
                            Debug.Assert(data != null);

                            syncFilter = (IServerFilter)data;
                            _log.DebugFormat("enter '{0}.OnPerformed'", syncFilter.GetType().Name);

                            syncFilter.OnPerformed(_performedContext);

                            _log.DebugFormat("leave '{0}.OnPerformed'", syncFilter.GetType().Name);

                            goto case State.OnCancelPrev;
                        }

                    case State.PerformAsyncBegin:
                        {
                            _log.DebugFormat("enter '{0}'", _context.BackgroundJob.Job);

                            task = PerformJobAsync();
                            if (task.Status != TaskStatus.RanToCompletion)
                            {
                                state = State.PerformAsyncEnd;
                                return task;
                            }

                            goto case State.PerformAsyncEnd;
                        }

                    case State.PerformAsyncEnd:
                        {
                            _log.DebugFormat("leave '{0}'", _context.BackgroundJob.Job);

                            Debug.Assert(_performedContext != null);

                            //_filters.Reset();
                            _filters.ResetToEnd();
                            goto case State.OnPerformedNext;
                        }

                    case State.OnPerformedNext:
                        {
                            //filter = _filters.GetNextFilter<IServerFilter, IAsyncServerFilter>();
                            filter = _filters.GetPrevFilter<IServerFilter, IAsyncServerFilter>();
                            if (filter.NotFound)
                            {
                                goto case State.End;
                            }
                            else if (filter.Async != null)
                            {
                                data = filter.Async;
                                goto case State.OnPerformedAsyncBegin;
                            }
                            else
                            {
                                data = filter.Sync;
                                goto case State.OnPerformedSync;
                            }
                        }

                    case State.OnPerformedAsyncBegin:
                        {
                            Debug.Assert(data != null);

                            asyncFilter = (IAsyncServerFilter)data;
                            _log.DebugFormat("enter '{0}.OnPerformedAsync'", asyncFilter.GetType().Name);

                            task = asyncFilter.OnPerformedAsync(_performedContext);
                            if (task.Status != TaskStatus.RanToCompletion)
                            {
                                state = State.OnPerformedAsyncEnd;
                                return task;
                            }

                            goto case State.OnPerformedAsyncEnd;
                        }

                    case State.OnPerformedAsyncEnd:
                        {
                            Debug.Assert(data != null);

                            asyncFilter = (IAsyncServerFilter)data;
                            _log.DebugFormat("leave '{0}.OnPerformedAsync'", asyncFilter.GetType().Name);

                            goto case State.OnPerformedNext;
                        }

                    case State.OnPerformedSync:
                        {
                            Debug.Assert(data != null);

                            syncFilter = (IServerFilter)data;
                            _log.DebugFormat("enter '{0}.OnPerformed'", syncFilter.GetType().Name);

                            syncFilter.OnPerformed(_performedContext);

                            _log.DebugFormat("leave '{0}.OnPerformed'", syncFilter.GetType().Name);

                            goto case State.OnPerformedNext;
                        }

                    case State.End:
                        {
                            state = State.End;
                            return Task.FromResult(0);
                        }

                    default:
                        throw new InvalidOperationException("Invalid state");
                }
            }

            private Task ExceptionFilters(ref State state, ref object data)
            {
                Debug.Assert(_exceptionContext != null, "exceptionContext not initialized");

                Dual<IServerExceptionFilter, IAsyncServerExceptionFilter> filter;
                IServerExceptionFilter syncFilter; IAsyncServerExceptionFilter asyncFilter;
                Task task;

                switch (state)
                {
                    case State.Begin:
                        {
                            _filters.Reset();
                            goto case State.OnServerExceptionNext;
                        }

                    case State.OnServerExceptionNext:
                        {
                            filter = _filters.GetNextFilter<IServerExceptionFilter, IAsyncServerExceptionFilter>();
                            if (filter.NotFound)
                            {
                                goto case State.End;
                            }
                            else if (filter.Async != null)
                            {
                                data = filter.Async;
                                goto case State.OnServerExceptionAsyncBegin;
                            }
                            else
                            {
                                data = filter.Sync;
                                goto case State.OnServerExceptionSync;
                            }
                        }

                    case State.OnServerExceptionAsyncBegin:
                        {
                            Debug.Assert(data != null);

                            asyncFilter = (IAsyncServerExceptionFilter)data;
                            _log.DebugFormat("enter '{0}.OnServerExceptionAsync'", asyncFilter.GetType().Name);

                            task = asyncFilter.OnServerExceptionAsync(_exceptionContext);
                            if (task.Status != TaskStatus.RanToCompletion)
                            {
                                state = State.OnServerExceptionAsyncEnd;
                                return task;
                            }

                            goto case State.OnServerExceptionAsyncEnd;
                        }

                    case State.OnServerExceptionAsyncEnd:
                        {
                            Debug.Assert(data != null);

                            asyncFilter = (IAsyncServerExceptionFilter)data;
                            _log.DebugFormat("leave '{0}.OnServerExceptionAsync'", asyncFilter.GetType().Name);

                            goto case State.OnServerExceptionNext;
                        }

                    case State.OnServerExceptionSync:
                        {
                            Debug.Assert(data != null);

                            syncFilter = (IServerExceptionFilter)data;
                            _log.DebugFormat("enter '{0}.OnServerException'", syncFilter.GetType().Name);

                            syncFilter.OnServerException(_exceptionContext);

                            _log.DebugFormat("leave '{0}.OnServerException'", syncFilter.GetType().Name);

                            goto case State.OnServerExceptionNext;
                        }

                    case State.End:
                        {
                            state = State.End;
                            return Task.FromResult(0);
                        }

                    default:
                        throw new InvalidOperationException("Invalid state");
                }
            }
            
            private static void FillPerformedContext(Task<object> task, object state)
            {
                var self = (Performance)state;

                self._performedContext = task.IsFaulted 
                    ? new PerformedContext(self._context, null, false, task.Exception.InnerException)
                    : new PerformedContext(self._context, task.Result, false, null);
            }
            
            private Task PerformJobAsync()
            {
                const TaskContinuationOptions options = TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.ExecuteSynchronously;

                try
                {
                    return _innerPerformer.PerformAsync(_context).ContinueWith(FillPerformedContext, this, options);
                }
                catch (Exception ex)
                {
                    // fill performedContext with exception if failed to create task at all
                    _performedContext = new PerformedContext(_context, null, false, ex);
                    return Task.FromResult(0);
                }
            }

            private enum State
            {
                Begin,

                // OnPerforming states:
                OnPerformingNext,
                OnPerformingAsyncBegin,
                OnPerformingAsyncEnd,
                OnPerformingSync,
                OnPerformingCheckCancel,

                // OnCancel states:
                OnCancelPrev,
                OnCancelAsyncBegin,
                OnCancelAsyncEnd,
                OnCancelSync,

                // Perform states:
                PerformAsyncBegin,
                PerformAsyncEnd,

                // OnPerformed states:
                OnPerformedNext,
                OnPerformedAsyncBegin,
                OnPerformedAsyncEnd,
                OnPerformedSync,

                // OnServerException states:
                OnServerExceptionNext,
                OnServerExceptionAsyncBegin,
                OnServerExceptionAsyncEnd,
                OnServerExceptionSync,
                
                End
            }
        }
    }
}
