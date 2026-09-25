[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:55905',
    [string]$OutputDirectory,
    [switch]$UnboundedExecution,
    [switch]$ApproveLive
)

$ErrorActionPreference = 'Stop'
if (-not $ApproveLive) { throw 'Specificare -ApproveLive per autorizzare le sei run LIVE a pagamento. Con -UnboundedExecution non viene applicato alcun tetto di spesa.' }
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot $(if ($UnboundedExecution) { 'misurazioni-2026-09-25-unbounded' } else { 'misurazioni-2026-09-25-4096' })
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$question = "Per l'ordine ORD-1042, dimmi quando e stato consegnato, se posso restituirlo perche difettoso al primo utilizzo e quale importo mi spetterebbe. Consigliami anche una camicia disponibile sotto i 40 USD, con ID e prezzo. Non creare bozze ne eseguire azioni."
$configuration = @{
    mode = 'live'; modelProfileId = 'gpt5'; agentModels = @{}
    promptProfile = 'good'; promptBlocks = @{
        checklist = $false; outputContract = $false; examples = $false
        redundancy = $false; conflictingStyle = $false
    }
    historyStrategy = 'full'; toolTransport = 'direct'; confirmAction = $false
    maxOutputTokens = 4096; maxModelCalls = 24; approvedBudgetUsd = 0.10
}
if ($UnboundedExecution) {
    $configuration.unboundedExecution = $true
    $configuration.approvedBudgetUsd = $null
}
$cases = @(
    @{ technology = 'inline'; repetition = 1 },
    @{ technology = 'skills'; repetition = 1 },
    @{ technology = 'a2a'; repetition = 1 },
    @{ technology = 'a2a'; repetition = 2 },
    @{ technology = 'skills'; repetition = 2 },
    @{ technology = 'inline'; repetition = 2 }
)
$manifestPath = Join-Path $OutputDirectory 'experiment.json'
if (-not (Test-Path $manifestPath)) {
    @{
        startedAt = [DateTimeOffset]::UtcNow.ToString('o')
        question = $question; configuration = $configuration; executionOrder = $cases
        notice = 'Two sequential repetitions per architecture; no cache reset or guaranteed cold/warm state. New conversation for each run. Synthetic data. Provider measurements; not a benchmark.'
        limitsNotice = $(if ($UnboundedExecution) { 'Explicit unbounded execution: budget, call count and run timeout disabled; provider output cap omitted. Legacy numeric limit fields are ignored. Provider/SDK technical constraints, cancellation and action permissions remain.' } else { 'Bounded: 0.10 USD/run, 24 calls, 4096 output tokens per call.' })
    } | ConvertTo-Json -Depth 15 | Set-Content $manifestPath -Encoding utf8
} else {
    $existing = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ($existing.question -cne $question -or $existing.configuration.maxOutputTokens -ne $configuration.maxOutputTokens -or $existing.configuration.modelProfileId -ne $configuration.modelProfileId -or [bool]$existing.configuration.unboundedExecution -ne [bool]$UnboundedExecution) {
        throw 'Experiment configuration differs. Use a new output directory to avoid mixing measurements.'
    }
}
foreach ($case in $cases) {
    $tech = $case.technology
    $rep = $case.repetition
    $prefix = Join-Path $OutputDirectory "$tech-$rep"
    $api = "$BaseUrl/api/$tech"
    if (Test-Path "$prefix-export.json") {
        Write-Output "$tech/$rep already exported; no repeated paid submission."
        continue
    }
    $config = Invoke-RestMethod "$api/config" -TimeoutSec 30
    $ready = $config.capabilities.modelCapabilities | Where-Object modelProfileId -eq 'gpt5'
    if (-not $config.allowLive -or -not $ready.liveReady) { throw "$tech is not LIVE ready for GPT-5." }
    if ($UnboundedExecution -and -not $config.capabilities.allowUnboundedExecution) { throw "$tech does not allow unbounded execution." }
    if (-not (Test-Path "$prefix-submission.json")) {
        $preview = Invoke-RestMethod -Method Post "$api/prompts/preview" -ContentType 'application/json' -Body ($configuration | ConvertTo-Json -Depth 8)
        $preview | ConvertTo-Json -Depth 30 | Set-Content "$prefix-prompt-preview.json" -Encoding utf8
        $conversation = Invoke-RestMethod -Method Post "$api/conversations" -ContentType 'application/json' -Body (@{title="Misure talk - $tech - ripetizione $rep"} | ConvertTo-Json)
        $payload = @{message=$question; idempotencyKey=[Guid]::NewGuid().ToString('N'); configuration=$configuration}
        @{conversationId=$conversation.id; payload=$payload} | ConvertTo-Json -Depth 15 | Set-Content "$prefix-submission.json" -Encoding utf8
    }
    $submission = Get-Content "$prefix-submission.json" -Raw | ConvertFrom-Json
    # The persisted idempotency key makes recovery from an uncertain submission safe.
    $accepted = Invoke-RestMethod -Method Post "$api/conversations/$($submission.conversationId)/turns" -ContentType 'application/json' -Body ($submission.payload | ConvertTo-Json -Depth 15) -TimeoutSec 30
    $accepted | ConvertTo-Json | Set-Content "$prefix-accepted.json" -Encoding utf8
    Write-Output "Started $tech/$rep run=$($accepted.runId)"
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(8)
    do {
        $run = Invoke-RestMethod "$api/runs/$($accepted.runId)" -TimeoutSec 30
        if ($run.status -in @('completed','failed','cancelled','canceled')) { break }
        if (-not $UnboundedExecution -and [DateTimeOffset]::UtcNow -gt $deadline) { throw "Timeout awaiting $tech/$rep; run ID persisted. Inspect before resuming." }
        Start-Sleep -Seconds 2
    } while ($true)
    $export = Invoke-RestMethod "$api/runs/$($accepted.runId)/export" -TimeoutSec 30
    $export | ConvertTo-Json -Depth 100 | Set-Content "$prefix-export.json" -Encoding utf8
    Write-Output ("Finished {0}/{1}: status={2}, calls={3}, input={4}, output={5}, ms={6}, USD={7}" -f $tech,$rep,$run.status,$run.calls.Count,$run.inputTokens,$run.outputTokens,$run.durationMs,$run.estimatedCostUsd)
    if ($run.status -ne 'completed') { Write-Warning "$tech/$rep ended $($run.status): $($run.error). Retaining failed measurement without retry or budget increase." }
    if (@($run.calls | Where-Object { $_.mode -ne 'live' -or $_.usageSource -ne 'provider' }).Count) { throw "$tech/$rep lacks exclusive LIVE/provider usage; inspect exported evidence." }
}
Write-Output "Six run exports saved to $OutputDirectory"
