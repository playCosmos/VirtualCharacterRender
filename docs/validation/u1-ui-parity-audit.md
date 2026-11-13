# U1 UI parity audit

Baseline: PR #44 head 34a8ec345170be691f86e795f3e0e567bdfb50ac (2026-11-13).
Compared ApplicationUiController uGUI with WorkspaceToolkitShell and DesktopNativeSettingsWindow.
Scope is code paths and the runtime service each control calls. Physical GUI, IME, real VRM files, tracking devices, and OBS capture are not closed here.

## Confirmed defects fixed in the working tree

Layout snapshots stored docks and orders by catalog index. A reordered catalog restored those values onto different panels, and a shorter array rejected the snapshot. WorkspaceLayoutFile now uses schema version 2 and stores panel IDs. Version 1 stays positional and is rejected when longer than the current catalog. Unknown version-2 IDs are ignored. Truncated JSON has version 0 and is rejected.

LoadCharacterAsync and ReloadCharacterAsync wrote UI labels and PlayerPrefs after await without checking that the shell still existed. The continuation now returns before those writes when the shell is destroyed. The finally blocks update pending state only while the shell exists. C# does not allow return inside finally, so the guard is a condition.

The regression check reads WorkspaceToolkitShell.cs and requires the destroyed-shell guard on the line after each of those awaits. A missing guard fails P1WorkspaceLayoutValidation.

## Local verification

Editor 6000.3.25f1, method VCR.Editor.P1.P1BatchValidation.RunSourceFreeAndExit.
First run, process 38636, log .tmp/u1-p1-sourcefree.log: layout PASS and renderer-core PASS. This was before the async guard.
Second run, process 42436, log .tmp/u1-p1-sourcefree-3.log: VCR P1 workspace layout persistence validation PASS, including the new guard check, and VCR P1 renderer core validation PASS. The renderer-core pass includes configuration persistence and versioning. The batch also requires P0SourceFreeValidationSuite.RunAllChecks.
An intermediate run failed to compile because finally contained return (CS0157). That was corrected before the passing run.
GitHub Actions was not run. Nothing was committed. PR #44 remains Draft at 34a8ec34.

## Not closed

Tracking stop and reconnect, output status during capture, and closing Settings during reload have call paths but no new lifecycle regression.
Win32, AppKit, and fallback Settings item differences are not compared control by control.
Physical acceptance remains blocked: Windows and macOS GUI, Korean IME, a real VRM load and swap, a real tracker, OBS capture, 720p60, 1080p60, and a 4-hour run.
Legacy uGUI was not removed. No PR comment was posted.
Exception paths: the 2026-11-13 follow-up run, process 40388, log .tmp/u1-p1-sourcefree-4.log, also passed layout and renderer-core validation. Load and reload now return before UI writes on success, cancellation, and failure when the shell is gone. The check requires three destroyed-shell returns in each method. Configuration reload is synchronous, so it does not resume after the window closes.
Native action 204 differs and is not changed. Legacy ApplicationUiController.NativeSettings maps 204 to ShowProfileLoadUnavailable, which calls ReloadSavedConfiguration. U1 WorkspaceToolkitShell maps 204 to restore render defaults: scale 1, 60 FPS, VSync off. U1 reload of the saved configuration is action 206, and save is 205. Actions 200-203 and 300-303 and 401-403 match. Choosing which 204 behavior is canonical needs an explicit decision because both are user-visible and changing either one breaks the other platform path.