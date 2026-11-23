# U3 authored-scene read-only acceptance — operator handoff

Scope: existing Alpha and P11 scenes and their corresponding persistent
UI Toolkit PanelSettings. Generated CI fixtures are **not** substitutes
for operator-authored scenes. No release, physical device or OBS approval.

## Preflight

Back up your authored Unity project outside the project directory. Use
**Unity 6000.3.25f1**, open no other Editor instance on the same project,
and verify the following eight existing files under the project root:

- `Assets/VCR/Alpha/AlphaRuntime.unity` and `.unity.meta`
- `Assets/VCR/P11/P11Runtime.unity` and `.unity.meta`
- `Assets/VCR/Alpha/AlphaWorkspacePanelSettings.asset` and `.asset.meta`
- `Assets/VCR/P11/P11WorkspacePanelSettings.asset` and `.asset.meta`

If you only have generated CI scenes rather than authored assets, the
test checks the generated fixture, **not** your real project. The tool
will refuse to create missing scenes or PanelSettings.

## Run against the actual authored Unity project

Windows (PowerShell; adjust paths to your installation):

```powershell
python tools/run-u3-authored-scene-acceptance.py --editor "C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" --project "C:\path\to\VirtualCharacterRender\unity" --output "C:\VCR-U3-acceptance"
```

macOS (shell; use the actual Unity 6 application binary):

```bash
python3 tools/run-u3-authored-scene-acceptance.py --editor "/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity" --project "/path/to/VirtualCharacterRender/unity" --output "$HOME/VCR-U3-acceptance"
```

The tool launches the **real Unity Editor** in batch mode, invoking
`VCR.Editor.Alpha.U3ToolkitSceneValidation.ValidateExistingAlphaAndP11ScenesAndExit`.
It calls **no** scene generator or SaveScene/SaveAssets. The Editor checks
existing scene integrity, one active shell/UIDocument per scene, persistent
PanelSettings, bootstrap and opaque window output, and missing or retired
MonoScripts. The launcher also requires an explicit source-generated PASS
marker, a zero Editor exit status, and unchanged SHA-256 digests of the
eight authored files. Missing inputs and modified assets fail closed.

Inspect `authored-scene-evidence.json`, `UnityEditor.log`, and
`editor-stdout.log` in the output directory. Never call a green source
or synthetic self-test physical acceptance. No authored bytes are
deliberately saved by this tool; the hash comparison detects accidental
changes but cannot undo external modifications. Keep your backup.
Logs may contain local model paths and should not be uploaded publicly.

## Remaining operator acceptance (separate)

On a real Windows PC and Apple Silicon Mac, manually inspect scene/UI
interaction and native Settings navigation, Korean IME and focus,
VRM model selection/load/unload/restore, tracking devices, output
transparency/click-through recovery, OBS alpha capture and sustained
runtime operation. Compare actual user-authored scenes, not CI-generated
fixtures. Signing/notarization and commercial release remain separate.
PRs #44/#45/#46 remain Draft until evidence is reviewed.
