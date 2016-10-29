using Hangfire.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hangfire.Async.Server.Infrastructure.Compatibility
{
    internal class ServerComponentWrapper : ITaskSource
    {
#pragma warning disable CS0618 // Type or member is obsolete
        private readonly IServerComponent _component;

        public ServerComponentWrapper(IServerComponent component)
#pragma warning restore CS0618 // Type or member is obsolete
        {
            if (component == null) throw new ArgumentNullException(nameof(component));

            _component = component;
        }

        public Task CreateTask(BackgroundProcessContext context)
        {
            return Task.Factory.StartNew(
                Execute, context,
                TaskCreationOptions.LongRunning);
        }

        private void Execute(object context)
        {
            try
            {
                Thread.CurrentThread.Name = _component.GetType().Name;
            }
            catch
            {
            }

            _component.Execute(((BackgroundProcessContext)context).CancellationToken);
        }
    }
}
