using Microsoft.AspNetCore.SignalR;

namespace RabbitEars.Api;

/// <summary>
/// The remote-control wire. The TV screen (forwarding Flirc key presses) and
/// the phone remote both call these; every change comes back to all of them as
/// a "State" message, so they can never disagree about what is on.
/// </summary>
/// <remarks>
/// Each connection belongs to one television. At home that is always the one
/// TV; in demo mode it is the room named in the hub URL's <c>room</c> query.
/// </remarks>
public sealed class TvHub(TvService tv) : Hub
{
    private const string RoomKey = "room";

    private string Room => (string)Context.Items[RoomKey]!;

    public override async Task OnConnectedAsync()
    {
        var requested = Context.GetHttpContext()?.Request.Query[RoomKey].ToString();
        if (tv.Join(requested) is not { } room)
        {
            Context.Abort();
            return;
        }

        Context.Items[RoomKey] = room;
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        await Clients.Caller.SendAsync("State", tv.Snapshot(room));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.ContainsKey(RoomKey)) tv.Leave(Room);
        return base.OnDisconnectedAsync(exception);
    }

    public Task ChannelUp() => tv.Press(t => t.ChannelUp(), Room);
    public Task ChannelDown() => tv.Press(t => t.ChannelDown(), Room);
    public Task LastChannel() => tv.Press(t => t.LastChannel(), Room);
    public Task GoToChannel(int number) => tv.Press(t => t.GoToChannel(number), Room);
    public Task Digit(int digit) => tv.Press(t => t.PushDigit(digit), Room);
    public Task Confirm() => tv.Press(t => t.Confirm(), Room);
    public Task Info() => tv.Press(t => t.ShowInfo(), Room);
    public Task VolumeUp() => tv.Press(t => t.VolumeUp(), Room);
    public Task VolumeDown() => tv.Press(t => t.VolumeDown(), Room);
    public Task Mute() => tv.Press(t => t.ToggleMute(), Room);
    public Task Power() => tv.Press(t => t.TogglePower(), Room);

    // Reports from the TV screen. The token says which play request they are
    // about, so a late report for a channel that was just left is harmless.
    public Task EpisodeEnded(string token) => tv.Press(t => t.EpisodeEnded(token), Room);
    public Task EpisodeFailed(string token) => tv.Press(t => t.EpisodeFailed(token), Room);
    public void ReportPosition(string token, double seconds) => tv.ReportPosition(token, seconds, Room);
}
