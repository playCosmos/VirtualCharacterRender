# Windows webcam physical validation

This procedure runs the source-bound, nondevelopment U1 P1 Windows IL2CPP Player on the Windows PC with the webcam. The scripts collect inventory, binary, Player.log, webcam start/result/cleanup and operator-entered evidence. They never set `physicalHumanAcceptance` to true; a reviewer must assess the run separately.

## Tested Player

- Repository: `playCosmos/VirtualCharacterRender`
- Source SHA: `417936b2a004c45c72c4138523e50660f971c9ae`
- Successful Windows IL2CPP Actions run: [45939202405](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/45939202405)
- Artifact name: `u1-p1-windows-il2cpp-45939202405-1`
- Expected artifact files: `windows.zip`, `windows.sha256`, `windows.source-sha`, `windows.payload-sha256.json`

The artifact is retained by GitHub Actions for a limited time. If it expires, select a later successful U1 P1 Windows IL2CPP run and use that run's exact SHA, run ID and artifact attempt number. Do not reuse the SHA from this page for a different artifact.

## Requirements on the webcam PC

- Windows 10/11 x64, access to the internet, and permission to sign in to GitHub CLI to download the private Actions artifact.
- GitHub CLI (`gh`) authenticated for `playCosmos/VirtualCharacterRender`.
- Python 3 (`py -3` or `python.exe`) for archive verification and extraction.
- Windows PowerShell 5.1 and the repository scripts.
- The test webcam visible in Device Manager and allowed under Windows **Settings → Privacy & security → Camera** for desktop apps.
- A test participant in view for face/body tracking. A replacement VRM can be supplied if model swap is included in this run.

Keep the camera connected directly to the test PC. Close other applications that might hold it. Use one camera at a time, or pass the exact Windows PnP friendly name when several are connected.

## 1. Get the scripts

On the webcam PC, clone the pushed PR branch (or update an existing checkout to it):

```powershell
git clone --branch feature/obs-ui-u1-layout-contract-20261026 --single-branch `
  https://github.com/playCosmos/VirtualCharacterRender.git VCR-U1
Set-Location .\VCR-U1
```

Confirm the checkout contains these files:

- `tools/get-p1-windows-il2cpp-artifact.ps1`
- `tools/run-p1-windows-webcam-validation.ps1`
- `tools/run-p1-player.ps1`
- `tools/prepare-p1-physical-player.py`
- `tools/verify-p1-il2cpp-archive.py`
- `tools/verify-p1-player-payload.ps1`
- `tools/inspect-p1-physical-evidence.ps1`
- `tools/inspect-p1-windows-webcam-evidence.ps1`

The scripts are intended to be copied/run from one checkout so their neighboring verifier paths remain intact.

## 2. Download and verify the exact Actions artifact

Open PowerShell in the checkout directory and authenticate if needed:

```powershell
gh auth login
```

Download the artifact. By default, it is saved in a new, SHA-named folder under `%LOCALAPPDATA%\VirtualCharacterRender\P1Artifacts`:

```powershell
.\tools\get-p1-windows-il2cpp-artifact.ps1 `
  -RunId 45939202405 `
  -ExpectedSourceSha 417936b2a004c45c72c4138523e50660f971c9ae
```

The helper checks the run workflow/conclusion/head SHA, selects the matching run-attempt artifact, and verifies the ZIP checksum and manifest source SHA. It does not claim publisher authenticity. Do not overwrite the verified ZIP or sidecars.

## 3. Run the webcam validation harness

First open **Device Manager → Cameras** and note the webcam's friendly name. Run the harness, using the exact substring if more than one camera appears:

```powershell
.\tools\run-p1-windows-webcam-validation.ps1 `
  -ArtifactDirectory "$env:LOCALAPPDATA\VirtualCharacterRender\P1Artifacts\417936b2a004c45c72c4138523e50660f971c9ae\45939202405" `
  -ExpectedSourceSha 417936b2a004c45c72c4138523e50660f971c9ae `
  -ExpectedCameraName "Logitech BRIO"
```

`ExpectedCameraName` filters the Windows PnP inventory. If Unity displays the device under a different `WebCamTexture` label, add `-ExpectedUnityCameraName "<Unity camera label>"`; the Player.log comparison then checks that the runtime chose that device. If only one camera is present, omit `-ExpectedCameraName`; if several are present, the script asks which PnP device to track.

Optional VRM test inputs (supply both paths for a model-swap check):

```powershell
.\tools\run-p1-windows-webcam-validation.ps1 `
  -ArtifactDirectory "$env:LOCALAPPDATA\VirtualCharacterRender\P1Artifacts\417936b2a004c45c72c4138523e50660f971c9ae\45939202405" `
  -ExpectedSourceSha 417936b2a004c45c72c4138523e50660f971c9ae `
  -InitialVrm "D:\TestAssets\avatar-a.vrm" `
  -ReplacementVrm "D:\TestAssets\avatar-b.vrm"
```

The initial VRM is passed to the Player at launch. Load the replacement through U1's Sources panel while the Player is running. Use redistributable test files; these paths are not copied into the evidence receipt.

For the four-hour continuous-run gate, add `-SoakHours 4`. The harness records start/end times and treats an early Player exit as a failed run. For the initial camera lifecycle check, leave this at its default `0` so a normal manual close is allowed.

Evidence defaults to a new timestamped directory under `%LOCALAPPDATA%\VirtualCharacterRender\P1-Webcam-Evidence`. Pass `-EvidenceDirectory` to choose a different new path. Existing evidence folders are never overwritten.

## 4. Operator test sequence

The harness prints this sequence before launch:

1. In U1 **Tracking**, add or open **MediaPipe Webcam**, choose the intended camera, and enable the source. Approve the Windows camera permission prompt if shown.
2. Confirm the intended camera is active. Keep a face/body in view and move it; confirm tracking responds rather than relying only on “Healthy” status.
3. Turn the source Off. Confirm U1 shows **Off/Stopped** and the camera privacy-use indicator goes dark or the device is otherwise released. Avoid opening another camera app to test release; that app could take the camera itself.
4. Turn the source On again. Confirm frames/tracking recover without restarting the Player. Use U1 **Recover** if needed and note the result.
5. For an external USB webcam, leave the Player open, unplug the camera for at least 10 seconds, reconnect it, then use U1 **Recover** if needed. The harness polls Windows PnP presence changes. For a fixed internal camera, mark this item not applicable.
6. If VRM inputs were provided, load the initial model, replace it with the second model through the UI, and verify tracking/output follows the active model.
7. Close the Player normally. Return to PowerShell and answer each checklist prompt with `pass`, `fail`, or `not-run`; only the USB reconnect item also accepts `not-applicable`. A failed or unperformed check must never be marked pass.

The harness then checks the Player's source-bound launch receipt and log hash, runtime startup/quit markers, MediaPipe camera start/resolution, positive face/holistic result counters, and cleanup completion. These machine checks support review; they cannot prove that the webcam physically stopped, that tracking looked correct, or that the operator saw a good result.

If screenshots are useful, save UI-only images to the printed evidence folder's `Screenshots` subfolder before closing the Player. The harness writes their file names, sizes, and SHA-256 values to `screenshot-index.json`. Do not capture or distribute a webcam preview containing a person's face unless that person has agreed.

## Evidence files

The output folder contains:

- `camera-inventory-before.json`, `camera-inventory-after.json`: Windows PnP/CIM snapshots; PnP instance IDs are hashed.
- `camera-presence-events.json`: detected device disappearance/return while the Player was open.
- Source SHA/checksum/manifest and `download-receipt.json`: the downloaded artifact sidecars and run metadata. The large `windows.zip` remains in the separate artifact directory named in `webcam-session.json` to avoid copying it into the evidence bundle.
- `PlayerRun/Player.log` and `PlayerRun/launch-evidence.json`: runtime output and source-bound launch receipt.
- `webcam-session.json`: selected device, run outcome and operator responses.
- `webcam-inspection.json`: machine evidence and missing/failed checklist fields.
- `Screenshots/` and `screenshot-index.json`: optional UI-only screenshots and their hashes.

To re-inspect a folder later without launching the Player:

```powershell
.\tools\inspect-p1-windows-webcam-evidence.ps1 `
  -EvidenceDirectory "$env:LOCALAPPDATA\VirtualCharacterRender\P1-Webcam-Evidence\<timestamp>" `
  -ExpectedSourceSha 417936b2a004c45c72c4138523e50660f971c9ae
```

Use `-ReportOnly` to emit an inspection report while preserving an incomplete/failing status without throwing. Do not edit original evidence files after collection; create a redacted copy separately if logs contain usernames or other local paths.

## Acceptance limits

The harness records but does not decide acceptance. Review webcam permission and device identity, MediaPipe result counts, Off/On behavior, disconnect/reconnect observations, VRM swaps, Player faults, and image/output quality together. A successful script exit is not proof of privacy-safe camera shutdown, product correctness, four-hour stability, OBS transparency, Korean IME handling, or human approval.

Keep PR #44 Draft and legacy uGUI available until the independent Windows/macOS physical acceptance items in [`u1-ui-parity-audit.md`](u1-ui-parity-audit.md) are completed.
