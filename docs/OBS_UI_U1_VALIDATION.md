# UI Toolkit U1 integration gate

U1 is implemented in source but **not accepted as a production UI** until Unity compilation, player builds and interactive desktop checks pass. GitHub repository CI success alone does not establish Unity compatibility.

## Integration and validation

- `WorkspaceToolkitShell.cs` supplies the UI Toolkit workspace, runtime-bound character actions, inspector controls, docking state and preview attempts.
- `P1ApplicationSceneBuilder.cs` installs the shell and a persisted `PanelSettings` asset into newly generated P1 scenes; it can also upgrade existing scenes without regenerating their other objects.
- `P1PlayerBuildMenu.cs` requires the workspace before packaging Windows/macOS players.
- `ApplicationUiController.cs` remains the existing uGUI controller. Native Settings still depends on this controller. If it is absent, the shell now provides an in-workspace Render settings fallback (FPS 30–120 and VSync); a complete standalone native Settings window is **not complete**.
- The generated `Assets/VCR/P1/P1Runtime.unity` scene is not committed in this branch. Generate it locally before attempting the P1 player build.

## Unity 6000.3.25f1 batch checks

After opening the Unity project and resolving all compilation errors, create the scene using **VCR > P1 > Create Application Runtime Scene**. For an existing scene, use **VCR > P1 > Upgrade Existing Runtime Scene With Workspace** instead of regenerating it.

For a clean checkout where the generated scene does not exist, the non-overwriting batch generator can create and validate it:

```text
Unity -batchmode -nographics -quit -projectPath <unity-project-path> -executeMethod VCR.Editor.P1.P1BatchValidation.GenerateWorkspaceAndExit -logFile <log-path>
```

The generator exits with code 1 if the P1 scene already exists. It creates scene and project assets, so review the resulting working-tree changes before committing.

Run this command from the host with Unity installed (replace paths as appropriate):

```text
Unity -batchmode -nographics -quit -projectPath <unity-project-path> -executeMethod VCR.Editor.P1.P1BatchValidation.RunWorkspaceAndExit -logFile <log-path>
```

The workspace check returns exit code 0 on success, 1 on failure. It may upgrade an older P1 scene and save the result; run on a clean working tree or review the changes afterwards. It does **not** test rendering, native window behavior, mouse input, or GPU compatibility. `RunSourceFreeAndExit` is a separate source-free P0/P1 check that does not require a generated P1 scene.

## Manual acceptance checklist

1. Verify no Unity C# compilation errors and build Windows x64 and macOS players on their supported build hosts.
2. Launch the P1 player; verify the UI Toolkit shell is visible and no duplicate UI obscures it.
3. Test VRM load, reload, unload, last-model restore and unsupported FBX behavior without claiming an importer exists.
4. Test live preview in the actual rendering pipeline, camera discovery and preview cleanup after model/scene changes.
5. Test docking, tab/stack, resize, hide/show, layout persistence, corrupt-layout recovery and keyboard/focus handling.
6. Test native Settings availability from the shell. When `ApplicationUiController` is absent, or native Settings are unsupported, verify the fallback panel opens, applies FPS/VSync, reads back the effective values, handles missing runtime and closes. Settings must remain clickable on unsupported native-window platforms.
7. Verify startup logs, module/plugin/shader/model discovery and the configured 60 FPS default / 120 FPS maximum.
8. Test 720p/1080p and 100%/150%/200% DPI on Windows, Retina scaling on macOS, and multi-monitor moves.
9. Verify no regressions in the existing uGUI application, tracking, transparent output, shutdown and startup.

Do not merge PR #44 or publish a U1 release until the above evidence is collected.