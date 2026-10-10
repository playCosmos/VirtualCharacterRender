#!/usr/bin/env python3
"""Static repository guard for immutable alpha release policy; no Unity required."""
from pathlib import Path

root = Path(__file__).resolve().parents[1]
workflow = (root / ".github/workflows/build-alpha-binaries.yml").read_text(encoding="utf-8")
builder = (root / "unity/Assets/VCR/Editor/Alpha/AlphaBuildMenu.cs").read_text(encoding="utf-8")
obsolete = root / ".github/workflows/publish-alpha-release.yml"
local_build = (root / "tools/build-alpha.ps1").read_text(encoding="utf-8")
local_run = (root / "tools/run-alpha.ps1").read_text(encoding="utf-8")

checks = {
    "legacy mutable release workflow removed": not obsolete.exists(),
    "build is triggered by versioned tags": '      - "v*-alpha.*"' in workflow,
    "release builds do not auto-run from branch pushes": 'release/0.1.0-alpha.1' not in workflow,
    "tag SHA verified": 'git/ref/tags/$tag' in workflow,
    "tag and build SHA match": 'test "$(git rev-parse HEAD)" = "$GITHUB_SHA"' in workflow,
    "existing releases refused": 'Refusing to replace an existing release' in workflow,
    "binary SHA256 verified": 'sha256sum --check' in workflow,
    "source-proof artifact verified": '.source-sha' in workflow,
    "provenance published": 'build-provenance.json' in workflow,
    "overwrite options forbidden": '--clobber' not in workflow and '-F force=true' not in workflow,
    "version file survives Unity builder container boundary": "Builds/Alpha/release-version.txt" in workflow and "Builds/Alpha/release-version.txt" in builder,
    "version supplied by tag to Unity": 'VCR_RELEASE_VERSION:' in workflow and 'VCR_RELEASE_VERSION' in builder,
    "local build and run scripts support same version": "$env:VCR_RELEASE_VERSION = $AlphaVersion" in local_build and "[string]$AlphaVersion" in local_run,
    "release depends on both builds": 'needs: [preflight, build]' in workflow,
    "explicit alpha.2 intent is limited to develop": '"refs/heads/develop"' in workflow and '.github/release-intents/v0.1.0-alpha.2' in workflow and (root / ".github/release-intents/v0.1.0-alpha.2").read_text(encoding="utf-8").strip() == "0.1.0-alpha.2",
    "new tag is created only after both builds pass": workflow.index('needs: [preflight, build]') < workflow.index('Create immutable alpha tag after both builds passed') < workflow.index('Publish immutable prerelease with provenance manifest'),
    "alpha.2 build refuses source SHA mismatch": '-f "sha=$GITHUB_SHA"' in workflow and 'test "$(git rev-parse HEAD)" = "$GITHUB_SHA"' in workflow,
}
for description, passed in checks.items():
    print(f"{'PASS' if passed else 'FAIL'}: {description}")
if not all(checks.values()):
    raise SystemExit(1)
