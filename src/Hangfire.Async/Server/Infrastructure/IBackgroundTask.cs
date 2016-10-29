using Hangfire.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure
{
    internal interface IBackgroundTask
    {
        string Name { get; }

        Task ExecuteAsync(BackgroundProcessContext context);
    }
}
