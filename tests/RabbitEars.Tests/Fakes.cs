using Microsoft.AspNetCore.SignalR;
using RabbitEars.Api;

namespace RabbitEars.Tests;

internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Stands in for SignalR. One list records every message, with the room it
/// went to, or null when it went to everyone.
/// </summary>
internal sealed class RecordingHub : IHubContext<TvHub>, IHubClients
{
    public List<(string? Group, string Method, object?[] Args)> Sent { get; } = [];

    IHubClients IHubContext<TvHub>.Clients => this;
    IGroupManager IHubContext<TvHub>.Groups => throw new NotSupportedException();

    public IClientProxy All => new Proxy(this, null);
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
    public IClientProxy Client(string connectionId) => throw new NotSupportedException();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
    public IClientProxy Group(string groupName) => new Proxy(this, groupName);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
    public IClientProxy User(string userId) => throw new NotSupportedException();
    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

    private sealed class Proxy(RecordingHub hub, string? group) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            hub.Sent.Add((group, method, args));
            return Task.CompletedTask;
        }
    }
}
