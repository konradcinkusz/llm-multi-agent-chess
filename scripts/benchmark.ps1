#requires -Version 7.4
<#
.SYNOPSIS
    Benchmarks one candidate against opponents on a running arena (03 §12.3, ADR-014, docs/BENCHMARK.md).

.DESCRIPTION
    Creates the run through the API, follows its progress, and writes benchmark-<name>.md and benchmark-<name>.json.
    The PowerShell twin of scripts/benchmark.sh: the same options, the same files and the same exit codes.
    Needs PowerShell 7.4 or later and an arena already running (the fake profile works without Copilot).
    Refuses to run under CI. Exit status: 0 finished; 1 the API refused or failed, or the run was cancelled;
    2 bad usage or CI; 130 interrupted (Ctrl-C cancels the run on the server first).

.EXAMPLE
    ./scripts/benchmark.ps1 -Candidate claude-sonnet-4.5@medium

.EXAMPLE
    ./scripts/benchmark.ps1 -Candidate fake-a@high -Opponents fake-b,random -Games 4 -OutDir $HOME/bench-out
#>
[CmdletBinding()]
param(
    # The model to benchmark, with an optional effort and prompt style: <model>[@<effort>][~<style>].
    [Parameter(Mandatory)]
    [string] $Candidate,

    # Opponent ids, each with an optional effort and style. Default: every enabled model except the candidate's model
    # (in any effort or style) and Auto.
    [string[]] $Opponents = @(),

    # Games against each opponent: an even number, at least 2.
    [int] $Games = 30,

    # Games played in parallel; at most Arena:MaxConcurrentGames.
    [int] $Concurrency = 2,

    # The run's name; the file names use it too. Default: the candidate, with each character other than letters, digits,
    # dot, dash and underscore replaced by -.
    [string] $Name,

    # Where benchmark-<name>.md and benchmark-<name>.json are written.
    [string] $OutDir = '.'
)

$ErrorActionPreference = 'Stop'
$PollSeconds = 10

# The API: ARENA_API_URL, default http://127.0.0.1:5080.
$script:Api = if ($env:ARENA_API_URL) { $env:ARENA_API_URL.TrimEnd('/') } else { 'http://127.0.0.1:5080' }

function Exit-Benchmark([int] $Code, [string] $Message) {
    [Console]::Error.WriteLine("benchmark.ps1: $Message")
    exit $Code
}

# A variable counts as set when it is non-empty and not false or 0 (GitHub Actions sets CI=true and GITHUB_ACTIONS=true).
function Test-EnvSet([string] $Value) {
    -not [string]::IsNullOrEmpty($Value) -and $Value -notin @('false', '0')
}

# One call to the API. Returns the status and the body; throws only when the API cannot be reached.
function Invoke-Arena([string] $Method, [string] $Path, [string] $Json) {
    $parameters = @{
        Uri                 = "$script:Api$Path"
        Method              = $Method
        Headers             = @{ Accept = 'application/json' }
        SkipHttpErrorCheck  = $true
        ErrorAction         = 'Stop'
    }
    if ($PSBoundParameters.ContainsKey('Json')) {
        $parameters.Body = $Json
        $parameters.ContentType = 'application/json'
    }
    $response = Invoke-WebRequest @parameters
    [pscustomobject]@{ Status = [int] $response.StatusCode; Body = [string] $response.Content }
}

# The problem details of a refused request, one line each, or the raw body when it is not JSON.
function Format-Refusal([string] $Body) {
    if ([string]::IsNullOrWhiteSpace($Body)) {
        return '  no details were returned; check the options against --help'
    }
    try {
        $problem = $Body | ConvertFrom-Json
    }
    catch {
        return "  $Body"
    }
    $lines = @()
    if ($problem.title) { $lines += "  $($problem.title)" }
    if ($problem.detail) { $lines += "  $($problem.detail)" }
    if ($problem.errors) {
        foreach ($property in $problem.errors.PSObject.Properties) {
            $lines += "  $($property.Name): $($property.Value -join ' ')"
        }
    }
    $lines -join [Environment]::NewLine
}

function Write-Utf8File([string] $Path, [string] $Text) {
    [System.IO.File]::WriteAllText($Path, $Text + "`n", [System.Text.UTF8Encoding]::new($false))
}

if (Test-EnvSet $env:CI -or Test-EnvSet $env:GITHUB_ACTIONS) {
    Exit-Benchmark 2 'refusing to run under CI: a benchmark plays real games against a live arena (see docs/BENCHMARK.md)'
}

if (-not $Name) {
    $Name = $Candidate
}
$slug = $Name -replace '[^A-Za-z0-9._-]', '-'

try {
    $alive = Invoke-Arena GET '/alive'
}
catch {
    Exit-Benchmark 1 "no arena answers at $script:Api. Start the app first (docs/BENCHMARK.md), or set ARENA_API_URL."
}
if ($alive.Status -ne 200) {
    Exit-Benchmark 1 "no arena answers at $script:Api. Start the app first (docs/BENCHMARK.md), or set ARENA_API_URL."
}

# A list given on the command line as one string (pwsh -File x.ps1 -Opponents a,b) arrives unsplit, so split every item.
$opponentIds = @($Opponents | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
# Assigned, not returned from an if: a one-item list returned from an if-expression is unrolled to a string in JSON.
$opponentsValue = $null
if ($opponentIds.Count -gt 0) {
    $opponentsValue = [string[]] $opponentIds
}

$createBody = [ordered]@{
    name              = $Name
    candidate         = $Candidate
    opponents         = $opponentsValue
    gamesPerOpponent  = $Games
    maxConcurrency    = $Concurrency
} | ConvertTo-Json -Depth 4 -Compress

try {
    $created = Invoke-Arena POST '/api/benchmarks' $createBody
}
catch {
    Exit-Benchmark 1 "could not reach $script:Api to create the benchmark"
}
if ($created.Status -ne 201) {
    [Console]::Error.WriteLine("The arena refused the benchmark (HTTP $($created.Status)):")
    [Console]::Error.WriteLine((Format-Refusal $created.Body))
    exit 1
}

$run = $created.Body | ConvertFrom-Json
$runId = $run.benchmark.id
$opponentCount = @($run.benchmark.opponents).Count
Write-Output "Benchmark ${runId}: $Candidate against $opponentCount opponent(s), $($run.benchmark.gamesTotal) games in all ($Concurrency concurrent)."

# The estimate is for the whole run. A model with no price has no estimate, so the total leaves it out, and the count says so.
$premium = 0.0
$usd = 0.0
$unpriced = 0
foreach ($estimate in @($run.estimatedPremiumRequests)) {
    if ($null -eq $estimate.estimatedPremiumRequests) {
        $unpriced++
    }
    else {
        $premium += $estimate.estimatedPremiumRequests
    }
    if ($null -ne $estimate.estimatedUsd) {
        $usd += $estimate.estimatedUsd
    }
}
Write-Output ('Estimate: {0} premium request(s), about ${1:N4}; {2} model(s) without a price are not counted.' -f $premium, $usd, $unpriced)

$last = ''
$lostArena = $false
# Still true when the poll loop is left by Ctrl-C (a pipeline stop), which runs the finally block and nothing else.
$interrupted = $true
try {
    while ($true) {
        try {
            $poll = Invoke-Arena GET "/api/benchmarks/$runId"
        }
        catch {
            $poll = $null
        }
        if ($null -eq $poll -or $poll.Status -ne 200) {
            $lostArena = $true
            $interrupted = $false
            break
        }
        $state = $poll.Body | ConvertFrom-Json
        $progress = "$($state.status) $($state.gamesFinished)/$($state.gamesTotal)"
        if ($progress -ne $last) {
            Write-Output ('[{0}] {1}: {2} of {3} games finished' -f (Get-Date -Format 'HH:mm:ss'), $state.status, $state.gamesFinished, $state.gamesTotal)
            $last = $progress
        }
        if ($state.status -ne 'running') {
            $interrupted = $false
            break
        }
        Start-Sleep -Seconds $PollSeconds
    }
}
finally {
    if ($interrupted) {
        # Ctrl-C: stop the run on the server too, so no games keep running for an abandoned command.
        try { Invoke-Arena POST "/api/benchmarks/$runId/cancel" '{}' | Out-Null } catch { }
        [Console]::Error.WriteLine("`nCancelled benchmark $runId on the server.")
        $Host.SetShouldExit(130)
    }
}
if ($lostArena) {
    Exit-Benchmark 1 "lost the arena while benchmark $runId ran; it keeps running on the server. Check $script:Api/api/benchmarks/$runId"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$jsonPath = Join-Path $OutDir "benchmark-$slug.json"
$mdPath = Join-Path $OutDir "benchmark-$slug.md"

$report = Invoke-Arena GET "/api/benchmarks/$runId/report"
if ($report.Status -ne 200) {
    Exit-Benchmark 1 "could not read the report of benchmark $runId (HTTP $($report.Status))"
}
try {
    $summary = ($report.Body | ConvertFrom-Json).summary
}
catch {
    Exit-Benchmark 1 "the report of benchmark $runId is not JSON"
}
Write-Utf8File $jsonPath $report.Body

$markdown = Invoke-Arena GET "/api/benchmarks/$runId/report?format=md"
if ($markdown.Status -ne 200) {
    Exit-Benchmark 1 "could not read the Markdown report of benchmark $runId (HTTP $($markdown.Status))"
}
Write-Utf8File $mdPath $markdown.Body

Write-Output ('Wrote {0} and {1} ({2} games, score {3:N0}%).' -f $mdPath, $jsonPath, $summary.games, ($summary.score * 100))
if ($state.status -ne 'finished') {
    Exit-Benchmark 1 "benchmark $runId ended as '$($state.status)'; the report holds the games played so far"
}
