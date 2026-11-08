# PowerShell 7: test the published Windows package, actual mpv pipes and API shutdown.
param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
$Package = (Resolve-Path $Package).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('ears-windows-' + [guid]::NewGuid().ToString('N'))
$children = [Collections.Generic.List[Diagnostics.Process]]::new()
New-Item -ItemType Directory $root | Out-Null
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Eventually([scriptblock]$Action) {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        try { $value = & $Action; if ($value) { return $value } } catch { $lastError = $_ }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Desktop smoke timeout: $lastError"
}
function Request([string]$Path) { Invoke-RestMethod "$base$Path" -TimeoutSec 2 -NoProxy }
function Ipc([string]$Endpoint, [object[]]$Command, [switch]$NoReply) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $Endpoint.Substring(9), [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous, [Security.Principal.TokenImpersonationLevel]::Anonymous)
    try {
        $pipe.Connect(3000)
        $bytes = [Text.Encoding]::UTF8.GetBytes((@{ command = $Command; request_id = 42 } | ConvertTo-Json -Compress -Depth 5) + "`n")
        $pipe.Write($bytes, 0, $bytes.Length)
        if ($NoReply) { return }
        $reader = [IO.StreamReader]::new($pipe)
        while ($true) {
            $line = $reader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(3)).GetAwaiter().GetResult()
            if ($null -eq $line) { throw 'mpv disconnected' }
            $message = $line | ConvertFrom-Json
            if ($message.request_id -eq 42) {
                if ($message.error -eq 'property unavailable') { return $null }
                Assert ($message.error -eq 'success') "mpv: $line"
                return $message.data
            }
        }
    } finally { $pipe.Dispose() }
}
function Start-Owned([string]$Exe, [string[]]$Arguments, [switch]$InputLifetime) {
    $start = [Diagnostics.ProcessStartInfo]::new($Exe)
    $start.WorkingDirectory = Split-Path $Exe -Parent
    $start.UseShellExecute = $false
    foreach ($arg in $Arguments) { $start.ArgumentList.Add($arg) }
    if ($InputLifetime) {
        $start.RedirectStandardInput = $true
        $start.Environment['RABBITEARS_DESKTOP_STDIN'] = '1'
    }
    $child = [Diagnostics.Process]::Start($start)
    $children.Add($child)
    return $child
}
function Wait-Exit($Child) {
    Assert ($Child.WaitForExit(20000)) 'Owned process did not stop in 20 seconds'
    return $Child.ExitCode
}
try {
    # Edit a disposable copy, never the shipped configuration or a user's profile.
    $copy = Join-Path $root 'package with spaces'
    Copy-Item $Package $copy -Recurse
    Add-Content "$copy/app/mpv/mpv.conf" "`nvo=null`nao=null`nhwdec=no`nforce-window=no"
    $media = Join-Path $root 'TV shows 日本語/Cartoons'
    New-Item -ItemType Directory $media -Force | Out-Null
    $episode = Join-Path $media 'Test episode.mp4'
    & ffmpeg -v error -f lavfi -i 'testsrc2=size=160x120:rate=10' -f lavfi -i 'sine=frequency=440' -t 30 -c:v mpeg4 -c:a aac $episode
    Assert ($LASTEXITCODE -eq 0) 'Fixture generation failed'
    $mpv = (Get-Command mpv.exe).Source
    $probe = (Get-Command ffprobe.exe).Source
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $base = "http://127.0.0.1:$port"
    $state = Join-Path $root 'state'
    $launcher = Join-Path $copy 'launcher/RabbitEars.Desktop.exe'
    $launchArgs = @('--media', (Split-Path $media -Parent), '--state', $state, '--port', "$port", '--mpv', $mpv, '--ffprobe', $probe, '--local')
    foreach ($attempt in 0..1) {
        $process = Start-Owned $launcher $launchArgs
        $endpoint = Eventually { (Request '/api/player').socket }
        Assert ($endpoint.StartsWith('\\.\pipe\')) 'Expected a local named pipe'
        Eventually { (Ipc $endpoint @('get_property', 'path')).Replace('/', '\') -eq $episode } | Out-Null
        Eventually { (Ipc $endpoint @('get_property', 'time-pos')) -gt 0 } | Out-Null
        Assert ((Request '/api/library').episodes -eq 1) 'Expected one episode'
        Assert (-not (Request '/api/demo').enabled) 'Desktop must not be demo mode'
        if ($attempt -eq 0) {
            $duplicate = Start-Owned $launcher $launchArgs
            Assert ((Wait-Exit $duplicate) -ne 0) 'Duplicate profile was accepted'
            $collisionArgs = $launchArgs.Clone()
            $collisionArgs[3] = Join-Path $root 'other state'
            $collision = Start-Owned $launcher $collisionArgs
            Assert ((Wait-Exit $collision) -ne 0) 'Occupied port was accepted'
            $oldVolume = (Request '/api/state').volume
            Ipc $endpoint @('script-message', 'ears', 'volume-up') | Out-Null
            $savedVolume = Eventually { $v = (Request '/api/state').volume; if ($v -gt $oldVolume) { $v } }
            Ipc $endpoint @('quit', 0) -NoReply
            Assert ((Wait-Exit $process) -eq 0) 'Normal player quit failed'
            Assert (Test-Path "$state/state.json") 'Graceful API shutdown did not save state'
        } else {
            Assert ((Request '/api/state').volume -eq $savedVolume) 'Restart did not restore volume'
            $playerPid = Ipc $endpoint @('get_property', 'pid')
            Stop-Process -Id $playerPid -Force
            Assert ((Wait-Exit $process) -ne 0) 'Player crash was not reported'
        }
        $connection = [Net.Sockets.TcpClient]::new()
        try {
            $open = $false
            try { $connection.Connect('127.0.0.1', $port); $open = $true } catch {}
            Assert (-not $open) 'API listener survived launcher exit'
        } finally { $connection.Dispose() }
    }
    # EOF (parent disappearance) and explicit shutdown both let the real API save.
    foreach ($mode in 'shutdown', 'eof') {
        $directState = Join-Path $root $mode
        $apiArgs = @('--urls', $base, '--Box:MediaRoot', (Split-Path $media -Parent), '--Box:StateDir', $directState,
            '--Box:Player:Kind', 'browser', '--Box:Ffprobe', $probe, '--Box:Demo:Enabled', 'false')
        $api = Start-Owned "$copy/app/RabbitEars.Api.exe" $apiArgs -InputLifetime
        Eventually { (Request '/api/state').volume -gt 0 } | Out-Null
        Invoke-RestMethod "$base/api/admin/rescan" -Method Post -Headers @{ 'X-RabbitEars-Admin' = '1' } -NoProxy | Out-Null
        if ($mode -eq 'shutdown') { $api.StandardInput.WriteLine('shutdown') }
        $api.StandardInput.Close()
        Assert ((Wait-Exit $api) -eq 0) "API $mode shutdown failed"
        Assert (Test-Path "$directState/state.json") "API $mode did not save state"
    }
    Write-Host 'Windows playback, pipes, profile/port rejection, persistence, crash cleanup and stdin shutdown passed.'
} finally {
    foreach ($child in $children) {
        if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
    Get-ChildItem "$root/state/logs/*.log" -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_ -Tail 60 }
    Remove-Item $root -Recurse -Force
}
