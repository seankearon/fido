<#
.SYNOPSIS
    Builds Fido and runs it, forwarding the command line to the app.

.DESCRIPTION
    The development counterpart to release.ps1: a `dotnet build` of src/Fido.csproj, then a launch
    of what it produced. Nothing here publishes, packages, signs, tags or releases - release.ps1
    owns all of that, and build.md covers `dotnet publish`.

    Whatever this script is given, Fido gets, in the shape Fido's own command line expects - so
    `.\run-fido.ps1 feature/new-ui rider` is the run that `fido feature/new-ui rider` would be, and
    the muscle memory carries over. Arguments it does not recognise are forwarded untouched, so a
    flag added to the app works here on the day it lands, with no edit to this file.

    It starts the built executable rather than shelling out to `dotnet run`, which buys two things:
    arguments go through as typed, with no `--` separator to remember, and -NoBuild is then a
    launch with no MSBuild anywhere near it.

    The app runs attached to this terminal, so an unhandled exception during startup is visible and
    the exit code comes back - which is the reason to launch it from here rather than from bin\.
    That costs the prompt for as long as the window is open; pass -Detach to keep it.

    One PowerShell wart is worth knowing, because Fido's own spellings are the natural thing to
    type: `--branch` given to *this* script binds to -Branch's position, not to its name, so
    `.\run-fido.ps1 --branch feature/x` would otherwise scan for a branch called "--branch". The
    script refuses it and says so. Use -b / -t / -s, or put Fido's flags after the branch and the
    tool, where they are forwarded as typed.

.PARAMETER Branch
    The branch to pre-fill. Supplying one starts discovery as the window opens, exactly as typing
    it does. Fido's first positional argument, and this script's.

.PARAMETER Tool
    The tool this run defaults to: a slug such as rider, vsc, vs, zed, term or files, or none for
    the equal-weight grid. With a branch, Fido opens it automatically when the branch is checked
    out in exactly one place. Slugs are editable in Settings and the app calls out an unknown one
    itself, so this script does not vet them.

.PARAMETER Solution
    Filters the solution chips, like Fido's --solution / -s.

.PARAMETER Folder
    Starts the run on the Folder chip rather than a solution, like Fido's --folder.

.PARAMETER Configuration
    Debug unless asked otherwise. Release here is still an ordinary managed build - the Native AOT
    publish that ships belongs to `dotnet publish` and release.ps1.

.PARAMETER NoBuild
    Launches the binary already in bin\, for the runs where only the arguments changed.

.PARAMETER Detach
    Hands the prompt straight back, and leaves Fido running when this terminal closes.

.PARAMETER AppArgs
    Anything else on the line, given to Fido unchanged.

.EXAMPLE
    .\run-fido.ps1
    Builds Debug and opens Fido with an empty form.

.EXAMPLE
    .\run-fido.ps1 feature/new-ui rider
    Launches straight into a scan for the branch, opening Rider if exactly one location matches.

.EXAMPLE
    .\run-fido.ps1 feature/new-ui -s MyApp -Folder
    The same scan, solution chips filtered to MyApp, starting on the Folder chip.

.EXAMPLE
    .\run-fido.ps1 -NoBuild feature/new-ui term
    Tries different arguments against a build that is seconds old.

.EXAMPLE
    .\run-fido.ps1 -c Release -Detach
    Builds Release, starts Fido and gives the prompt back.
#>
[CmdletBinding()]
param(
    # Fido's first positional argument: the branch to scan for.
    [Parameter(Position = 0)]
    [Alias('b')]
    [string] $Branch,

    # Fido's second positional argument: the tool slug this run defaults to.
    [Parameter(Position = 1)]
    [Alias('t')]
    [string] $Tool,

    [Alias('s')]
    [string] $Solution,

    # Start the run on the Folder chip rather than a solution.
    [switch] $Folder,

    [Alias('c')]
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    # Launch what is already built: no restore, no MSBuild.
    [switch] $NoBuild,

    # Start Fido and return to the prompt instead of waiting for the window to close.
    [switch] $Detach,

    # Everything the parameters above did not claim, forwarded to Fido as typed. This is what keeps
    # the script from becoming a bottleneck on the app's own command line.
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $AppArgs
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Off by default in both shells, but a host or profile that turns it on would make a failed
# `dotnet build` throw before the exit code can be reported - which is the one failure this script
# exists to report well. Pinned rather than trusted.
$PSNativeCommandUseErrorActionPreference = $false

$root = $PSScriptRoot

function Write-Step([string] $Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Ok([string] $Message) { Write-Host "    $Message" -ForegroundColor Green }
function Write-Warn([string] $Message) { Write-Host "    $Message" -ForegroundColor Yellow }

# Start-Process joins ArgumentList with spaces and quotes nothing itself, so each argument is quoted
# here the way the C runtime parses it back: a run of backslashes before a quote - or at the end of
# a quoted argument - is doubled, and an embedded quote is escaped. The same text is used for the
# launch and for the line echoed above it, so what is printed is what ran.
function Format-Arg([string] $Value) {
    if ($Value -notmatch '[\s"]') { return $Value }
    '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

# --- where the app will be -------------------------------------------------

$project = [System.IO.Path]::Combine($root, 'src', 'Fido.csproj')
if (-not (Test-Path $project)) {
    throw "Cannot find $project. This script belongs at the root of the Fido repo, beside src\."
}

# The output path is read off the project rather than written down here, so that the day the target
# framework moves this launches the new build instead of the stale one sitting beside it.
# [IO.Path]::Combine takes every segment at once; Windows PowerShell 5.1's Join-Path takes one child
# at a time, and five nested calls are unreadable.
$projectXml = Get-Content $project -Raw
$framework = [regex]::Match($projectXml, '<TargetFramework>\s*([^<]+?)\s*</TargetFramework>').Groups[1].Value
$assembly = [regex]::Match($projectXml, '<AssemblyName>\s*([^<]+?)\s*</AssemblyName>').Groups[1].Value
if (-not $assembly) { $assembly = 'Fido' }

# Not $IsWindows: that automatic variable does not exist in Windows PowerShell 5.1, where
# Set-StrictMode would then make reading it a terminating error.
$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$exeName = if ($onWindows) { "$assembly.exe" } else { $assembly }
$outputRoot = [System.IO.Path]::Combine($root, 'src', 'bin', $Configuration)

function Resolve-Exe {
    $expected = ''
    if ($framework) {
        $expected = [System.IO.Path]::Combine($outputRoot, $framework, $exeName)
        if (Test-Path $expected) { return $expected }
    }

    # A framework this script cannot read - <TargetFrameworks>, or one set behind a condition -
    # should not stop a perfectly good build from launching. One level under bin\<Configuration> is
    # exactly the framework folders: a publish sits a level deeper, under its runtime identifier, so
    # this cannot mistake one for an ordinary build.
    $built = @(Get-ChildItem $outputRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { [System.IO.Path]::Combine($_.FullName, $exeName) } |
        Where-Object { Test-Path $_ } |
        Sort-Object { (Get-Item $_).LastWriteTimeUtc } -Descending)
    if ($built) { return $built[0] }

    # Nothing built yet. Name where it is going to be, so the caller can say so.
    if ($expected) { return $expected }
    throw "Cannot read <TargetFramework> from $project, and there is no built $exeName under $outputRoot."
}

$exe = Resolve-Exe

# --- the app's command line ------------------------------------------------

# PowerShell matches --branch to -Branch's *position*, not its name, so one of Fido's own flags
# typed where the branch goes arrives as the branch - and Fido would take it at face value.
foreach ($positional in @($Branch, $Tool)) {
    if ($positional -like '-*') {
        throw "'$positional' was read as a positional value rather than a flag: PowerShell binds it " +
              "to -Branch or -Tool, not by name. Use -b / -t / -s, or put Fido's own flags after the " +
              'branch and the tool, where they are forwarded as typed.'
    }
}

# Named rather than Fido's positional shorthand: the app ignores a bare argument that starts with a
# dash (ApplyStartupArgs in src\Views\MainWindow.axaml.cs), and naming each value also expresses a
# tool without a branch, which the shorthand cannot. Whatever the parameters did not claim goes
# last, so an explicit --branch typed on the line still wins: Fido keeps the last one it reads.
$startupArgs = @()
if ($Branch) { $startupArgs += @('--branch', $Branch) }
if ($Tool) { $startupArgs += @('--tool', $Tool) }
if ($Solution) { $startupArgs += @('--solution', $Solution) }
if ($Folder) { $startupArgs += '--folder' }
if ($AppArgs) { $startupArgs += $AppArgs }

# --- build -----------------------------------------------------------------

if ($NoBuild) {
    Write-Warn "Skipping the build: launching the last $Configuration build as it stands."
}
else {
    # An ordinary build wants nothing but the SDK. The C++ toolchain, Parcel and gh belong to the
    # publish and the release, and release.ps1 checks for those there.
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK is not on PATH. Install .NET 10 from https://dotnet.microsoft.com/download'
    }

    # A Fido already running out of this folder holds its own exe open, and the copy into bin\ then
    # fails with an MSB3027 that never mentions Fido by name. Said here, it is obvious.
    $running = @()
    if ($onWindows) {
        $running = @(Get-Process -Name $assembly -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $exe })
    }
    if ($running) {
        Write-Warn "Fido is already running from this build (pid $($running[0].Id)); Windows will not"
        Write-Warn 'let the build replace an exe that is open, so close it first.'
    }

    Write-Step "Building Fido ($Configuration)"

    # The project, not the solution: neither the tests nor the F# build project is needed to put a
    # window on the screen, and leaving them out is most of the difference in the wait.
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet build $project -c $Configuration --nologo
    $buildExit = $LASTEXITCODE
    $timer.Stop()

    # Launching the previous binary after a failed build is worse than not launching at all: it
    # looks like the edit worked. dotnet has printed the errors already, so this adds only the exit
    # code - and the one cause MSBuild's own message hides.
    if ($buildExit -ne 0) {
        Write-Host "`n    The build failed with exit code $buildExit." -ForegroundColor Red
        if ($running) {
            Write-Warn "Fido is still running (pid $($running[0].Id)) - a locked $exeName is the usual cause."
        }
        exit $buildExit
    }

    Write-Ok "built in $([math]::Round($timer.Elapsed.TotalSeconds, 1))s"

    # Re-resolved: a first build, or one whose framework folder has just changed, only appears now.
    $exe = Resolve-Exe
}

if (-not (Test-Path $exe)) {
    $why = if ($NoBuild) { 'Run without -NoBuild to build it.' } else { 'The build reported success but left nothing there.' }
    throw "Cannot find $exe. $why"
}

# --- launch ----------------------------------------------------------------

Write-Step "Launching Fido$(if ($Detach) { ' (detached)' })"

$quoted = @($startupArgs | ForEach-Object { Format-Arg $_ })
Write-Host "    $(Format-Arg $exe) $($quoted -join ' ')".TrimEnd() -ForegroundColor DarkGray

# Start-Process rather than the call operator, which returns the moment a windowed process starts
# and so could never report how it ended; and -PassThru with WaitForExit rather than -Wait, which
# waits for the descendants too - and starting an IDE is the whole job.
$start = @{
    FilePath = $exe
    # Fido resolves everything it reads from an absolute path, so this is only about being
    # predictable: the run behaves the same wherever the prompt happened to be standing.
    WorkingDirectory = $root
    PassThru = $true
}

# Windows PowerShell 5.1 rejects an empty ArgumentList outright.
if ($quoted.Count -gt 0) { $start.ArgumentList = $quoted }

# An attached run shares this console, so anything the app writes - an unhandled exception, most
# usefully - lands here. A detached one deliberately does not: a process attached to a console is
# killed when that console closes, and -Detach exists to outlive the terminal.
if (-not $Detach) { $start.NoNewWindow = $true }

$process = Start-Process @start

if ($Detach) {
    Write-Ok "running as pid $($process.Id)"
    return
}

# Reading .Handle caches the handle while the process is alive; without it the ExitCode below can
# come back empty once it has gone.
$null = $process.Handle
$process.WaitForExit()

# Truthiness rather than -ne 0: an ExitCode that could not be read is not evidence of a failure.
if ($process.ExitCode) {
    Write-Host "    Fido exited with code $($process.ExitCode)." -ForegroundColor Red
    exit $process.ExitCode
}

Write-Ok 'Fido closed.'
