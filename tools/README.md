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

## P0 standalone player launcher

Build the corresponding player from Unity first.

Windows:

```powershell
./tools/run-p0-player.ps1 -Vrm "C:\models\avatar.vrm"
./tools/run-p0-player.ps1 -Vrm "C:\models\avatar.vrm" -Mode Performance
```

Optional custom shader validation:

```powershell
./tools/run-p0-player.ps1 `
  -Vrm "C:\models\avatar.vrm" `
  -ShaderBundle "C:\bundles\vcr-shaders-windows" `
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
