# ADR-0024: P0 Unity package baseline

- Status: Accepted
- Date: 2026-10-01

## Context

Implementation cannot begin reproducibly while Unity, URP, UniVRM, and MediaPipe integration versions remain floating.

The project needs a stable P0 combination that supports Windows, macOS Apple Silicon, VRM 0.x/1.0, URP, and the accepted MediaPipe tracking path. The current execution decision is ADR-0025 (FaceLandmarker + HolisticLandmarker).

## Decision

Pin the initial P0 baseline to:

- Unity 6000.3.25f1 (Unity 6.3 LTS)
- URP 17.3.x as the Unity 6000.3 editor-matched core package
- UniVRM 0.131.2
- MediaPipeUnityPlugin 0.16.3
- MediaPipe 0.10.22 as bundled by MediaPipeUnityPlugin

Use Apple Silicon M1 as the minimum macOS validation hardware.

Windows minimum/recommended hardware remains TBD until P0 measurements.

## Dependency installation

UniVRM is installed through pinned UPM Git dependencies.

MediaPipeUnityPlugin uses the official prebuilt 0.16.3 tarball rather than cloning source, because the source repository does not contain all required prebuilt native libraries/models.

The tarball is downloaded locally by project bootstrap tooling and is not committed to Git.

## Consequences

- all developers test the same engine/package combination
- package drift cannot silently alter performance results
- large native MediaPipe binaries stay out of the repository
- initial desktop MediaPipe inference is CPU-bound on Windows/macOS
- Unity upgrades are deliberate compatibility work rather than routine package updates

## Revisit conditions

Supersede this ADR if:

- a pinned component has a blocking defect
- MediaPipe 0.16.3 cannot meet stability/performance requirements
- UniVRM 0.131.2 fails a required VRM path
- a later Unity 6.3 LTS patch is required to fix a blocking issue
- a security or platform-support requirement forces an upgrade
