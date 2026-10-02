# Tools

Developer and content-pipeline tooling lives here.

Runtime-critical behavior must not depend on ad-hoc developer scripts.

## MediaPipe bootstrap

Windows:

```powershell
./tools/bootstrap-mediapipe.ps1
```

macOS:

```bash
chmod +x ./tools/bootstrap-mediapipe.sh
./tools/bootstrap-mediapipe.sh
```

## P0 source-free batch validation

Run the MediaPipe bootstrap first so Unity can resolve the pinned local package.

Windows:

```powershell
./tools/validate-p0-source-free.ps1
```

Override the Unity executable when Unity Hub is installed elsewhere:

```powershell
./tools/validate-p0-source-free.ps1 -UnityEditor "D:\\Unity\\6000.3.25f1\\Editor\\Unity.exe"
```

macOS M1+:

```bash
chmod +x ./tools/validate-p0-source-free.sh
./tools/validate-p0-source-free.sh
```

The macOS script defaults to:

```text
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity
```

Both launchers call:

```text
VCR.Editor.P0.P0BatchValidation.RunSourceFreeAndExit
```

Exit code `0` means the consolidated source-free suite passed. Any suite failure exits with code `1`; launcher/prerequisite errors use a nonzero code as well.

Batch PASS is still not a substitute for physical webcam, ARKit device, transparent-window, OBS, external VMC, real-VRM, or frame-time evidence.

## P1 source-free batch validation

P1 validation includes the inherited P0 source-free suite and the P1 renderer-core lifecycle checks.

Windows:

```powershell
./tools/validate-p1-source-free.ps1
```

macOS M1+:

```bash
chmod +x ./tools/validate-p1-source-free.sh
./tools/validate-p1-source-free.sh
```

The entrypoint is:

```text
VCR.Editor.P1.P1BatchValidation.RunSourceFreeAndExit
```

A batch PASS proves source-free invariants only. It does not replace the deferred physical-device P0 evidence.

## P3 source-free batch validation

P3 validation includes the inherited P0/P1/P2 suites plus built-in tracking lifecycle checks.

Windows:

```powershell
./tools/validate-p3-source-free.ps1
```

macOS M1+:

```bash
chmod +x ./tools/validate-p3-source-free.sh
./tools/validate-p3-source-free.sh
```

The entrypoint is:

```text
VCR.Editor.P3.P3BatchValidation.RunSourceFreeAndExit
```

A PASS validates source-free lifecycle/contracts only. Webcam quality, low-light benefit, Apple ARKit input, device recovery, and tracking performance still require physical execution evidence.

## P2 source-free batch validation

P2 validation includes the inherited P0/P1 source-free suites plus material preset and compatibility checks.

Windows:

```powershell
./tools/validate-p2-source-free.ps1
```

macOS M1+:

```bash
chmod +x ./tools/validate-p2-source-free.sh
./tools/validate-p2-source-free.sh
```

The entrypoint is:

```text
VCR.Editor.P2.P2BatchValidation.RunSourceFreeAndExit
```

A PASS validates source-free material preset behavior, texture-ID resolution, preset persistence, registry reporting, and guarded bundle-failure status. Real VRM/MToon, successful external platform bundle loading, and GPU-specific compatibility still require Unity/player execution evidence.

## P1 application player

Create the P1 runtime scene first:

```text
VCR > P1 > Create Application Runtime Scene
```

Then build from:

```text
VCR > P1 > Build > Windows x64 Development Player
VCR > P1 > Build > Windows x64 Player
VCR > P1 > Build > macOS Development Player
VCR > P1 > Build > macOS Player
```

Windows launch examples:

```powershell
./tools/run-p1-player.ps1
./tools/run-p1-player.ps1 -Vrm "C:\\models\\avatar.vrm"
./tools/run-p1-player.ps1 -Config "C:\\configs\\vcr-runtime-config.json"
./tools/run-p1-player.ps1 -Mode Development -Vrm "C:\\models\\avatar.vrm"
```

macOS launch examples:

```bash
chmod +x ./tools/run-p1-player.sh
./tools/run-p1-player.sh
./tools/run-p1-player.sh --vrm /Users/me/models/avatar.vrm
./tools/run-p1-player.sh --config /Users/me/configs/vcr-runtime-config.json
./tools/run-p1-player.sh --mode development --vrm /Users/me/models/avatar.vrm
```

The P1 application bootstrap uses `--vcr-vrm` and `--vcr-config`. A VRM path is optional; the renderer can start with an empty character slot.

## P0 standalone player launcher

Build the corresponding player from Unity first.

Windows:

```powershell
./tools/run-p0-player.ps1 -Vrm "C:\\models\\avatar.vrm"
./tools/run-p0-player.ps1 -Vrm "C:\\models\\avatar.vrm" -Mode Performance
```

Optional custom shader validation:

```powershell
./tools/run-p0-player.ps1 `
  -Vrm "C:\\models\\avatar.vrm" `
  -ShaderBundle "C:\\bundles\\vcr-shaders-windows" `
  -ShaderId "VCR/Example"
```

macOS:

```bash
chmod +x ./tools/run-p0-player.sh
./tools/run-p0-player.sh --vrm /Users/me/models/avatar.vrm
./tools/run-p0-player.sh --vrm /Users/me/models/avatar.vrm --mode performance
```

Optional custom shader validation:

```bash
./tools/run-p0-player.sh \
  --vrm /Users/me/models/avatar.vrm \
  --shader-bundle /Users/me/bundles/vcr-shaders-macos \
  --shader-id 'VCR/Example'
```

Use `--material-slot` / `-MaterialSlot` only when a specific discovered slot must be targeted. Otherwise the P0 bootstrap selects the first material slot.

Evidence builds automatically record diagnostics/system files. Performance builds do not enable CSV evidence automatically.

## Future tooling areas

- schema validation
- shader/package validation
- asset inspection
- compatibility reporting
- migration utilities
