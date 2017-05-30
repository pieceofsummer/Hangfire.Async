using Hangfire.Server;
using System.Threading.Tasks;

namespace Hangfire.Async.Server
{
    /// <summary>
    /// Defines methods that are required for an asynchronous server filter.
    /// </summary>
    public interface IAsyncServerFilter
    {
        /// <summary>
        /// Called before the performance of the job.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        Task OnPerformingAsync(PerformingContext filterContext);

        /// <summary>
        /// Called after the performance of the job.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        Task OnPerformedAsync(PerformedContext filterContext);
    }
}
