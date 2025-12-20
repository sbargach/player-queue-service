using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PlayerQueueService.Api.Application;

public static class Program
{
    public static async Task Main(string[] args)
    {
        using var host = HostBuilderFactory.Create(args).Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var appName = assembly.GetName().Name ?? "PlayerQueueService.Api";
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly.GetName().Version?.ToString();

        logger.LogInformation("Starting {Application} v{Version}", appName, version);
        await host.RunAsync().ConfigureAwait(false);
    }
}
