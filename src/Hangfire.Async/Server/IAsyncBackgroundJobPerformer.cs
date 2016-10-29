using Hangfire.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hangfire.Async.Server
{
    public interface IAsyncBackgroundJobPerformer
    {
        Task<object> PerformAsync(PerformContext context);
    }
}
