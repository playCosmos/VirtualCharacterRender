[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][long]$RunId,
    [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
    [string]$OutputDirectory = ""
)

# Download one exact successful U1 P1 Windows IL2CPP artifact via GitHub CLI.
# The output contains the untouched ZIP plus its source/hash/manifest sidecars.
$ErrorActionPreference = 'Stop'
if ($ExpectedSourceSha -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'ExpectedSourceSha must be the verified 40-character Git commit SHA.'
}
$ExpectedSourceSha = $ExpectedSourceSha.ToLowerInvariant()

$gh = Get-Command gh.exe -ErrorAction SilentlyContinue
if (-not $gh) { $gh = Get-Command gh -ErrorAction SilentlyContinue }
if (-not $gh) { throw 'GitHub CLI (gh) is required. Install it and run gh auth login.' }
& $gh.Source auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not authenticated for this repository.' }

$runJson = & $gh.Source run view "$RunId" --json attempt,databaseId,headSha,conclusion,workflowName,status
if ($LASTEXITCODE -ne 0) { throw "Could not read GitHub Actions run $RunId." }
$run = $runJson | ConvertFrom-Json
if ($run.databaseId -ne $RunId -or
    $run.workflowName -ne 'U1 P1 Windows IL2CPP build and native smoke (manual)' -or
    $run.conclusion -ne 'success' -or
    $run.headSha -ine $ExpectedSourceSha) {
    throw ("Run mismatch/rejection: id={0}, workflow='{1}', conclusion='{2}', SHA={3}" -f
        $run.databaseId, $run.workflowName, $run.conclusion, $run.headSha)
}

$artifactsJson = & $gh.Source api "repos/playCosmos/VirtualCharacterRender/actions/runs/$RunId/artifacts?per_page=100"
if ($LASTEXITCODE -ne 0) { throw "Could not list artifacts for run $RunId." }
$artifacts = ($artifactsJson | ConvertFrom-Json).artifacts
$artifactName = "u1-p1-windows-il2cpp-$RunId-$($run.attempt)"
$matchingArtifacts = @($artifacts | Where-Object {
    $_.name -ceq $artifactName -and
    -not $_.expired
})
if ($matchingArtifacts.Count -ne 1) {
    throw "Expected one unexpired IL2CPP artifact named '$artifactName'; found $($matchingArtifacts.Count)."
}
$artifact = $matchingArtifacts[0]

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $env:LOCALAPPDATA `
        "VirtualCharacterRender\P1Artifacts\$ExpectedSourceSha\$RunId"
}
$destination = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) {
    throw "Refusing to overwrite existing artifact directory: $destination"
}
$parent = Split-Path -Parent $destination
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$stage = Join-Path $parent ('.vcr-download-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    & $gh.Source run download "$RunId" --name "$artifactName" --dir "$stage"
    if ($LASTEXITCODE -ne 0) { throw "GitHub artifact download failed for '$artifactName'." }

    $requiredFiles = @('windows.zip', 'windows.sha256', 'windows.source-sha', 'windows.payload-sha256.json')
    foreach ($name in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)) {
            throw "Downloaded artifact is missing required sidecar: $name"
        }
    }
    $sidecarSha = (Get-Content -LiteralPath (Join-Path $stage 'windows.source-sha') -Raw).Trim()
    if ($sidecarSha -ine $ExpectedSourceSha) { throw 'Downloaded source-SHA sidecar does not match the expected commit.' }
    $manifest = Get-Content -LiteralPath (Join-Path $stage 'windows.payload-sha256.json') -Raw | ConvertFrom-Json
    if ($manifest.sourceSha -ine $ExpectedSourceSha -or
        $manifest.format -ne 'vcr-p1-payload-sha256-v1' -or
        @($manifest.files).Count -le 0) {
        throw 'Downloaded payload manifest is not valid for the expected source SHA.'
    }

    $zipHash = (Get-FileHash -LiteralPath (Join-Path $stage 'windows.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLine = (Get-Content -LiteralPath (Join-Path $stage 'windows.sha256') -Raw).Trim()
    $checksumMatch = [regex]::Match($checksumLine, '^([a-fA-F0-9]{64})\s+\*?windows\.zip$')
    if (-not $checksumMatch.Success -or
        $checksumMatch.Groups[1].Value.ToLowerInvariant() -ne $zipHash) {
        throw 'Downloaded windows.zip does not match windows.sha256.'
    }

    $receipt = [ordered]@{
        schema = 'vcr-p1-windows-artifact-download-v1'
        runId = $RunId
        attempt = [int]$run.attempt
        workflowName = $run.workflowName
        conclusion = $run.conclusion
        sourceSha = $ExpectedSourceSha
        artifactId = $artifact.id
        artifactName = $artifactName
        archiveSha256 = $zipHash
        manifestFileCount = @($manifest.files).Count
        downloadedAtUtc = [DateTime]::UtcNow.ToString('o')
        verifiedPublisherIdentity = $false
        physicalHumanAcceptance = $false
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $stage 'download-receipt.json'),
        (ConvertTo-Json -InputObject $receipt -Depth 8) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    if (Test-Path -LiteralPath $destination) { throw 'Output directory was created during download; refusing overwrite.' }
    Move-Item -LiteralPath $stage -Destination $destination
    Write-Host "Downloaded successful P1 IL2CPP artifact: $destination"
    Write-Host "Source: $ExpectedSourceSha"
    Write-Host "Artifact: $artifactName (id $($artifact.id))"
    Write-Host "ZIP SHA-256: $zipHash"
    Write-Host 'The archive content/source hash was checked; publisher identity and physical acceptance are not asserted.'
} finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
