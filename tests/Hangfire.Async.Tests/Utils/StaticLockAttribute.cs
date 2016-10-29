using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace Hangfire.Async.Tests.Utils
{
    internal class StaticLockAttribute : BeforeAfterTestAttribute
    {
        private readonly ConcurrentDictionary<Type, object> _locks
            = new ConcurrentDictionary<Type, object>();

        public override void Before(MethodInfo methodUnderTest)
        {
            var type = GetType(methodUnderTest);
            _locks.TryAdd(type, new object());

            Monitor.Enter(_locks[type]);
        }

        public override void After(MethodInfo methodUnderTest)
        {
            Monitor.Exit(_locks[GetType(methodUnderTest)]);
        }

        private static Type GetType(MethodInfo methodInfo)
        {
            return methodInfo.DeclaringType;
        }
    }
}
