namespace RabbitEars.Api;

/// <summary>Allows an owning desktop process to request a graceful stop without a network shutdown route.</summary>
public sealed class DesktopParentLifetime(IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // EOF also stops the API if the launcher dies. This input is never shared with mpv.
            while (await Console.In.ReadLineAsync(stoppingToken) is { } line)
                if (line == "shutdown") break;
            lifetime.StopApplication();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (IOException) { lifetime.StopApplication(); }
    }
}
