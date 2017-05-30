namespace Hangfire.Async.Server.Infrastructure
{
    internal interface IBackgroundTaskWrapper : IBackgroundTask
    {
        IBackgroundTask InnerTask { get; }
    }
}
