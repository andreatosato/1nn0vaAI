[CmdletBinding()]
param(
    [Parameter(Mandatory)][Uri]$InlineUrl,
    [Parameter(Mandatory)][Uri]$SkillsUrl,
    [Parameter(Mandatory)][Uri]$A2AUrl
)

$ErrorActionPreference = 'Stop'
$targets = [ordered]@{ inline = $InlineUrl; skills = $SkillsUrl; a2a = $A2AUrl }
$summary = [System.Collections.Generic.List[object]]::new()
$serviceNames = @('catalog', 'orders', 'returns')
$skillNames = @('shop-catalog', 'shop-orders', 'shop-returns')

foreach ($technology in $targets.Keys) {
    $base = $targets[$technology].AbsoluteUri.TrimEnd('/')
    $config = Invoke-RestMethod -Uri "$base/api/config"
    if ($config.technology -ne $technology -or $config.allowLive) {
        throw "Unexpected configuration for $technology. This smoke test requires MOCK-only resources."
    }
    $expectedAgents = if ($technology -eq 'a2a') { @('router', 'catalog', 'orders', 'returns') } else { @('router') }
    $expectedTopology = switch ($technology) {
        'inline' { 'router-http' }
        'skills' { 'router-skills-http' }
        'a2a' { 'router-a2a' }
    }
    if (-not $config.capabilities -or
        $config.capabilities.businessApi -ne ($technology -ne 'a2a') -or
        $config.capabilities.executionTopology -ne $expectedTopology -or
        (@($config.capabilities.agentNames | Sort-Object) -join ',') -cne (@($expectedAgents | Sort-Object) -join ',') -or
        (@($config.capabilities.serviceNames | Sort-Object) -join ',') -cne ($serviceNames -join ',')) {
        throw "Unexpected execution agents, service targets or topology for $technology."
    }
    $products = Invoke-RestMethod -Uri "$base/api/products"
    if ($products.Count -lt 30 -or -not ($products | Where-Object { $_.id -eq 83 -and $_.thumbnail })) {
        throw "Public catalog/images missing for $technology."
    }

    $scenarios = Invoke-RestMethod -Uri "$base/api/scenarios"
    $scenario = $scenarios | Where-Object { $_.id -eq 'main-six-turns' }
    if (-not $scenario -or $scenario.turns.Count -ne 6) {
        throw "The six-turn scenario is missing for $technology."
    }
    $conversation = Invoke-RestMethod -Method Post -Uri "$base/api/conversations" -ContentType 'application/json' `
        -Body '{"title":"Automated MOCK six-turn smoke test"}'
    $seenAgents = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $seenRequests = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $seenResponses = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $loadedSkills = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $protocolEvents = 0
    $skillEvents = 0
    $lastRun = $null
    foreach ($turn in $scenario.turns) {
        $payload = @{
            message = $turn.message
            idempotencyKey = [Guid]::NewGuid().ToString('N')
            configuration = @{
                mode = 'mock'
                modelProfileId = 'gpt5'
                promptProfile = 'good'
                historyStrategy = 'full'
                toolTransport = 'direct'
                confirmAction = [bool]$turn.confirmAction
            }
        } | ConvertTo-Json -Depth 8
        $accepted = Invoke-RestMethod -Method Post -Uri "$base/api/conversations/$($conversation.id)/turns" `
            -ContentType 'application/json' -Body $payload
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
        do {
            $run = Invoke-RestMethod -Uri "$base/api/runs/$($accepted.runId)"
            if ($run.status -in @('completed', 'failed', 'cancelled', 'canceled')) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($run.status -ne 'completed') {
            throw "$technology run $($run.id) did not complete: $($run.status) $($run.error)"
        }
        if ([string]::IsNullOrWhiteSpace($run.result.answer) -or $run.calls.Count -eq 0) {
            throw "$technology returned no answer or model-call ledger."
        }
        foreach ($call in $run.calls) {
            if ($call.agent -notin $expectedAgents) {
                throw "Unexpected model agent $($call.agent) in $technology. Business services are not execution agents."
            }
            [void]$seenAgents.Add($call.agent)
            if ($call.mode -ne 'mock' -or $call.usageSource -ne 'mock' -or $null -ne $call.inputTokens) {
                throw 'MOCK run contains incorrect usage provenance.'
            }
            $request = $call.request | ConvertTo-Json -Depth 40 -Compress
            if ($request -match 'cdn\.dummyjson\.com|image_url|data:image/|\"thumbnail\"') {
                throw "Images leaked into a model request: $($call.id)"
            }
        }
        if (-not @($run.calls | Where-Object { $_.agent -eq 'router' }).Count) {
            throw "No router model invocation in $technology run $($run.id)."
        }
        foreach ($event in $run.events) {
            if ($event.kind -in @('agent.started', 'agent.completed') -and $event.agent -notin $expectedAgents) {
                throw "Unexpected agent lifecycle event for $($event.agent) in $technology."
            }
            if ($event.kind -in @('protocol.request', 'protocol.response')) {
                if ($technology -ne 'a2a') {
                    if ($event.agent -ne 'router' -or $event.data.protocol -cne 'HTTP' -or $event.data.service -cnotin $serviceNames) {
                        throw "Direct $technology traffic must be HTTP emitted by router with an explicit business service target."
                    }
                    if ($event.kind -eq 'protocol.request') {
                        [void]$seenRequests.Add($event.data.service)
                        $protocolEvents++
                    } else {
                        [void]$seenResponses.Add($event.data.service)
                    }
                } elseif ($event.data.protocol -ceq 'A2A') {
                    if ($event.agent -notin $serviceNames) {
                        throw "A2A event has no known remote agent target: $($event.agent)."
                    }
                    if ($event.kind -eq 'protocol.request' -and $event.data.method -eq 'message/send') {
                        [void]$seenRequests.Add($event.agent)
                        $protocolEvents++
                    } elseif ($event.kind -eq 'protocol.response') {
                        [void]$seenResponses.Add($event.agent)
                    }
                }
            }
            if ($event.kind -like 'skill.*' -and $technology -ne 'skills') {
                throw "Unexpected skill event in $technology."
            }
            if ($event.kind -eq 'skill.loaded') {
                if ($event.agent -ne 'router' -or $event.data.provider -ne 'AgentSkillsProvider') {
                    throw 'Native skills must be loaded by the router, not by specialist agents.'
                }
                $names = @($event.data.arguments.PSObject.Properties.Value | Where-Object { $_ -cin $skillNames })
                if ($names.Count -ne 1) {
                    throw 'Native skill.loaded event must identify exactly one service skill in its recorded arguments.'
                }
                [void]$loadedSkills.Add($names[0])
                $skillEvents++
            }
            if ($event.kind -eq 'tool.called') {
                if ($technology -ne 'a2a' -and $event.message -like '*_agent') {
                    throw "Unexpected agent delegation tool in $technology."
                }
                if ($event.message -in @('load_skill', 'read_skill_resource') -and
                    ($technology -ne 'skills' -or $event.agent -ne 'router')) {
                    throw 'Native skill tools must execute only in the Skills router.'
                }
            }
        }
        $lastRun = $run
        Write-Output "$technology $($run.id): completed, $($run.calls.Count) model invocations (MOCK, not billed)."
    }
    foreach ($expected in $expectedAgents) {
        if (-not $seenAgents.Contains($expected)) {
            throw "Agent $expected was not executed in $technology."
        }
    }
    foreach ($service in $serviceNames) {
        if (-not $seenRequests.Contains($service) -or -not $seenResponses.Contains($service)) {
            throw "No complete recorded exchange with $service across the six turns in $technology."
        }
    }
    if ($technology -eq 'skills') {
        foreach ($skill in $skillNames) {
            if (-not $loadedSkills.Contains($skill)) {
                throw "Native service skill $skill was not loaded by the router across the six turns."
            }
        }
    }
    if ($lastRun.result.answer -notmatch 'RET-') {
        throw 'The final confirmed turn did not produce a synthetic return draft.'
    }
    $summary.Add([pscustomobject]@{
        Technology = $technology
        Turns = 6
        Agents = $seenAgents.Count
        ExecutionAgents = (@($seenAgents | Sort-Object) -join ',')
        ServiceTargets = (@($seenRequests | Sort-Object) -join ',')
        ProtocolRequests = $protocolEvents
        SkillLoads = $skillEvents
        NativeSkills = (@($loadedSkills | Sort-Object) -join ',')
        ConversationId = $conversation.id
    })
}
$summary | Format-Table -AutoSize
Write-Output 'PASS: six turns per architecture; actual execution agents, all three remote service targets and native router skills verified with MOCK inference. No model performance/cost claims.'
