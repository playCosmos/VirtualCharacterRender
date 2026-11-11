param(
    [Parameter(Mandatory=$true)][string]$PlayerDirectory,
    [Parameter(Mandatory=$true)][string]$ManifestFile,
    [Parameter(Mandatory=$true)][string]$ExpectedSourceSha
)

# Read-only check of extracted payload bytes against a CI-created per-file
# manifest. The expected SHA must come from a separately verified source;
# a sibling manifest alone cannot authenticate a downloaded build.
$ErrorActionPreference = "Stop"
if ($ExpectedSourceSha -notmatch "^[0-9a-fA-F]{40}$") {
    throw "ExpectedSourceSha must be exactly 40 hexadecimal characters."
}
$root = [IO.Path]::GetFullPath($PlayerDirectory)
$prefix = $root.TrimEnd([char[]]@("\", "/")) + [IO.Path]::DirectorySeparatorChar
$manifestPath = (Resolve-Path -LiteralPath $ManifestFile -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "P1 manifest must be a file: $ManifestFile"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.format -ne "vcr-p1-payload-sha256-v1" -or
    $manifest.sourceSha -notmatch "^[0-9a-fA-F]{40}$" -or
    $manifest.sourceSha -ine $ExpectedSourceSha -or
    $null -eq $manifest.files -or @($manifest.files).Count -eq 0 -or
    @($manifest.files).Count -gt 100000) {
    throw "Invalid P1 payload manifest format, source SHA, or inventory."
}

$expected = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.files) {
    $relative = [string]$entry.path
    $components = $relative.Split("/")
    if ([string]::IsNullOrWhiteSpace($relative) -or
        $relative.Contains("\") -or $relative.Contains(":") -or
        $relative.StartsWith("/") -or $relative -match "[\x00-\x1f]" -or
        @($components | Where-Object { $_ -eq "" -or $_ -eq "." -or $_ -eq ".." }).Count -gt 0 -or
        -not $expected.Add($relative)) {
        throw "Unsafe or duplicate P1 payload path: $relative"
    }
    $full = [IO.Path]::GetFullPath((Join-Path $root ($relative.Replace("/", "\"))))
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $full -PathType Leaf)) {
        throw "Missing P1 payload file: $relative"
    }
    $info = Get-Item -LiteralPath $full -Force
    if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $null -eq $entry.size -or [long]$entry.size -ne $info.Length -or
        [string]$entry.sha256 -notmatch "^[0-9a-fA-F]{64}$" -or
        (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ine $entry.sha256) {
        throw "P1 payload hash/size mismatch: $relative"
    }
}
$actual = @(Get-ChildItem -LiteralPath $root -File -Recurse -Force)
if ($actual.Count -ne $expected.Count) {
    throw "P1 payload count mismatch: expected $($expected.Count), found $($actual.Count)"
}
foreach ($info in $actual) {
    $relative = $info.FullName.Substring($prefix.Length).Replace("\", "/")
    if (-not $expected.Contains($relative)) {
        throw "Unexpected P1 payload file: $relative"
    }
}
$reparse = @(Get-ChildItem -LiteralPath $root -Recurse -Force |
    Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
if ($reparse.Count -gt 0) {
    throw "P1 payload contains a reparse point; exact inventory is unsafe."
}

[pscustomobject]@{
    sourceSha = $manifest.sourceSha.ToLowerInvariant()
    manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    fileCount = $expected.Count
}
