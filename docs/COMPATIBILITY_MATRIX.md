# Compatibility Matrix

## P0 pinned baseline

| Component | Version / target | Status | Notes |
|---|---|---|---|
| Unity | 6000.3.25f1 | Pinned for P0 | Unity 6.3 LTS |
| URP | 17.3.x | Pinned by editor line | Core graphics package for Unity 6000.3 |
| UniVRM | 0.131.2 | Pinned for P0 | VRM 0.x + VRM 1.0 packages |
| MediaPipeUnityPlugin | 0.16.3 | Pinned for P0 | Prebuilt desktop native package |
| MediaPipe runtime | 0.10.22 | Transitive | Bundled by MediaPipeUnityPlugin 0.16.3 |
| UniWindowController | 0.9.8 / `v0.9.8` | Pinned P0 output candidate | MIT; Windows/macOS native transparent-window adapter; Git tag pinned |
| macOS | Apple Silicon M1+ | Supported baseline | M1 is minimum validation reference |
| Windows | x86-64 | Platform target | Hardware floor TBD |

## Required validation combinations

| Test | macOS M1+ | Windows x86-64 |
|---|---:|---:|
| Unity editor opens | Required | Required |
| URP render | Required | Required |
| VRM 0.x load | Required | Required |
| VRM 1.0 load | Required | Required |
| MToon | Required | Required |
| MediaPipe native load | Required | Required |
| FaceLandmarker LIVE_STREAM | Required | Required |
| Holistic LIVE_STREAM (hands/upper body) | Required | Required |
| Transparent output | Required: Metal + UniWinC standalone | Required: D3D11 + BitBlt + UniWinC standalone |
| Alpha edge/overlap pattern | Required | Required |
| OBS-compatible capture | Required: macOS Screen Capture/window | Required: Game Capture + Allow Transparency |
| 720p60 baseline | Required | Required |
| 1080p60 measurement | Required | Required |

## Known constraints

- MediaPipeUnityPlugin desktop inference is CPU-based on macOS and Windows.
- The MediaPipe prebuilt package is intentionally not committed to this repository because of its size; bootstrap scripts fetch the pinned release tarball.
- ADR-0025 uses FaceLandmarker for face/head and HolisticLandmarker for hands/upper body because the 0.16.3 Holistic C# wrapper gates complete results on pose availability.
- Separate Hand/Pose tasks remain a later optimization only if profiling or hand-quality evidence justifies the added complexity.
- Windows hardware requirements remain intentionally undefined until P0 measurements exist.
- Windows transparent-window P0 is explicitly D3D11-only with the D3D11 flip-model swapchain disabled. This is an output-path constraint, not a claim that D3D12 is generally unsuitable for the renderer.
- URP Alpha Processing must be enabled; the P0 transparent path uses SDR RGBA8 with camera HDR disabled.
- UniWindowController automatic opacity hit testing is disabled in the lightweight baseline.

## Upgrade policy

Do not casually float package versions during P0.

A patch/version change must record:

1. reason for change
2. previous failing or blocking evidence
3. new compatibility result
4. performance delta
