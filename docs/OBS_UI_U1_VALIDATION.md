# UI Toolkit U1 integration gate

This is a prototype, not a completed replacement for the existing uGUI interface.

## Current code paths

- `WorkspacePanelCatalog.cs`: stable panel identifiers and validation.
- `WorkspaceToolkitShell.cs`: opt-in `UIDocument` shell; status text reads `SingleCharacterSceneRuntime`.
- `ApplicationUiController.cs`: existing production uGUI implementation remains active.

## Manual Unity 6000.3.25f1 validation

1. Open the Unity project; check for compile errors in `VCR.Runtime.UI`.
2. In an isolated test scene or copy, create a `PanelSettings` asset and `UIDocument` GameObject, attach `WorkspaceToolkitShell`.
3. Confirm Live/Advanced toggle and verify each expected panel appears.
4. Confirm status shows unavailable when scene runtime is absent and reflects the actual scene runtime when present.
5. Verify that disabling the prototype leaves character rendering and tracking unaffected.
6. Confirm no UI Toolkit preview is misrepresented as the output/capture transport.
7. Check 100%, 150%, 200% DPI and 720p/1080p window sizes on Windows and Retina scaling on macOS.
8. Do not replace production UI or merge this draft until the real Player build and interaction evidence is available.

## Known missing work

Docking, drag/drop, split resizing, layout persistence, runtime-bound controls, model library, log viewer, native Settings replacement and production scene integration remain unimplemented. Repository validation CI does not run Unity compilation.
