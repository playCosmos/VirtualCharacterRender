[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
    [switch]$ReportOnly
)

# Integrity and completeness review for a physical Windows webcam session.
# This script never converts machine logs or operator answers into acceptance.
$ErrorActionPreference = 'Stop'

function Write-JsonFile {
    param([string]$Path, [object]$Value)
    $json = ConvertTo-Json -InputObject $Value -Depth 16
    [System.IO.File]::WriteAllText(
        $Path, $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

if ($ExpectedSourceSha -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'ExpectedSourceSha must be the verified 40-character commit SHA.'
}
$ExpectedSourceSha = $ExpectedSourceSha.ToLowerInvariant()
$root = [System.IO.Path]::GetFullPath($EvidenceDirectory)
$sessionPath = Join-Path $root 'webcam-session.json'
$launchPath = Join-Path $root 'PlayerRun\launch-evidence.json'
$logPath = Join-Path $root 'PlayerRun\Player.log'
$downloadReceiptPath = Join-Path $root 'download-receipt.json'
$problems = New-Object 'System.Collections.Generic.List[string]'
$starts = @()
$rates = @()
$cleanups = @()
$logHash = $null
$launch = $null
$session = $null
$genericInspectionPassed = $false

try {
    foreach ($path in @($sessionPath, $launchPath, $logPath, $downloadReceiptPath,
            (Join-Path $root 'windows.sha256'), (Join-Path $root 'windows.source-sha'),
            (Join-Path $root 'windows.payload-sha256.json'))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required evidence file is missing: $path"
        }
    }
    $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    $launch = Get-Content -LiteralPath $launchPath -Raw | ConvertFrom-Json
    $downloadReceipt = Get-Content -LiteralPath $downloadReceiptPath -Raw | ConvertFrom-Json
    $sourceSidecar = (Get-Content -LiteralPath (Join-Path $root 'windows.source-sha') -Raw).Trim()
    $manifestCopy = Get-Content -LiteralPath (Join-Path $root 'windows.payload-sha256.json') -Raw | ConvertFrom-Json
    $checksumLine = (Get-Content -LiteralPath (Join-Path $root 'windows.sha256') -Raw).Trim()
    $checksumMatch = [regex]::Match($checksumLine, '^([a-fA-F0-9]{64})\s+\*?windows\.zip$')
    if ($session.schema -ne 'vcr-p1-windows-webcam-session-v1' -or
        $session.expectedSourceSha -ine $ExpectedSourceSha -or
        $launch.mode -ne 'IL2CPP' -or
        $launch.expectedSourceSha -ine $ExpectedSourceSha -or
        $launch.payloadManifestVerified -ne $true -or
        $launch.physicalHumanAcceptance -ne $false -or
        $launch.exitCode -ne 0 -or
        $session.playerExitCode -ne 0 -or
        $session.setupFailure -or
        $downloadReceipt.schema -ne 'vcr-p1-windows-artifact-download-v1' -or
        $downloadReceipt.workflowName -ne 'U1 P1 Windows IL2CPP build and native smoke (manual)' -or
        $downloadReceipt.conclusion -ne 'success' -or
        $downloadReceipt.runId -le 0 -or
        $downloadReceipt.artifactName -cne "u1-p1-windows-il2cpp-$($downloadReceipt.runId)-$($downloadReceipt.attempt)" -or
        $downloadReceipt.sourceSha -ine $ExpectedSourceSha -or
        $downloadReceipt.runId -ne $session.artifactRunId -or
        $downloadReceipt.artifactName -ne $session.artifactName -or
        $downloadReceipt.verifiedPublisherIdentity -ne $false -or
        $downloadReceipt.physicalHumanAcceptance -ne $false -or
        $sourceSidecar -ine $ExpectedSourceSha -or
        $manifestCopy.sourceSha -ine $ExpectedSourceSha -or
        $manifestCopy.format -ne 'vcr-p1-payload-sha256-v1' -or
        -not $checksumMatch.Success -or
        $checksumMatch.Groups[1].Value.ToLowerInvariant() -ne $downloadReceipt.archiveSha256) {
        $problems.Add('Session/Player launch evidence is not a clean source-bound IL2CPP run.')
    }

    $logHash = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($launch.playerLogSha256 -ine $logHash -or $session.playerLogSha256 -ine $logHash) {
        $problems.Add('Player.log SHA-256 does not match the launch/session receipts.')
    }
    if ($launch.payloadManifestSha256 -ine $session.payloadManifestSha256) {
        $problems.Add('Payload manifest SHA-256 differs between launch and webcam receipts.')
    }

    $genericInspector = Join-Path $PSScriptRoot 'inspect-p1-physical-evidence.ps1'
    try {
        $null = & $genericInspector -EvidenceDirectory (Join-Path $root 'PlayerRun') `
            -ExpectedSourceSha $ExpectedSourceSha
        $genericInspectionPassed = $true
    } catch {
        $problems.Add('Generic P1 Player lifecycle inspection failed: ' + $_.Exception.Message)
    }

    $startMatches = @(Select-String -LiteralPath $logPath -Pattern `
        "VCR MediaPipe started: device='(?<device>[^']*)', actual=(?<width>\d+)x(?<height>\d+)")
    foreach ($match in $startMatches) {
        $starts += [pscustomobject]@{
            line = $match.LineNumber
            device = $match.Matches[0].Groups['device'].Value
            width = [int]$match.Matches[0].Groups['width'].Value
            height = [int]$match.Matches[0].Groups['height'].Value
        }
    }
    $rateMatches = @(Select-String -LiteralPath $logPath -Pattern `
        'VCR tracking/5s: face=(?<face>\d+), holistic=(?<holistic>\d+), faceState=(?<faceState>[^,]+), holisticState=(?<holisticState>[^,]+), faceDrops=(?<faceDrops>\d+), holisticDrops=(?<holisticDrops>\d+), readbackErrors=(?<readbackErrors>\d+)')
    foreach ($match in $rateMatches) {
        $groups = $match.Matches[0].Groups
        $rates += [pscustomobject]@{
            line = $match.LineNumber
            faceResults = [long]$groups['face'].Value
            holisticResults = [long]$groups['holistic'].Value
            faceState = $groups['faceState'].Value.Trim()
            holisticState = $groups['holisticState'].Value.Trim()
            faceDrops = [long]$groups['faceDrops'].Value
            holisticDrops = [long]$groups['holisticDrops'].Value
            readbackErrors = [long]$groups['readbackErrors'].Value
        }
    }
    $cleanups = @(Select-String -LiteralPath $logPath -SimpleMatch -Pattern 'VCR MediaPipe cleanup completed (')
    $trackingResultSamples = @($rates | Where-Object { $_.faceResults -gt 0 -or $_.holisticResults -gt 0 })
    $validStarts = @($starts | Where-Object { $_.width -gt 16 -and $_.height -gt 16 })
    if ($validStarts.Count -eq 0) { $problems.Add('Player.log contains no valid MediaPipe camera start/resolution marker.') }
    if ($trackingResultSamples.Count -eq 0) { $problems.Add('Player.log contains no positive MediaPipe result-count sample.') }
    if ($cleanups.Count -eq 0) { $problems.Add('Player.log contains no MediaPipe cleanup-completed marker.') }

    $expectedName = [string]$session.expectedUnityCameraName
    if ($expectedName) {
        $nameMatches = @($validStarts | Where-Object {
            $_.device.IndexOf($expectedName, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
        if ($nameMatches.Count -eq 0) {
            $problems.Add("Runtime-selected camera name does not match expected device '$expectedName'.")
        }
    }
    if ($launch.exitCode -ne 0) { $problems.Add('Unity Player did not exit with code 0.') }
} catch {
    $problems.Add($_.Exception.Message)
}

$operator = if ($session) { $session.operatorChecklist } else { $null }
$requiredOperatorFields = @(
    'cameraPrivacyPermission', 'mediaPipeSourceHealthy', 'faceOrBodyTrackingResponded',
    'disablingReleasedCamera', 'reenableRecoveredWithoutRestart'
)
$operatorMissing = New-Object 'System.Collections.Generic.List[string]'
$operatorFailures = New-Object 'System.Collections.Generic.List[string]'
if ($operator) {
    foreach ($field in $requiredOperatorFields) {
        $answer = [string]$operator.$field
        if ($answer -eq 'fail') { $operatorFailures.Add($field) }
        elseif ($answer -ne 'pass') { $operatorMissing.Add($field) }
    }
    $reconnect = [string]$operator.usbDisconnectReconnect
    if ($reconnect -eq 'fail') { $operatorFailures.Add('usbDisconnectReconnect') }
    elseif ($reconnect -notin @('pass', 'not-applicable')) { $operatorMissing.Add('usbDisconnectReconnect') }
    if ($session.vrmTestRequested -eq $true) {
        $vrmResult = [string]$operator.vrmLoadAndSwap
        if ($vrmResult -eq 'fail') { $operatorFailures.Add('vrmLoadAndSwap') }
        elseif ($vrmResult -ne 'pass') { $operatorMissing.Add('vrmLoadAndSwap') }
    }
} else {
    foreach ($field in $requiredOperatorFields + @('usbDisconnectReconnect')) {
        $operatorMissing.Add($field)
    }
}

$cameraBeforeCount = 0
$expectedCameraSeenBefore = $false
$expectedCameraSeenAfter = $false
if ($session -and $session.cameraInventoryBefore) {
    $beforeDevices = @($session.cameraInventoryBefore.devices)
    $cameraBeforeCount = $beforeDevices.Count
    $expectedName = [string]$session.expectedCameraName
    $expectedHash = [string]$session.expectedCameraInstanceIdSha256
    $expectedCameraSeenBefore = @($beforeDevices | Where-Object {
        ($expectedHash -and $_.instanceIdSha256 -eq $expectedHash) -or
        ($expectedName -and [string]$_.friendlyName -eq $expectedName)
    }).Count -gt 0
}
if ($session -and $session.cameraInventoryAfter) {
    $afterDevices = @($session.cameraInventoryAfter.devices)
    $expectedName = [string]$session.expectedCameraName
    $expectedHash = [string]$session.expectedCameraInstanceIdSha256
    $expectedCameraSeenAfter = @($afterDevices | Where-Object {
        ($expectedHash -and $_.instanceIdSha256 -eq $expectedHash) -or
        (-not $expectedHash -and $expectedName -and [string]$_.friendlyName -eq $expectedName)
    }).Count -gt 0
}
if ($cameraBeforeCount -eq 0) { $problems.Add('No Windows camera was recorded in the pre-run PnP/CIM inventory.') }
if ($cameraBeforeCount -gt 0 -and -not $expectedCameraSeenBefore) {
    $problems.Add('The selected webcam is not present in the pre-run Windows inventory.')
}
if (-not $expectedCameraSeenAfter) {
    $problems.Add('The selected webcam was not present in the final Windows inventory; reconnect it before ending the run.')
}
$disconnectEvents = @()
if ($session) {
    $disconnectEvents = @($session.cameraPresenceEvents | Where-Object {
        $_.event -in @('camera-disappeared', 'camera-returned')
    })
}
$sawDisconnect = @($disconnectEvents | Where-Object { $_.event -eq 'camera-disappeared' }).Count -gt 0
$sawReturn = @($disconnectEvents | Where-Object { $_.event -eq 'camera-returned' }).Count -gt 0
if ($operator -and [string]$operator.usbDisconnectReconnect -eq 'pass' -and
    (-not $sawDisconnect -or -not $sawReturn)) {
    $problems.Add('USB reconnect was marked pass, but the selected Windows PnP device disappearance and return were not both observed.')
}
$machineComplete = ($problems.Count -eq 0 -and $genericInspectionPassed)
$operatorComplete = ($operatorMissing.Count -eq 0 -and $operatorFailures.Count -eq 0)
$status = if (-not $machineComplete) {
    'MACHINE_EVIDENCE_INCOMPLETE'
} elseif ($operatorFailures.Count -gt 0) {
    'OPERATOR_REPORTED_FAILURE'
} elseif (-not $operatorComplete) {
    'OPERATOR_CHECKLIST_INCOMPLETE'
} else {
    'COLLECTED_FOR_REVIEW'
}

$inspection = [ordered]@{
    schema = 'vcr-p1-windows-webcam-inspection-v1'
    expectedSourceSha = $ExpectedSourceSha
    status = $status
    machineEvidenceComplete = $machineComplete
    genericPlayerLifecycleInspectionPassed = $genericInspectionPassed
    playerLogSha256 = $logHash
    webcamStartCount = $starts.Count
    validCameraResolutionCount = $validStarts.Count
    trackingRateSampleCount = $rates.Count
    positiveTrackingResultSampleCount = $trackingResultSamples.Count
    cleanupCompletedCount = $cleanups.Count
    cameraInventoryBeforeCount = $cameraBeforeCount
    expectedCameraSeenBefore = $expectedCameraSeenBefore
    expectedCameraSeenAfter = $expectedCameraSeenAfter
    cameraDisconnectObserved = $sawDisconnect
    cameraReturnObserved = $sawReturn
    operatorChecklistComplete = $operatorComplete
    operatorFailureCount = $operatorFailures.Count
    operatorMissingCount = $operatorMissing.Count
    operatorMissingFields = @($operatorMissing.ToArray())
    operatorFailedFields = @($operatorFailures.ToArray())
    machineProblems = @($problems.ToArray())
    physicalHumanAcceptance = $false
    reviewerSignoffRequired = $true
    inspectedAtUtc = [DateTime]::UtcNow.ToString('o')
}
Write-JsonFile (Join-Path $root 'webcam-inspection.json') $inspection
$inspectionObject = [pscustomobject]$inspection
$inspectionObject

if (-not $ReportOnly -and ($status -ne 'COLLECTED_FOR_REVIEW')) {
    throw "Webcam evidence is '$status'; inspect webcam-inspection.json and PlayerRun/Player.log."
}
