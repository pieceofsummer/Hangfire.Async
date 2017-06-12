using Hangfire.Async.Server;
using Hangfire.Async.Tests.Mocks;
using Hangfire.Common;
using Hangfire.Server;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Hangfire.Async.Tests.Server
{
    public class AsyncBackgroundJobPerformerFacts
    {
        private readonly PerformContextMock _context;
        private readonly Mock<IAsyncBackgroundJobPerformer> _innerPerformer;
        private readonly IList<object> _filters;
        private readonly Mock<IJobFilterProvider> _filterProvider;

        public AsyncBackgroundJobPerformerFacts()
        {
            _context = new PerformContextMock();

            _innerPerformer = new Mock<IAsyncBackgroundJobPerformer>();
            _innerPerformer.Setup(x => x.PerformAsync(It.IsAny<PerformContext>()))
                .ReturnsAsync(() => null);

            _filters = new List<object>();
            _filterProvider = new Mock<IJobFilterProvider>();
            _filterProvider.Setup(x => x.GetFilters(It.IsNotNull<Job>())).Returns(
                _filters.Select(f => new JobFilter(f, JobFilterScope.Type, null)));
        }

        [Fact]
        public void Ctor_ThrowsAnException_WhenFilterProvider_IsNull()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => new AsyncBackgroundJobPerformer(null, _innerPerformer.Object));

            Assert.Equal("filterProvider", exception.ParamName);
        }

        [Fact]
        public void Ctor_ThrowsAnException_WhenInnerPerformer_IsNull()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => new AsyncBackgroundJobPerformer(_filterProvider.Object, (IAsyncBackgroundJobPerformer)null));

            Assert.Equal("innerPerformer", exception.ParamName);
        }

        [Fact]
        public async Task Run_ThrowsAnException_WhenContextIsNull()
        {
            var performer = CreatePerformer();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => performer.PerformAsync(null));

            Assert.Equal("context", exception.ParamName);
        }

        [Fact]
        public async Task Run_CallsTheRunMethod_OfInnerPerformer()
        {
            var performer = CreatePerformer();

            await performer.PerformAsync(_context.Object);

            _innerPerformer.Verify(x => x.PerformAsync(_context.Object), Times.Once);
        }

        [Fact]
        public async Task Run_StoresJobReturnValueInPerformedContext()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();
            var performer = CreatePerformer();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Returns(Task.FromResult<object>("Returned value"));

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert
            filter.Verify(
                x => x.OnPerformed(It.Is<PerformedContext>(context => (string)context.Result == "Returned value")));
        }

        [Fact]
        public async Task Run_StoresAsyncJobReturnValueInPerformedContext()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();
            var performer = CreatePerformer();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ReturnsAsync("Returned value");

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert
            filter.Verify(
                x => x.OnPerformed(It.Is<PerformedContext>(context => (string)context.Result == "Returned value")));
        }

        [Fact]
        public async Task Run_ReturnsValueReturnedByJob()
        {
            // Arrange
            // ReSharper disable once UnusedVariable
            var filter = CreateFilter<IServerFilter>();
            var performer = CreatePerformer();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Returns(Task.FromResult<object>("Returned value"));

            // Act
            var result = await performer.PerformAsync(_context.Object);

            // Assert
            Assert.Equal("Returned value", result);
        }

        [Fact]
        public async Task Run_ReturnsValueReturnedByAsyncJob()
        {
            // Arrange
            // ReSharper disable once UnusedVariable
            var filter = CreateFilter<IServerFilter>();
            var performer = CreatePerformer();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ReturnsAsync("Returned value");

            // Act
            var result = await performer.PerformAsync(_context.Object);

            // Assert
            Assert.Equal("Returned value", result);
        }

        [Fact]
        public Task Run_DoesNotCatchExceptions()
        {
            // Arrange
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Throws<InvalidOperationException>();

            var performer = CreatePerformer();

            // Act & Assert
            return Assert.ThrowsAsync<InvalidOperationException>(() => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public Task Run_DoesNotCatchAsyncExceptions()
        {
            // Arrange
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ThrowsAsync(new InvalidOperationException());

            var performer = CreatePerformer();

            // Act & Assert
            return Assert.ThrowsAsync<InvalidOperationException>(() => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public async Task Run_CallsExceptionFilter_OnException()
        {
            // Arrange
            var filter = CreateFilter<IServerExceptionFilter>();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Throws<InvalidOperationException>();

            var performer = CreatePerformer();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => performer.PerformAsync(_context.Object));

            filter.Verify(x => x.OnServerException(It.Is<ServerExceptionContext>(context =>
                context.Exception is InvalidOperationException)));
        }

        [Fact]
        public async Task Run_CallsExceptionFilter_OnAsyncException()
        {
            // Arrange
            var filter = CreateFilter<IServerExceptionFilter>();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ThrowsAsync(new InvalidOperationException());

            var performer = CreatePerformer();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => performer.PerformAsync(_context.Object));

            filter.Verify(x => x.OnServerException(It.Is<ServerExceptionContext>(context =>
                context.Exception is InvalidOperationException)));
        }

        [Fact]
        public async Task Run_CallsAsyncExceptionFilter_OnException()
        {
            // Arrange
            var filter = CreateFilter<IAsyncServerExceptionFilter>();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Throws<InvalidOperationException>();

            var performer = CreatePerformer();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => performer.PerformAsync(_context.Object));

            filter.Verify(x => x.OnServerExceptionAsync(It.Is<ServerExceptionContext>(context =>
                context.Exception is InvalidOperationException)));
        }

        [Fact]
        public async Task Run_CallsAsyncExceptionFilter_OnAsyncException()
        {
            // Arrange
            var filter = CreateFilter<IAsyncServerExceptionFilter>();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ThrowsAsync(new InvalidOperationException());

            var performer = CreatePerformer();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => performer.PerformAsync(_context.Object));

            filter.Verify(x => x.OnServerExceptionAsync(It.Is<ServerExceptionContext>(context =>
                context.Exception is InvalidOperationException)));
        }

        [Fact]
        public Task Run_EatsException_WhenItWasHandlerByExceptionFilter()
        {
            // Arrange
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Throws<InvalidOperationException>();

            var filter = CreateFilter<IServerExceptionFilter>();
            filter.Setup(x => x.OnServerException(It.IsAny<ServerExceptionContext>()))
                .Callback((ServerExceptionContext x) => x.ExceptionHandled = true);

            var performer = CreatePerformer();

            // Act & Assert does not throw
            return performer.PerformAsync(_context.Object);
        }

        [Fact]
        public Task Run_EatsException_WhenItWasHandlerByAsyncExceptionFilter()
        {
            // Arrange
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Throws<InvalidOperationException>();

            var filter = CreateFilter<IAsyncServerExceptionFilter>();
            filter.Setup(x => x.OnServerExceptionAsync(It.IsAny<ServerExceptionContext>()))
                .Callback((ServerExceptionContext x) => x.ExceptionHandled = true)
                .Returns(Task.FromResult(0));

            var performer = CreatePerformer();

            // Act & Assert does not throw
            return performer.PerformAsync(_context.Object);
        }

        [Fact]
        public async Task Run_DoesNotCallBoth_Perform_And_OnPerforming_WhenFilterCancelsThis()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();

            filter.Setup(x => x.OnPerforming(It.IsAny<PerformingContext>()))
                .Callback((PerformingContext x) => x.Canceled = true);

            var performer = CreatePerformer();

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert
            _innerPerformer.Verify(x => x.PerformAsync(_context.Object), Times.Never);

            filter.Verify(x => x.OnPerformed(It.IsAny<PerformedContext>()), Times.Never);
        }

        [Fact]
        public async Task Run_TellsPreviousFilters_AboutCancellation()
        {
            // Arrange
            var prevFilter1 = CreateFilter<IServerFilter>();
            var prevFilter2 = CreateFilter<IAsyncServerFilter>();
            var filter = CreateFilter<IServerFilter>();
            var nextFilter = CreateFilter<IServerFilter>();

            filter.Setup(x => x.OnPerforming(It.IsAny<PerformingContext>()))
                .Callback((PerformingContext context) => context.Canceled = true);

            var performer = CreatePerformer();

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert
            prevFilter1.Verify(x => x.OnPerforming(It.IsAny<PerformingContext>()), Times.Once);
            prevFilter1.Verify(x => x.OnPerformed(It.Is<PerformedContext>(context => context.Canceled)), Times.Once);

            prevFilter2.Verify(x => x.OnPerformingAsync(It.IsAny<PerformingContext>()), Times.Once);
            prevFilter2.Verify(x => x.OnPerformedAsync(It.Is<PerformedContext>(context => context.Canceled)), Times.Once);

            _innerPerformer.Verify(x => x.PerformAsync(It.IsAny<PerformContext>()), Times.Never);

            filter.Verify(x => x.OnPerformed(It.IsAny<PerformedContext>()), Times.Never);

            nextFilter.Verify(x => x.OnPerforming(It.IsAny<PerformingContext>()), Times.Never);
            nextFilter.Verify(x => x.OnPerformed(It.IsAny<PerformedContext>()), Times.Never);
        }

        [Fact]
        public async Task Run_DoesNotCall_Perform_And_OnPerformed_WhenExceptionOccured_DuringPerformingPhase()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();

            filter.Setup(x => x.OnPerforming(It.IsAny<PerformingContext>()))
                .Throws<InvalidOperationException>();

            var performer = CreatePerformer();

            // Act
            var exception = await Assert.ThrowsAsync<JobPerformanceException>(
                () => performer.PerformAsync(_context.Object));

            // Assert
            Assert.IsType<InvalidOperationException>(exception.InnerException);

            _innerPerformer.Verify(x => x.PerformAsync(It.IsAny<PerformContext>()), Times.Never);

            filter.Verify(x => x.OnPerformed(It.IsAny<PerformedContext>()), Times.Never);
        }

        [Fact]
        public async Task Run_TellsAllFilters_AboutException_WhenItOccured_DuringThePerformanceOfAJob()
        {
            // Arrange
            var filter1 = CreateFilter<IServerFilter>();
            var filter2 = CreateFilter<IAsyncServerFilter>();

            var exception = new InvalidOperationException();
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ThrowsAsync(exception);

            var performer = CreatePerformer();

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => performer.PerformAsync(_context.Object));

            // Assert
            filter1.Verify(
                x => x.OnPerformed(It.Is<PerformedContext>(context => context.Exception == exception)));
            filter2.Verify(
                x => x.OnPerformedAsync(It.Is<PerformedContext>(context => context.Exception == exception)));
        }
        
        [Fact, Obsolete]
        public async Task Run_TellsAllFilters_AboutAllExceptions()
        {
            // Arrange
            var outerFilter = CreateFilter<IServerFilter>();
            // ReSharper disable once UnusedVariable
            var innerFilter = CreateFilter<IServerFilter>();

            var exception = new InvalidOperationException();
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ThrowsAsync(exception);

            var performer = CreatePerformer();

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => performer.PerformAsync(_context.Object));

            outerFilter.Verify(x => x.OnPerformed(It.Is<PerformedContext>(context => context.Exception == exception)));
        }

        [Fact]
        public async Task Run_TellsAllFilters_AboutException_EvenIfItWasHandled()
        {
            // Arrange
            var filter1 = CreateFilter<IServerFilter>();
            var filter2 = CreateFilter<IServerFilter>();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ThrowsAsync(new InvalidOperationException());

            filter1.Setup(x => x.OnPerformed(It.IsAny<PerformedContext>()))
                .Callback((PerformedContext x) => x.ExceptionHandled = true);

            var performer = CreatePerformer();

            // Act
            await performer.PerformAsync(_context.Object);

            // Assert
            filter2.Verify(x => x.OnPerformed(It.Is<PerformedContext>(context => context.Exception != null)));
        }
        
        [Fact]
        public Task<object> Run_DoesNotThrow_HandledExceptions()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();

            var exception = new InvalidOperationException();
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ThrowsAsync(exception);

            filter.Setup(x => x.OnPerformed(It.Is<PerformedContext>(context => context.Exception == exception)))
                .Callback((PerformedContext x) => x.ExceptionHandled = true);

            var performer = CreatePerformer();

            // Act & Assert does not throw
            return performer.PerformAsync(_context.Object);
        }
        
        [Fact]
        public async Task Run_WrapsOnPerformedException_IntoJobPerformanceException()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();
            filter.Setup(x => x.OnPerformed(It.IsAny<PerformedContext>()))
                .Throws<InvalidOperationException>();

            var performer = CreatePerformer();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<JobPerformanceException>(() =>
                performer.PerformAsync(_context.Object));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        [Fact]
        public async Task Run_WrapsOnPerformedException_OccuredAfterAnotherException_IntoJobPerformanceException()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();
            filter.Setup(x => x.OnPerformed(It.IsAny<PerformedContext>()))
                .Throws<InvalidOperationException>();

            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .ThrowsAsync(new ArgumentNullException());

            var performer = CreatePerformer();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<JobPerformanceException>(() =>
                performer.PerformAsync(_context.Object));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        [Fact]
        public async Task Run_ExceptionFiltersAreNOTInvoked_OnJobAbortedException()
        {
            // Arrange
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Throws<JobAbortedException>();

            var filter = CreateFilter<IServerExceptionFilter>();
            var performer = CreatePerformer();

            // Act
            await Assert.ThrowsAsync<JobAbortedException>(() => performer.PerformAsync(_context.Object));

            // Assert
            filter.Verify(
                x => x.OnServerException(It.IsAny<ServerExceptionContext>()), Times.Never);
        }

        [Fact]
        public async Task Run_ExceptionFiltersAreNOTInvoked_OnOperationCanceledException_WhenShutdownTokenIsCanceled()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _context.CancellationToken.Setup(x => x.ShutdownToken).Returns(cts.Token);
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Throws<OperationCanceledException>();

            var filter = CreateFilter<IServerExceptionFilter>();
            var performer = CreatePerformer();

            // Act
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => performer.PerformAsync(_context.Object));

            // Assert
            filter.Verify(
                x => x.OnServerException(It.IsAny<ServerExceptionContext>()),
                Times.Never);
        }

        [Fact]
        public async Task Run_ExceptionFiltersAreInvoked_OnOperationCanceledException_WhenShutdownTokenIsNOTCanceled()
        {
            // Arrange
            _innerPerformer
                .Setup(x => x.PerformAsync(_context.Object))
                .Throws<OperationCanceledException>();

            var filter = CreateFilter<IServerExceptionFilter>();
            var performer = CreatePerformer();

            // Act
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => performer.PerformAsync(_context.Object));

            // Assert
            filter.Verify(
                x => x.OnServerException(It.IsAny<ServerExceptionContext>()),
                Times.Once);
        }

        [Fact]
        public Task Run_ThrowsOperationCanceledException_OccurredInPreFilterMethods_WhenShutdownTokenIsCanceled()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _context.CancellationToken.Setup(x => x.ShutdownToken).Returns(cts.Token);
            var filter = CreateFilter<IServerFilter>();
            filter.Setup(x => x.OnPerforming(It.IsAny<PerformingContext>()))
                .Throws<OperationCanceledException>();

            var performer = CreatePerformer();

            // Act & Assert
            return Assert.ThrowsAsync<OperationCanceledException>(
                () => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public async Task Run_ThrowsJobPerformanceException_InsteadOfOperationCanceled_OccurredInPreFilterMethods_WhenShutdownTokenIsNotCanceled()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();
            filter.Setup(x => x.OnPerforming(It.IsAny<PerformingContext>()))
                .Throws<OperationCanceledException>();

            var performer = CreatePerformer();

            // Act
            var exception = await Assert.ThrowsAsync<JobPerformanceException>(
                () => performer.PerformAsync(_context.Object));

            // Assert
            Assert.IsType<OperationCanceledException>(exception.InnerException);
        }

        [Fact]
        public Task Run_ThrowsOperationCanceledException_OccurredInPostFilterMethods_WhenShutdownTokenIsCanceled()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _context.CancellationToken.Setup(x => x.ShutdownToken).Returns(cts.Token);
            var filter = CreateFilter<IServerFilter>();
            filter.Setup(x => x.OnPerformed(It.IsAny<PerformedContext>()))
                .Throws<OperationCanceledException>();

            var performer = CreatePerformer();

            // Act & Assert
            return Assert.ThrowsAsync<OperationCanceledException>(() => performer.PerformAsync(_context.Object));
        }

        [Fact]
        public async Task Run_ThrowsJobPerformanceException_InsteadOfOperationCanceled_OccurredInPostFilterMethods_WhenShutdownTokenIsNOTCanceled()
        {
            // Arrange
            var filter = CreateFilter<IServerFilter>();
            filter.Setup(x => x.OnPerformed(It.IsAny<PerformedContext>()))
                .Throws<OperationCanceledException>();

            var performer = CreatePerformer();

            // Act
            var exception = await Assert.ThrowsAsync<JobPerformanceException>(
                () => performer.PerformAsync(_context.Object));

            // Assert
            Assert.IsType<OperationCanceledException>(exception.InnerException);
        }

        public static void Method()
        {
        }

        private AsyncBackgroundJobPerformer CreatePerformer()
        {
            return new AsyncBackgroundJobPerformer(_filterProvider.Object, _innerPerformer.Object);
        }

        private Mock<T> CreateFilter<T>()
            where T : class
        {
            var filter = new Mock<T>();
            _filters.Add(filter.Object);

            return filter;
        }
    }
}
