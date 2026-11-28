[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
    [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
    [string]$ExpectedCameraName = "",
    [string]$ExpectedUnityCameraName = "",
    [string]$EvidenceDirectory = "",
    [string]$PlayerCacheRoot = "",
    [string]$InitialVrm = "",
    [string]$ReplacementVrm = "",
    [string]$Config = "",
    [ValidateRange(0, 4)][int]$SoakHours = 0,
    [ValidateRange(2, 30)][int]$CameraPollSeconds = 5
)

# Interactive Windows physical-test harness. It records machine evidence and
# operator answers, but never marks physicalHumanAcceptance true.
$ErrorActionPreference = 'Stop'

function Write-JsonFile {
    param([string]$Path, [object]$Value)
    $json = ConvertTo-Json -InputObject $Value -Depth 16
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, $encoding)
}

function Get-TextSha256 {
    param([string]$Text)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    } finally {
        $algorithm.Dispose()
    }
}

function Get-WebcamInventory {
    $rows = @()
    $source = 'Get-PnpDevice'
    $devices = @()
    $pnpCommand = Get-Command Get-PnpDevice -ErrorAction SilentlyContinue
    if ($pnpCommand) {
        try {
            $devices = @(Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object {
                $_.Class -in @('Camera', 'Image') -or
                $_.FriendlyName -match '(?i)camera|webcam|video capture|uvc'
            })
            foreach ($device in $devices) {
                $friendlyName = [string]$device.FriendlyName
                $className = [string]$device.Class
                $instance = [string]$device.InstanceId
                $rows += [pscustomobject]@{
                    friendlyName = $friendlyName
                    class = $className
                    status = [string]$device.Status
                    manufacturer = [string]$device.Manufacturer
                    instanceIdSha256 = if ($instance) { Get-TextSha256 $instance } else { $null }
                }
            }
        } catch {
            $source = 'Win32_PnPEntity fallback'
            $devices = @()
            $rows = @()
        }
    } else {
        $source = 'Win32_PnPEntity fallback'
    }

    if ($source -eq 'Get-PnpDevice' -and $rows.Count -eq 0) {
        $source = 'Win32_PnPEntity fallback'
    }
    if ($source -eq 'Win32_PnPEntity fallback') {
        $devices = @(Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction Stop | Where-Object {
            $_.PNPClass -in @('Camera', 'Image') -or
            $_.Name -match '(?i)camera|webcam|video capture|uvc'
        })
        foreach ($device in $devices) {
            $code = $device.ConfigManagerErrorCode
            $status = if ($null -eq $code -or $code -eq 0) { 'OK' } else { "Problem:$code" }
            $instance = [string]$device.PNPDeviceID
            $rows += [pscustomobject]@{
                friendlyName = [string]$device.Name
                class = [string]$device.PNPClass
                status = $status
                manufacturer = [string]$device.Manufacturer
                instanceIdSha256 = if ($instance) { Get-TextSha256 $instance } else { $null }
            }
        }
    }

    return [pscustomobject]@{
        capturedAtUtc = [DateTime]::UtcNow.ToString('o')
        source = $source
        devices = @($rows | Sort-Object friendlyName, instanceIdSha256)
    }
}

function Test-TargetCameraPresent {
    param([object]$Snapshot, [string]$Fingerprint, [string]$FriendlyName)
    foreach ($device in @($Snapshot.devices)) {
        if (($Fingerprint -and $device.instanceIdSha256 -eq $Fingerprint) -or
            (-not $Fingerprint -and $FriendlyName -and [string]::Equals(
                [string]$device.friendlyName, $FriendlyName,
                [StringComparison]::OrdinalIgnoreCase))) {
            return $true
        }
    }
    return $false
}

function Read-OperatorResult {
    param([string]$Prompt, [switch]$AllowNotApplicable)
    $choices = if ($AllowNotApplicable) { 'pass/fail/not-run/not-applicable' } else { 'pass/fail/not-run' }
    while ($true) {
        $value = (Read-Host "$Prompt [$choices]").Trim().ToLowerInvariant()
        if (-not $value) { return 'not-run' }
        if ($value -in @('pass', 'fail', 'not-run')) { return $value }
        if ($AllowNotApplicable -and $value -in @('not-applicable', 'n/a', 'na')) {
            return 'not-applicable'
        }
        Write-Host "Enter one of: $choices" -ForegroundColor Yellow
    }
}

function ConvertTo-PowerShellLiteral {
    param([string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

if ($ExpectedSourceSha -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'ExpectedSourceSha must be the verified 40-character SHA from the successful Actions run.'
}
$ExpectedSourceSha = $ExpectedSourceSha.ToLowerInvariant()
if (-not $EvidenceDirectory) {
    $stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
    $EvidenceDirectory = Join-Path $env:LOCALAPPDATA "VirtualCharacterRender\P1-Webcam-Evidence\$stamp"
}
$evidenceRoot = [System.IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Refusing to overwrite evidence directory: $evidenceRoot"
}
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

$playerRunDirectory = Join-Path $evidenceRoot 'PlayerRun'
New-Item -ItemType Directory -Path $playerRunDirectory | Out-Null
$screenshotsDirectory = Join-Path $evidenceRoot 'Screenshots'
New-Item -ItemType Directory -Path $screenshotsDirectory | Out-Null
$startedAtUtc = [DateTime]::UtcNow.ToString('o')
$failure = $null
$playerExitCode = $null
$playerSessionStartedAtUtc = $null
$playerSessionEndedAtUtc = $null
$artifactRoot = $null
$playerDirectory = $null
$manifestPath = $null
$manifestEvidence = $null
$cameraBefore = $null
$cameraAfter = $null
$selectedCamera = $null
$timeline = New-Object 'System.Collections.Generic.List[object]'
$queryErrors = New-Object 'System.Collections.Generic.List[string]'
$operatorChecklist = [ordered]@{
    cameraPrivacyPermission = 'not-run'
    mediaPipeSourceHealthy = 'not-run'
    faceOrBodyTrackingResponded = 'not-run'
    disablingReleasedCamera = 'not-run'
    reenableRecoveredWithoutRestart = 'not-run'
    usbDisconnectReconnect = 'not-run'
    vrmLoadAndSwap = 'not-run'
}
$launchEvidencePassed = $false

try {
    if ($InitialVrm) { $InitialVrm = (Resolve-Path -LiteralPath $InitialVrm -ErrorAction Stop).Path }
    if ($ReplacementVrm) { $ReplacementVrm = (Resolve-Path -LiteralPath $ReplacementVrm -ErrorAction Stop).Path }
    if ($ReplacementVrm -and -not $InitialVrm) {
        throw '-ReplacementVrm requires -InitialVrm so the load-and-swap sequence has a defined first model.'
    }
    if ($Config) { $Config = (Resolve-Path -LiteralPath $Config -ErrorAction Stop).Path }

    $artifactInput = (Resolve-Path -LiteralPath $ArtifactDirectory -ErrorAction Stop).Path
    $zipCandidates = @(Get-ChildItem -LiteralPath $artifactInput -Filter 'windows.zip' -File -Recurse |
        Where-Object {
            (Test-Path -LiteralPath (Join-Path $_.DirectoryName 'windows.sha256')) -and
            (Test-Path -LiteralPath (Join-Path $_.DirectoryName 'windows.source-sha')) -and
            (Test-Path -LiteralPath (Join-Path $_.DirectoryName 'windows.payload-sha256.json'))
        })
    if ($zipCandidates.Count -ne 1) {
        throw "Expected one complete Windows IL2CPP artifact set below '$artifactInput'; found $($zipCandidates.Count)."
    }
    $artifactRoot = $zipCandidates[0].Directory.FullName
    $manifestPath = Join-Path $artifactRoot 'windows.payload-sha256.json'
    $artifactSource = (Get-Content -LiteralPath (Join-Path $artifactRoot 'windows.source-sha') -Raw).Trim()
    if ($artifactSource -ine $ExpectedSourceSha) {
        throw "Artifact source SHA $artifactSource does not match ExpectedSourceSha $ExpectedSourceSha."
    }
    $downloadReceiptPath = Join-Path $artifactRoot 'download-receipt.json'
    if (-not (Test-Path -LiteralPath $downloadReceiptPath -PathType Leaf)) {
        throw 'Run tools/get-p1-windows-il2cpp-artifact.ps1 first; the artifact download receipt is missing.'
    }
    $downloadReceipt = Get-Content -LiteralPath $downloadReceiptPath -Raw | ConvertFrom-Json
    $expectedArtifactName = "u1-p1-windows-il2cpp-$($downloadReceipt.runId)-$($downloadReceipt.attempt)"
    if ($downloadReceipt.schema -ne 'vcr-p1-windows-artifact-download-v1' -or
        $downloadReceipt.workflowName -ne 'U1 P1 Windows IL2CPP build and native smoke (manual)' -or
        $downloadReceipt.conclusion -ne 'success' -or
        $downloadReceipt.runId -le 0 -or
        $downloadReceipt.artifactName -cne $expectedArtifactName -or
        $downloadReceipt.sourceSha -ine $ExpectedSourceSha -or
        $downloadReceipt.verifiedPublisherIdentity -ne $false -or
        $downloadReceipt.physicalHumanAcceptance -ne $false) {
        throw 'Artifact download receipt is invalid or does not match the expected source.'
    }

    $cameraBefore = Get-WebcamInventory
    Write-JsonFile (Join-Path $evidenceRoot 'camera-inventory-before.json') $cameraBefore
    $availableCameras = @($cameraBefore.devices)
    if ($availableCameras.Count -eq 0) {
        throw 'Windows PnP/CIM enumeration found no present camera. Check Device Manager and camera privacy settings.'
    }
    if ($ExpectedCameraName) {
        $cameraMatches = @($availableCameras | Where-Object {
            ([string]$_.friendlyName).IndexOf($ExpectedCameraName, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
        if ($cameraMatches.Count -eq 0) {
            throw "ExpectedCameraName '$ExpectedCameraName' did not match a present device."
        }
        if ($cameraMatches.Count -eq 1) { $selectedCamera = $cameraMatches[0] }
        else { $availableCameras = $cameraMatches }
    }
    if (-not $selectedCamera -and $availableCameras.Count -eq 1) {
        $selectedCamera = $availableCameras[0]
    } elseif (-not $selectedCamera) {
        if ($ExpectedCameraName) { Write-Host "Several devices match '$ExpectedCameraName':" }
        else { Write-Host 'Multiple cameras are present:' }
        for ($index = 0; $index -lt $availableCameras.Count; $index++) {
            Write-Host ("[{0}] {1} ({2}, {3})" -f $index, $availableCameras[$index].friendlyName,
                $availableCameras[$index].class, $availableCameras[$index].status)
        }
        $chosen = Read-Host 'Enter the index of the webcam to validate'
        $chosenIndex = -1
        if (-not [int]::TryParse($chosen, [ref]$chosenIndex) -or
            $chosenIndex -lt 0 -or $chosenIndex -ge $availableCameras.Count) {
            throw 'Invalid webcam index.'
        }
        $selectedCamera = $availableCameras[$chosenIndex]
    }
    if ([string]$selectedCamera.status -ne 'OK') {
        throw "Selected camera '$($selectedCamera.friendlyName)' reports Windows status '$($selectedCamera.status)'. Resolve Device Manager errors before the Player test."
    }

    if (-not $PlayerCacheRoot) {
        $PlayerCacheRoot = Join-Path $env:LOCALAPPDATA 'VirtualCharacterRender\P1Players'
    }
    $playerCacheRootFull = [System.IO.Path]::GetFullPath($PlayerCacheRoot)
    $playerDirectory = Join-Path $playerCacheRootFull $ExpectedSourceSha
    New-Item -ItemType Directory -Path $playerCacheRootFull -Force | Out-Null

    $python = Get-Command py.exe -ErrorAction SilentlyContinue
    $pythonPrefix = @('-3')
    if (-not $python) {
        $python = Get-Command python.exe -ErrorAction SilentlyContinue
        $pythonPrefix = @()
    }
    if (-not $python) { throw 'Python 3 was not found. Install Python 3 or the Windows py launcher.' }

    $archiveVerifier = Join-Path $PSScriptRoot 'verify-p1-il2cpp-archive.py'
    $archiveArgs = @($pythonPrefix) + @($archiveVerifier, '--artifact-dir', $artifactRoot, '--source', $ExpectedSourceSha)
    & $python.Source @archiveArgs
    if ($LASTEXITCODE -ne 0) { throw "IL2CPP archive verification failed with exit code $LASTEXITCODE." }

    if (-not (Test-Path -LiteralPath $playerDirectory)) {
        $prepareScript = Join-Path $PSScriptRoot 'prepare-p1-physical-player.py'
        $prepareArgs = @($pythonPrefix) + @($prepareScript, '--artifact-dir', $artifactRoot,
            '--source', $ExpectedSourceSha, '--output', $playerDirectory)
        & $python.Source @prepareArgs
        if ($LASTEXITCODE -ne 0) { throw "Verified Player extraction failed with exit code $LASTEXITCODE." }
    }
    $payloadVerifier = Join-Path $PSScriptRoot 'verify-p1-player-payload.ps1'
    $manifestEvidence = & $payloadVerifier -PlayerDirectory $playerDirectory `
        -ManifestFile $manifestPath -ExpectedSourceSha $ExpectedSourceSha
    if ($manifestEvidence.sourceSha -ine $ExpectedSourceSha) { throw 'Extracted Player source SHA mismatch.' }

    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $evidenceRoot 'windows.payload-sha256.json')
    Copy-Item -LiteralPath (Join-Path $artifactRoot 'windows.sha256') -Destination $evidenceRoot
    Copy-Item -LiteralPath (Join-Path $artifactRoot 'windows.source-sha') -Destination $evidenceRoot
    Copy-Item -LiteralPath $downloadReceiptPath -Destination $evidenceRoot

    Write-Host ''
    Write-Host 'Physical webcam test sequence:'
    Write-Host '1. In Tracking, add or open MediaPipe Webcam, select the intended camera, and enable it.'
    Write-Host '2. Keep a face/body in view; move to confirm live tracking. Then turn the source Off.'
    Write-Host '3. Turn it On again. If this is a USB camera, disconnect for at least 10 seconds, reconnect, and use Recover if needed.'
    Write-Host '4. Close the Player normally after the checks. Do not let the script infer pass from log cleanliness alone.'
    if ($SoakHours -gt 0) {
        Write-Host ("5. Keep the Player open for at least {0} hours for this soak run." -f $SoakHours)
    }
    if ($ExpectedUnityCameraName) {
        Write-Host "Expected Unity WebCamTexture device label: $ExpectedUnityCameraName"
    }
    Write-Host ("Evidence will be stored at: {0}" -f $evidenceRoot)
    Write-Host ("Evidence collection started UTC: {0}" -f $startedAtUtc)
    [void](Read-Host 'Press Enter to start the interactive Unity Player')

    $launcher = Join-Path $PSScriptRoot 'run-p1-player.ps1'
    $psExe = (Get-Command powershell.exe -ErrorAction Stop).Source
    $childCommand = "& $(ConvertTo-PowerShellLiteral $launcher) -Mode IL2CPP " +
        "-PlayerDirectory $(ConvertTo-PowerShellLiteral $playerDirectory) " +
        "-EvidenceDirectory $(ConvertTo-PowerShellLiteral $playerRunDirectory) " +
        "-ManifestFile $(ConvertTo-PowerShellLiteral $manifestPath) " +
        "-ExpectedSourceSha $(ConvertTo-PowerShellLiteral $ExpectedSourceSha)"
    if ($InitialVrm) { $childCommand += " -Vrm $(ConvertTo-PowerShellLiteral $InitialVrm)" }
    if ($Config) { $childCommand += " -Config $(ConvertTo-PowerShellLiteral $Config)" }
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCommand))
    $child = Start-Process -FilePath $psExe `
        -ArgumentList @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encodedCommand) `
        -WorkingDirectory (Split-Path -Parent $PSScriptRoot) -NoNewWindow -PassThru
    $playerSessionStartedAtUtc = [DateTime]::UtcNow
    Write-Host 'Player started. Keep this PowerShell window open; PnP camera removal/return will be reported below.'

    $targetHash = [string]$selectedCamera.instanceIdSha256
    $targetName = [string]$selectedCamera.friendlyName
    $previousPresence = Test-TargetCameraPresent $cameraBefore $targetHash $targetName
    $timeline.Add([pscustomobject]@{
        capturedAtUtc = [DateTime]::UtcNow.ToString('o')
        targetPresent = $previousPresence
        presentCameraCount = $cameraBefore.devices.Count
        event = 'baseline'
    })
    while (-not $child.HasExited) {
        try {
            $snapshot = Get-WebcamInventory
            $present = Test-TargetCameraPresent $snapshot $targetHash $targetName
            if ($present -ne $previousPresence) {
                $timeline.Add([pscustomobject]@{
                    capturedAtUtc = $snapshot.capturedAtUtc
                    targetPresent = $present
                    presentCameraCount = $snapshot.devices.Count
                    event = if ($present) { 'camera-returned' } else { 'camera-disappeared' }
                })
                $presenceText = if ($present) { 'RETURNED' } else { 'DISAPPEARED' }
                Write-Host ("{0}: selected PnP camera '{1}' at {2}" -f
                    $presenceText, $targetName, $snapshot.capturedAtUtc) -ForegroundColor Yellow
                $previousPresence = $present
            }
        } catch {
            $queryErrors.Add($_.Exception.Message)
        }
        Start-Sleep -Seconds $CameraPollSeconds
        $child.Refresh()
    }
    $child.WaitForExit()
    $playerSessionEndedAtUtc = [DateTime]::UtcNow
    $playerExitCode = $child.ExitCode
    Write-Host "Player process ended with exit code $playerExitCode."
    if ($SoakHours -gt 0 -and
        $playerSessionEndedAtUtc -lt $playerSessionStartedAtUtc.AddHours($SoakHours)) {
        throw ("Player exited before the requested {0}-hour soak completed." -f $SoakHours)
    }
    $cameraAfter = Get-WebcamInventory
    Write-JsonFile (Join-Path $evidenceRoot 'camera-inventory-after.json') $cameraAfter
    Write-JsonFile (Join-Path $evidenceRoot 'camera-presence-events.json') @($timeline.ToArray())

    $operatorChecklist.cameraPrivacyPermission = Read-OperatorResult 'Windows desktop camera privacy permission allowed?'
    $operatorChecklist.mediaPipeSourceHealthy = Read-OperatorResult 'Did MediaPipe Webcam show Healthy with the intended camera?'
    $operatorChecklist.faceOrBodyTrackingResponded = Read-OperatorResult 'Did tracking respond to face/body movement in frame?'
    $operatorChecklist.disablingReleasedCamera = Read-OperatorResult 'When disabled, did status turn Off/Stopped and did the camera release?'
    $operatorChecklist.reenableRecovered = Read-OperatorResult 'Did re-enable recover capture without restarting the Player?'
    $operatorChecklist.usbDisconnectReconnect = Read-OperatorResult `
        'Did unplug/replug plus recovery work? Use not-applicable for a fixed internal webcam.' -AllowNotApplicable
    if ($InitialVrm -or $ReplacementVrm) {
        $operatorChecklist.vrmLoadAndSwap = Read-OperatorResult 'Did the provided VRM load and replacement work through the UI?'
    }
    $operatorChecklist.notes = Read-Host 'Optional test notes (leave blank if none)'
} catch {
    $failure = $_.Exception.Message
}

if (-not $cameraAfter) {
    try { $cameraAfter = Get-WebcamInventory } catch { $cameraAfter = [pscustomobject]@{ devices = @(); source = 'unavailable' } }
}
$launchRecordPath = Join-Path $playerRunDirectory 'launch-evidence.json'
$playerLogPath = Join-Path $playerRunDirectory 'Player.log'
$launchRecord = $null
if (Test-Path -LiteralPath $launchRecordPath) {
    try { $launchRecord = Get-Content -LiteralPath $launchRecordPath -Raw | ConvertFrom-Json } catch {}
}
$session = [ordered]@{
    schema = 'vcr-p1-windows-webcam-session-v1'
    expectedSourceSha = $ExpectedSourceSha
    runStartedAtUtc = $startedAtUtc
    requestedSoakHours = $SoakHours
    playerSessionStartedAtUtc = if ($playerSessionStartedAtUtc) { $playerSessionStartedAtUtc.ToString('o') } else { $null }
    playerSessionEndedAtUtc = if ($playerSessionEndedAtUtc) { $playerSessionEndedAtUtc.ToString('o') } else { $null }
    playerSessionDurationHours = if ($playerSessionStartedAtUtc -and $playerSessionEndedAtUtc) {
        [Math]::Round(($playerSessionEndedAtUtc - $playerSessionStartedAtUtc).TotalHours, 3)
    } else { $null }
    expectedCameraName = if ($selectedCamera) { $selectedCamera.friendlyName } else { $ExpectedCameraName }
    expectedUnityCameraName = $ExpectedUnityCameraName
    expectedCameraInstanceIdSha256 = if ($selectedCamera) { $selectedCamera.instanceIdSha256 } else { $null }
    artifactRunId = if (Test-Path -LiteralPath (Join-Path $evidenceRoot 'download-receipt.json')) {
        (Get-Content -LiteralPath (Join-Path $evidenceRoot 'download-receipt.json') -Raw | ConvertFrom-Json).runId
    } else { $null }
    artifactName = if (Test-Path -LiteralPath (Join-Path $evidenceRoot 'download-receipt.json')) {
        (Get-Content -LiteralPath (Join-Path $evidenceRoot 'download-receipt.json') -Raw | ConvertFrom-Json).artifactName
    } else { $null }
    artifactArchiveSha256 = if ($artifactRoot -and (Test-Path (Join-Path $artifactRoot 'windows.zip'))) {
        (Get-FileHash -LiteralPath (Join-Path $artifactRoot 'windows.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
    } else { $null }
    artifactDirectory = if ($artifactRoot) { [System.IO.Path]::GetFullPath($artifactRoot) } else { $null }
    payloadManifestSha256 = if ($manifestEvidence) { $manifestEvidence.manifestSha256 } else { $null }
    payloadFileCount = if ($manifestEvidence) { $manifestEvidence.fileCount } else { $null }
    capturedAtUtc = [DateTime]::UtcNow.ToString('o')
    playerExitCode = $playerExitCode
    playerLogPath = if (Test-Path -LiteralPath $playerLogPath) { 'PlayerRun/Player.log' } else { $null }
    playerLogSha256 = if (Test-Path -LiteralPath $playerLogPath) {
        (Get-FileHash -LiteralPath $playerLogPath -Algorithm SHA256).Hash.ToLowerInvariant()
    } else { $null }
    playerLogBytes = if (Test-Path -LiteralPath $playerLogPath) { (Get-Item -LiteralPath $playerLogPath).Length } else { $null }
    launchEvidencePresent = ($null -ne $launchRecord)
    cameraInventoryBefore = if ($cameraBefore) { $cameraBefore } else { $null }
    cameraInventoryAfter = $cameraAfter
    cameraPresenceEvents = @($timeline.ToArray())
    inventoryQueryErrors = @($queryErrors.ToArray())
    operatorChecklist = $operatorChecklist
    vrmTestRequested = [bool]($InitialVrm -or $ReplacementVrm)
    initialVrmFileName = if ($InitialVrm) { [System.IO.Path]::GetFileName($InitialVrm) } else { $null }
    replacementVrmFileName = if ($ReplacementVrm) { [System.IO.Path]::GetFileName($ReplacementVrm) } else { $null }
    setupFailure = $failure
    physicalHumanAcceptance = $false
    status = 'MACHINE_EVIDENCE_REQUIRES_INSPECTION'
}
Write-JsonFile (Join-Path $evidenceRoot 'webcam-session.json') $session
$screenshotIndex = @(
    Get-ChildItem -LiteralPath $screenshotsDirectory -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -match '^\.(png|jpe?g)$' } |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                file = $_.Name
                bytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
)
Write-JsonFile (Join-Path $evidenceRoot 'screenshot-index.json') $screenshotIndex

if ($failure) {
    Write-Error "Webcam validation did not complete: $failure. Evidence: $evidenceRoot"
    exit 1
}

$inspector = Join-Path $PSScriptRoot 'inspect-p1-windows-webcam-evidence.ps1'
$inspection = & $inspector -EvidenceDirectory $evidenceRoot `
    -ExpectedSourceSha $ExpectedSourceSha -ReportOnly
$inspection | Format-List
Write-Host "Webcam evidence directory: $evidenceRoot"
Write-Host 'physicalHumanAcceptance remains false; reviewer sign-off is separate.'
if (-not $inspection.machineEvidenceComplete -or
    -not $inspection.operatorChecklistComplete -or
    $inspection.operatorFailureCount -gt 0) {
    throw "Webcam evidence is incomplete or contains operator-reported failure: $($inspection.status)."
}
Write-Host 'Machine evidence and required operator answers were recorded; acceptance still requires independent review.'
