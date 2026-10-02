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
