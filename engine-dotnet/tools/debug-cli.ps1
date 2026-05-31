<#
.SYNOPSIS
  Talk to a running PokeGold instance over its debug pipe (T1).

.DESCRIPTION
  The game's Host starts a named-pipe debug server ("pokegold-debug" by default).
  This client connects, sends one or more commands, prints each reply, and exits.
  Replies are terminated by a lone "<<END>>" sentinel line, which this client
  consumes and does not print.

  Run the game first (dotnet run --project src/PokeGold.Host), then use this.

.PARAMETER Command
  One or more command lines to send (e.g. "player", "warp AzaleaTown 9 12").
  If omitted, drops into an interactive prompt; type 'quit' to exit.

.PARAMETER PipeName
  The pipe name to connect to. Defaults to "pokegold-debug".

.EXAMPLE
  ./debug-cli.ps1 player
  ./debug-cli.ps1 npcs flags
  ./debug-cli.ps1 "warp AzaleaTown 9 12 down"
  ./debug-cli.ps1            # interactive
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $Command,
    [string] $PipeName = 'pokegold-debug',
    [int] $TimeoutMs = 3000
)

$ErrorActionPreference = 'Stop'

function Connect-Pipe {
    param([string] $Name, [int] $Timeout)
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $Name, [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect($Timeout)
    $reader = New-Object System.IO.StreamReader($pipe)
    $writer = New-Object System.IO.StreamWriter($pipe)
    $writer.AutoFlush = $true
    [pscustomobject]@{ Pipe = $pipe; Reader = $reader; Writer = $writer }
}

function Invoke-DebugCommand {
    param($Conn, [string] $Line)
    $Conn.Writer.WriteLine($Line)
    $out = New-Object System.Collections.Generic.List[string]
    while ($true) {
        $reply = $Conn.Reader.ReadLine()
        if ($null -eq $reply -or $reply -eq '<<END>>') { break }
        $out.Add($reply)
    }
    $out -join "`n"
}

try {
    $conn = Connect-Pipe -Name $PipeName -Timeout $TimeoutMs
}
catch {
    Write-Error "Could not connect to pipe '$PipeName'. Is the game running? ($_)"
    exit 1
}

try {
    if ($Command) {
        foreach ($c in $Command) {
            Write-Output (Invoke-DebugCommand -Conn $conn -Line $c)
        }
    }
    else {
        Write-Output "Connected to '$PipeName'. Type a command ('help' for list, 'quit' to exit)."
        while ($true) {
            $line = Read-Host 'pokegold'
            if ($line -in @('quit', 'exit')) { break }
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            Write-Output (Invoke-DebugCommand -Conn $conn -Line $line)
        }
    }
}
finally {
    # Dispose the underlying pipe; closing it tears down the stream readers/writers.
    # Guard each step so a server-side close doesn't surface as a terminating error.
    foreach ($d in @($conn.Writer, $conn.Reader, $conn.Pipe)) {
        try { $d.Dispose() } catch { }
    }
}
