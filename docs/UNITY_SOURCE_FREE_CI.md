# P0–P13 Unity source-free CI validation

This is an **actual Unity Editor 6000.3.25f1 execution**, not a Python static-only validation. The inherited chain runs `VCR.Editor.P13.P13BatchValidation.RunSourceFreeAndExit`, which calls P13 + P12 -> P11 -> ... -> P0 via the existing `RunChecks()` sequence. Unity returns exit code 0 on PASS, 1 on FAIL or an exception.

## CI behavior

- GitHub Actions: `.github/workflows/unity-source-free.yml` on PRs to `develop`, pushes to `develop` and manual dispatch; implementation branch has an explicit temporary push trigger for testing.
- Strictly require committed Unity 6000.3.25f1 generated `packages-lock.json` and `ProjectSettings.asset`, and verify the project dependency graph before Unity runs.
- Bootstrap verified MediaPipe, Pretendard fonts, pinned Ultraleap and patched UniWinC before editor initialization, from a fresh checkout without `unity/Library`.
- Launch `game-ci/unity-builder@v4` in Editor mode with custom `buildMethod` and `manualExit: true`. No standalone player needs to be packaged; that remains a separate alpha release gate.
- Editor writes `Builds/Validation/p0-p13-source-free.json` containing PASS/FAIL, editor version and UTC timestamp. The post-build Python verifier **requires this file and an exact PASS**; an unexpected zero exit code without the marker is not accepted.
- Compare the post-validation `packages-lock.json` and `ProjectSettings.asset` hashes to the committed baseline. A modified lock/settings snapshot fails this reproducibility gate.
- Upload validation report as a GitHub Actions artifact (including on failure if generated), retention 30 days.

The standalone `tools/validate-p13-source-free.sh` / `.ps1` scripts continue to use the same inherited validation entrypoint, now with report emission.

## Explicit limits

P0–P13 source-free PASS is not evidence of Windows/macOS Player startup, native shutdown, VRM 0.x/1.0 geometry/material quality, MediaPipe/Ultraleap physical tracking, OBS alpha capture, or long-duration frame-time performance. Those remain independent gates.

## Acceptance

1. Repository validation and the report-verifier self-test PASS.
2. New `Unity P0-P13 source-free validation` CI job returns success **after real Unity execution**.
3. Evidence artifact exists and verifies `suite=P0-P13`, `unityVersion=6000.3.25f1`, `passed=true`.
4. Strict lock/settings SHA-256 comparison PASS.
5. CI passes for the PR and merged `develop` commit.
