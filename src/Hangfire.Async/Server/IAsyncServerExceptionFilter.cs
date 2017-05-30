using Hangfire.Server;
using System.Threading.Tasks;

namespace Hangfire.Async.Server
{
    /// <summary>
    /// Defines methods that are required for the server exception filter.
    /// </summary>
    public interface IAsyncServerExceptionFilter
    {
        /// <summary>
        /// Called when an exception occurred during the performance of the job.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        Task OnServerExceptionAsync(ServerExceptionContext filterContext);
    }
}
