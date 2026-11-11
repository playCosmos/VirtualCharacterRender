param(
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [Parameter(Mandatory=$true)][string]$ExpectedSourceSha
)

# Preflight only: verifies source-bound machine evidence after an actual
# interactive P1 IL2CPP session. NEVER asserts human or hardware acceptance.
$ErrorActionPreference = 'Stop'
if ($ExpectedSourceSha -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'ExpectedSourceSha must be a verified 40-character Git commit SHA.'
}
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$recordPath = Join-Path $root 'launch-evidence.json'
$logPath = Join-Path $root 'Player.log'
if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw 'P1 physical evidence requires both launch-evidence.json and Player.log.'
}
$record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
$log = Get-Item -LiteralPath $logPath -Force
if (($log.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
    $log.Length -le 0) {
    throw 'P1 Player.log is empty or references an unsupported reparse point.'
}
if ($record.mode -ne 'IL2CPP' -or
    $record.payloadManifestVerified -ne $true -or
    $record.expectedSourceSha -ine $ExpectedSourceSha -or
    $record.expectedSourceSha -notmatch '^[a-fA-F0-9]{40}$' -or
    $record.payloadManifestSha256 -notmatch '^[a-fA-F0-9]{64}$' -or
    $record.playerExecutableSha256 -notmatch '^[a-fA-F0-9]{64}$' -or
    $null -eq $record.payloadFileCount -or
    $record.payloadFileCount -le 0 -or
    $record.physicalHumanAcceptance -ne $false -or
    $record.playerLogBytes -ne $log.Length -or
    $record.playerLogSha256 -notmatch '^[a-fA-F0-9]{64}$' -or
    (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash -ine $record.playerLogSha256) {
    throw 'P1 physical evidence is incomplete, modified, not source-bound or falsely marked accepted.'
}

# Detect critical runtime faults without misclassifying routine Unity warnings.
# This is a conservative minimum; a clean log is NOT proof of correct UI pixels.
$runtimeFault = Select-String -LiteralPath $logPath -Quiet -Pattern @(
    'NullReferenceException',
    'MissingReferenceException',
    'UnassignedReferenceException',
    'InvalidOperationException:',
    'AccessViolationException',
    'StackOverflowException',
    'VCR U1 .*refresh failed',
    'VCR U1 .*stage failed',
    'Mock test Player - NOT Unity evidence'
)
$exitClean = ($record.exitCode -is [int] -and $record.exitCode -eq 0)
if (-not $exitClean -or $runtimeFault) {
    throw (("P1 operator preflight rejected: exitCode={0}, runtimeFault={1}; inspect Player.log. " +
        "Do not approve this physical run.") -f $record.exitCode, [bool]$runtimeFault)
}

[pscustomobject]@{
    sourceSha = $record.expectedSourceSha
    manifestSha256 = $record.payloadManifestSha256
    payloadFileCount = $record.payloadFileCount
    playerLogSha256 = $record.playerLogSha256
    exitCode = $record.exitCode
    machineEvidencePreflightPassed = $true
    physicalHumanAcceptance = $false
}
