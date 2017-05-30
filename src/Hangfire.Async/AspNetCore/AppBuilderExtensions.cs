using Hangfire;
using Hangfire.Annotations;
using Hangfire.Async.Server;
using Hangfire.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder
{
    public static class AppBuilderExtensions
    {
        public static IApplicationBuilder UseHangfireAsyncServer(
            [NotNull] this IApplicationBuilder app,
            [CanBeNull] BackgroundJobServerOptions options = null,
            [CanBeNull] IEnumerable<IBackgroundProcess> additionalProcesses = null,
            [CanBeNull] JobStorage storage = null)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            ThrowIfNotConfigured(app);

            var services = app.ApplicationServices;
            var lifetime = services.GetRequiredService<IApplicationLifetime>();

            storage = storage ?? services.GetRequiredService<JobStorage>();
            options = options ?? services.GetService<BackgroundJobServerOptions>() ?? new BackgroundJobServerOptions();
            additionalProcesses = additionalProcesses ?? services.GetServices<IBackgroundProcess>();

            var server = new AsyncBackgroundJobServer(options, storage, additionalProcesses);

            lifetime.ApplicationStopping.Register(() => server.SendStop());
            lifetime.ApplicationStopped.Register(() => server.Dispose());

            return app;
        }

        private static void ThrowIfNotConfigured(IApplicationBuilder app)
        {
            var configuration = app.ApplicationServices.GetService<IGlobalConfiguration>();
            if (configuration == null)
            {
                throw new InvalidOperationException(
                    "Unable to find the required services. Please add all the required services by calling 'IServiceCollection.AddHangfire' inside the call to 'ConfigureServices(...)' in the application startup code.");
            }
        }
    }
}
