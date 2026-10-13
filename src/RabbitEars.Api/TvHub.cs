using Microsoft.AspNetCore.SignalR;

namespace RabbitEars.Api;

/// <summary>
/// The remote-control wire. The TV screen (forwarding Flirc key presses) and
/// the phone remote both call these; every change comes back to all of them as
/// a "State" message, so they can never disagree about what is on.
/// </summary>
public sealed class TvHub(TvService tv) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("State", tv.Snapshot());
        await base.OnConnectedAsync();
    }

    public Task ChannelUp() => tv.Press(t => t.ChannelUp());
    public Task ChannelDown() => tv.Press(t => t.ChannelDown());
    public Task LastChannel() => tv.Press(t => t.LastChannel());
    public Task GoToChannel(int number) => tv.Press(t => t.GoToChannel(number));
    public Task Digit(int digit) => tv.Press(t => t.PushDigit(digit));
    public Task Confirm() => tv.Press(t => t.Confirm());
    public Task Info() => tv.Press(t => t.ShowInfo());
    public Task VolumeUp() => tv.Press(t => t.VolumeUp());
    public Task VolumeDown() => tv.Press(t => t.VolumeDown());
    public Task Mute() => tv.Press(t => t.ToggleMute());
    public Task Power() => tv.Press(t => t.TogglePower());

    // Reports from the TV screen. The token says which play request they are
    // about, so a late report for a channel that was just left is harmless.
    public Task EpisodeEnded(string token) => tv.Press(t => t.EpisodeEnded(token));
    public Task EpisodeFailed(string token) => tv.Press(t => t.EpisodeFailed(token));
    public void ReportPosition(string token, double seconds) => tv.ReportPosition(token, seconds);
}
