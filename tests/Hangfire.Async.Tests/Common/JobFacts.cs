using Hangfire.Common;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Hangfire.Async.Tests.Common
{
    public class JobFacts
    {
        private static readonly DateTime SomeDateTime = new DateTime(2014, 5, 30, 12, 0, 0);

        private static bool _methodInvoked;
        private static bool _disposed;
        
        private static void PrivateMethod()
        {
        }

        public static void MethodWithReferenceParameter(ref string a)
        {
        }

        public static void MethodWithOutputParameter(out string a)
        {
            a = "hello";
        }

        public static void StaticMethod()
        {
            _methodInvoked = true;
        }

        public void InstanceMethod()
        {
            _methodInvoked = true;
        }

        public static void CancelableJob(IJobCancellationToken token)
        {
            token.ThrowIfCancellationRequested();
        }
        
        public static void NullArgumentMethod(string[] argument)
        {
            _methodInvoked = true;
            Assert.Null(argument);
        }

        public void MethodWithArguments(string stringArg, int intArg)
        {
            _methodInvoked = true;
            Assert.Equal("hello", stringArg);
            Assert.Equal(5, intArg);
        }

        public void MethodWithObjectArgument(object argument)
        {
            _methodInvoked = true;
            Assert.Equal("5", argument);
        }

        public void MethodWithDateTimeArgument(DateTime argument)
        {
            _methodInvoked = true;

            Assert.Equal(SomeDateTime, argument);
        }

        public void MethodWithCustomArgument(Instance argument)
        {
        }
        
        public static void ExceptionMethod()
        {
            throw new InvalidOperationException("exception");
        }

        public static void TaskCanceledExceptionMethod()
        {
            throw new TaskCanceledException();
        }

        public void GenericMethod<T>(T arg)
        {
        }

        public Task AsyncMethod()
        {
            var source = new TaskCompletionSource<bool>();
            return source.Task;
        }

        public async void AsyncVoidMethod()
        {
            await Task.Yield();
        }

        [TestType]
        public class Instance : IDisposable
        {
            [TestMethod]
            public void Method()
            {
                _methodInvoked = true;
            }

            public void Dispose()
            {
                _disposed = true;
            }

            public string FunctionReturningValue()
            {
                return "Return value";
            }

            public async Task FunctionReturningTask()
            {
                await Task.Yield();
            }

            public async Task<string> FunctionReturningTaskResultingInString()
            {
                await Task.Yield();

                return FunctionReturningValue();
            }
        }
        
        public class DerivedInstance : Instance
        {
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

        // ReSharper disable once UnusedTypeParameter
        public class JobClassWrapper<T> : IDisposable where T : IDisposable
        {
            public void Dispose()
            {
            }
        }

        public class TestTypeAttribute : JobFilterAttribute
        {
        }

        public class TestMethodAttribute : JobFilterAttribute
        {
        }
    }

}
