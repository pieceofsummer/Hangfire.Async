using Hangfire.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure
{
    public interface ITaskSource
    {
        Task CreateTask(BackgroundProcessContext context);
    }
}
