using Hangfire.Async.Server;
using Hangfire.Async.Tests.Mocks;
using Hangfire.Async.Tests.Utils;
using Hangfire.Common;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using System.Reflection;
using Hangfire.Server;
using System.Threading;
using Hangfire.Async.Tests.Common;

namespace Hangfire.Async.Tests.Server
{
    public class CoreAsyncBackgroundJobPerformerFacts : IDisposable
    {
        private readonly Mock<JobActivator> _activator;
        private readonly PerformContextMock _context;

        private static readonly DateTime SomeDateTime = new DateTime(2014, 5, 30, 12, 0, 0);
        private static bool _methodInvoked;
        private static bool _disposed;

        public CoreAsyncBackgroundJobPerformerFacts()
        {
            _activator = new Mock<JobActivator>() { CallBase = true };
            _context = new PerformContextMock();
        }

        [Fact]
        public void Ctor_ThrowsAnException_WhenActivatorIsNull()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                // ReSharper disable once AssignNullToNotNullAttribute
                () => new CoreAsyncBackgroundJobPerformer(null));

            Assert.Equal("activator", exception.ParamName);
        }

        [Fact, StaticLock]
        public async Task Perform_CanInvokeStaticMethods()
        {
            _methodInvoked = false;
            _context.BackgroundJob.Job = Job.FromExpression(() => StaticMethod());
            var performer = CreatePerformer();

            await performer.PerformAsync(_context.Object);

            Assert.True(_methodInvoked);
        }

        [Fact, StaticLock]
        public async Task Perform_CanInvokeInstanceMethods()
        {
            _methodInvoked = false;
            _context.BackgroundJob.Job = Job.FromExpression<CoreAsyncBackgroundJobPerformerFacts>(x => x.InstanceMethod());
            var performer = CreatePerformer();

            await performer.PerformAsync(_context.Object);

            Assert.True(_methodInvoked);
        }

        [Fact, StaticLock]
        public async Task Perform_ActivatesJob_WithinAScope()
        {
            var performer = CreatePerformer();
            _context.BackgroundJob.Job = Job.FromExpression<CoreAsyncBackgroundJobPerformerFacts>(x => x.InstanceMethod());

            await performer.PerformAsync(_context.Object);

            //_activator.Verify(x => x.BeginScope(It.IsNotNull<JobActivatorContext>()), Times.Once);
            _activator.Verify(x => x.BeginScope(), Times.Once);
        }

        [Fact, StaticLock]
        public async Task Perform_DisposesDisposableInstance_AfterPerformance()
        {
            _disposed = false;
            _context.BackgroundJob.Job = Job.FromExpression<Disposable>(x => x.Method());
            var performer = CreatePerformer();

            await performer.PerformAsync(_context.Object);

            Assert.True(_disposed);
        }

        [Fact, StaticLock]
        public async Task Perform_PassesArguments_ToACallingMethod()
        {
            // Arrange
            _methodInvoked = false;
            _context.BackgroundJob.Job = Job.FromExpression(() => MethodWithArguments("hello", 5));
            var performer = CreatePerformer();

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert - see the `MethodWithArguments` method.
            Assert.True(_methodInvoked);
        }

#if NETFULL
        [Fact, StaticLock]
        public async Task Perform_PassesCorrectDateTime_IfItWasSerialized_UsingTypeConverter()
        {
            // Arrange
            _methodInvoked = false;
            var typeConverter = TypeDescriptor.GetConverter(typeof(DateTime));
            var convertedDate = typeConverter.ConvertToInvariantString(SomeDateTime);

            var type = typeof(CoreBackgroundJobPerformerFacts);
            var method = type.GetMethod("MethodWithDateTimeArgument");

#pragma warning disable CS0618 // Type or member is obsolete
            _context.BackgroundJob.Job = new Job(type, method, new [] { convertedDate });
#pragma warning restore CS0618 // Type or member is obsolete
            var performer = CreatePerformer();

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert - see also the `MethodWithDateTimeArgument` method.
            Assert.True(_methodInvoked);
        }
#endif

        [Fact, StaticLock]
        public async Task Perform_PassesCorrectDateTime_IfItWasSerialized_UsingOldFormat()
        {
            // Arrange
            _methodInvoked = false;
            var convertedDate = SomeDateTime.ToString("MM/dd/yyyy HH:mm:ss.ffff");

            var type = typeof(CoreAsyncBackgroundJobPerformerFacts);
            var method = type.GetMethod("MethodWithDateTimeArgument");

#pragma warning disable CS0618 // Type or member is obsolete
            _context.BackgroundJob.Job = new Job(type, method, new[] { convertedDate });
#pragma warning restore CS0618 // Type or member is obsolete
            var performer = CreatePerformer();

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert - see also the `MethodWithDateTimeArgument` method.
            Assert.True(_methodInvoked);
        }

        [Fact, StaticLock]
        public async Task Perform_PassesCorrectDateTimeArguments()
        {
            // Arrange
            _methodInvoked = false;
            _context.BackgroundJob.Job = Job.FromExpression(() => MethodWithDateTimeArgument(SomeDateTime));
            var performer = CreatePerformer();

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert - see also the `MethodWithDateTimeArgument` method.
            Assert.True(_methodInvoked);
        }

        [Fact, StaticLock]
        public async Task Perform_WorksCorrectly_WithNullValues()
        {
            // Arrange
            _methodInvoked = false;
            _context.BackgroundJob.Job = Job.FromExpression(() => NullArgumentMethod(null));

            var performer = CreatePerformer();
            // Act
            await performer.PerformAsync(_context.Object);

            // Assert - see also `NullArgumentMethod` method.
            Assert.True(_methodInvoked);
        }

        [Fact]
        public Task Perform_ThrowsException_WhenActivatorThrowsAnException()
        {
            // Arrange
            var exception = new InvalidOperationException();
            _activator.Setup(x => x.ActivateJob(It.IsAny<Type>())).Throws(exception);

            _context.BackgroundJob.Job = Job.FromExpression(() => InstanceMethod());
            var performer = CreatePerformer();

            // Act
            return Assert.ThrowsAsync<InvalidOperationException>(
                () => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public Task Perform_ThrowsPerformanceException_WhenActivatorReturnsNull()
        {
            _activator.Setup(x => x.ActivateJob(It.IsNotNull<Type>())).Returns(null);
            _context.BackgroundJob.Job = Job.FromExpression(() => InstanceMethod());
            var performer = CreatePerformer();

            return Assert.ThrowsAsync<InvalidOperationException>(
                () => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public async Task Perform_ThrowsPerformanceException_OnArgumentsDeserializationFailure()
        {
            var type = typeof(JobFacts);
            var method = type.GetMethod("MethodWithDateTimeArgument");
            _context.BackgroundJob.Job = new Job(type, method, "sdfa");
            var performer = CreatePerformer();

            var exception = await Assert.ThrowsAsync<JobPerformanceException>(
                () => performer.PerformAsync(_context.Object));

            Assert.NotNull(exception.InnerException);
        }

        [Fact, StaticLock]
        public async Task Perform_ThrowsPerformanceException_OnDisposalFailure()
        {
            _methodInvoked = false;
            _context.BackgroundJob.Job = Job.FromExpression<BrokenDispose>(x => x.Method());
            var performer = CreatePerformer();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => performer.PerformAsync(_context.Object));

            Assert.True(_methodInvoked);
        }

        [Fact]
        public async Task Perform_ThrowsPerformanceException_WithUnwrappedInnerException()
        {
            _context.BackgroundJob.Job = Job.FromExpression(() => ExceptionMethod());
            var performer = CreatePerformer();

            var thrownException = await Assert.ThrowsAsync<JobPerformanceException>(
                () => performer.PerformAsync(_context.Object));

            Assert.IsType<InvalidOperationException>(thrownException.InnerException);
            Assert.Equal("exception", thrownException.InnerException.Message);
        }

        [Fact]
        public async Task Run_ThrowsPerformanceException_WithUnwrappedInnerException_ForTasks()
        {
            _context.BackgroundJob.Job = Job.FromExpression(() => TaskExceptionMethod());
            var performer = CreatePerformer();

            var thrownException = await Assert.ThrowsAsync<JobPerformanceException>(
                () => performer.PerformAsync(_context.Object));

            Assert.IsType<InvalidOperationException>(thrownException.InnerException);
            Assert.Equal("exception", thrownException.InnerException.Message);
        }

        [Fact]
        public async Task Perform_ThrowsPerformanceException_WhenMethodThrownTaskCanceledException()
        {
            _context.BackgroundJob.Job = Job.FromExpression(() => TaskCanceledExceptionMethod());
            var performer = CreatePerformer();

            var thrownException = await Assert.ThrowsAsync<JobPerformanceException>(
                () => performer.PerformAsync(_context.Object));

            Assert.IsType<TaskCanceledException>(thrownException.InnerException);
        }

        [Fact]
        public Task Perform_RethrowsOperationCanceledException_WhenShutdownTokenIsCanceled()
        {
            // Arrange
            _context.BackgroundJob.Job = Job.FromExpression(() => CancelableJob(JobCancellationToken.Null));
            _context.CancellationToken.Setup(x => x.ShutdownToken).Returns(new CancellationToken(true));
            _context.CancellationToken.Setup(x => x.ThrowIfCancellationRequested()).Throws<OperationCanceledException>();

            var performer = CreatePerformer();

            // Act & Assert
            return Assert.ThrowsAsync<OperationCanceledException>(() => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public Task Run_RethrowsTaskCanceledException_WhenShutdownTokenIsCanceled()
        {
            // Arrange
            _context.BackgroundJob.Job = Job.FromExpression(() => CancelableJob(JobCancellationToken.Null));
            _context.CancellationToken.Setup(x => x.ShutdownToken).Returns(new CancellationToken(true));
            _context.CancellationToken.Setup(x => x.ThrowIfCancellationRequested()).Throws<TaskCanceledException>();

            var performer = CreatePerformer();

            // Act & Assert
            return Assert.ThrowsAsync<TaskCanceledException>(() => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public Task Run_RethrowsJobAbortedException()
        {
            // Arrange
            _context.BackgroundJob.Job = Job.FromExpression(() => CancelableJob(JobCancellationToken.Null));
            _context.CancellationToken.Setup(x => x.ShutdownToken).Returns(CancellationToken.None);
            _context.CancellationToken.Setup(x => x.ThrowIfCancellationRequested()).Throws<JobAbortedException>();

            var performer = CreatePerformer();

            // Act & Assert
            return Assert.ThrowsAsync<JobAbortedException>(() => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public Task Run_ThrowsJobPerformanceException_InsteadOfOperationCanceled_WhenShutdownWasNOTInitiated()
        {
            // Arrange
            _context.BackgroundJob.Job = Job.FromExpression(() => CancelableJob(JobCancellationToken.Null));
            _context.CancellationToken.Setup(x => x.ShutdownToken).Returns(CancellationToken.None);
            _context.CancellationToken.Setup(x => x.ThrowIfCancellationRequested()).Throws<OperationCanceledException>();

            var performer = CreatePerformer();

            // Act & Assert
            return Assert.ThrowsAsync<JobPerformanceException>(() => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public Task Run_PassesStandardCancellationToken_IfThereIsCancellationTokenParameter()
        {
            // Arrange
            _context.BackgroundJob.Job = Job.FromExpression(() => CancelableJob(default(CancellationToken)));
            var source = new CancellationTokenSource();
            _context.CancellationToken.Setup(x => x.ShutdownToken).Returns(source.Token);
            var performer = CreatePerformer();

            // Act & Assert
            source.Cancel();
            return Assert.ThrowsAsync<OperationCanceledException>(
                () => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public async Task Perform_ReturnsValue_WhenCallingFunctionReturningValue()
        {
            _context.BackgroundJob.Job = Job.FromExpression<JobFacts.Instance>(x => x.FunctionReturningValue());
            var performer = CreatePerformer();

            var result = await performer.PerformAsync(_context.Object);

            Assert.Equal("Return value", result);
        }

        [Fact]
        public async Task Run_DoesNotReturnValue_WhenCallingFunctionReturningPlainTask()
        {
            _context.BackgroundJob.Job = Job.FromExpression<JobFacts.Instance>(x => x.FunctionReturningTask());
            var performer = CreatePerformer();

            var result = await performer.PerformAsync(_context.Object);

            Assert.Equal(null, result);
        }

        [Fact]
        public async Task Run_ReturnsTaskResult_WhenCallingFunctionReturningGenericTask()
        {
            _context.BackgroundJob.Job = Job.FromExpression<JobFacts.Instance>(x => x.FunctionReturningTaskResultingInString());
            var performer = CreatePerformer();

            var result = await performer.PerformAsync(_context.Object);

            Assert.Equal("Return value", result);
        }

        [Fact]
        public void Run_ReturnsTaskResult_InWaitingForActivationState()
        {
            _context.BackgroundJob.Job = Job.FromExpression<JobFacts.Instance>(x => x.FunctionReturningTaskResultingInString());
            var performer = CreatePerformer();

            var result = performer.PerformAsync(_context.Object);

            Assert.Equal(TaskStatus.WaitingForActivation, result.Status);
        }

        [Fact]
        public Task Run_DoesNotDisposeScopeBeforeCompletion()
        {
            _context.BackgroundJob.Job = Job.FromExpression<AsyncDisposable>(x => x.CheckDisposedAsync());
            var performer = CreatePerformer();

            return performer.PerformAsync(_context.Object);
        }

        public void InstanceMethod()
        {
            _methodInvoked = true;
        }

        public class Disposable : IDisposable
        {
            public void Method()
            {
                _methodInvoked = true;
            }

            public void Dispose()
            {
                _disposed = true;
            }
        }

        public class BrokenDispose : IDisposable
        {
            public void Method()
            {
                _methodInvoked = true;
            }

            public void Dispose()
            {
                throw new InvalidOperationException();
            }
        }

        private class AsyncDisposable : IDisposable
        {
            private bool _disposed = false;

            public void Dispose()
            {
                _disposed = true;
            }

            public async Task CheckDisposedAsync()
            {
                await Task.Delay(100);

                if (_disposed)
                    throw new ObjectDisposedException("instance");
            }
        }
        
        public void Dispose()
        {
            _disposed = true;
        }

        public static void NullArgumentMethod(string[] argument)
        {
            _methodInvoked = true;
            Assert.Null(argument);
        }

        public static void CancelableJob(IJobCancellationToken token)
        {
            token.ThrowIfCancellationRequested();
        }

        public static void CancelableJob(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
        }

        public void MethodWithDateTimeArgument(DateTime argument)
        {
            _methodInvoked = true;

            Assert.Equal(SomeDateTime, argument);
        }

        public static void StaticMethod()
        {
            _methodInvoked = true;
        }

        public void MethodWithArguments(string stringArg, int intArg)
        {
            _methodInvoked = true;

            Assert.Equal("hello", stringArg);
            Assert.Equal(5, intArg);
        }

        public static void ExceptionMethod()
        {
            throw new InvalidOperationException("exception");
        }

        public static async Task TaskExceptionMethod()
        {
            await Task.Yield();

            throw new InvalidOperationException("exception");
        }

        public static void TaskCanceledExceptionMethod()
        {
            throw new TaskCanceledException();
        }

        private CoreAsyncBackgroundJobPerformer CreatePerformer()
        {
            return new CoreAsyncBackgroundJobPerformer(_activator.Object);
        }
    }

}
