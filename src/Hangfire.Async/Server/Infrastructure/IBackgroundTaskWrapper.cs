using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure
{
    internal interface IBackgroundTaskWrapper : IBackgroundTask
    {
        IBackgroundTask InnerTask { get; }
    }
}
