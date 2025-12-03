using PlayerQueueService.Api.Application;

public class Program
{
    public static async Task Main(string[] args)
    {
        using var host = HostBuilderFactory.Create(args).Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}
