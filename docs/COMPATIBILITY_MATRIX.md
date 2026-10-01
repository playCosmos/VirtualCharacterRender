# Compatibility Matrix

## P0 pinned baseline

| Component | Version / target | Status | Notes |
|---|---|---|---|
| Unity | 6000.3.25f1 | Pinned for P0 | Unity 6.3 LTS |
| URP | 17.3.x | Pinned by editor line | Core graphics package for Unity 6000.3 |
| UniVRM | 0.131.2 | Pinned for P0 | VRM 0.x + VRM 1.0 packages |
| MediaPipeUnityPlugin | 0.16.3 | Pinned for P0 | Prebuilt desktop native package |
| MediaPipe runtime | 0.10.22 | Transitive | Bundled by MediaPipeUnityPlugin 0.16.3 |
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
| Holistic LIVE_STREAM | Required | Required |
| Transparent output | Required | Required |
| OBS-compatible capture | Required | Required |
| 720p60 baseline | Required | Required |
| 1080p60 measurement | Required | Required |

## Known constraints

- MediaPipeUnityPlugin desktop inference is CPU-based on macOS and Windows.
- The MediaPipe prebuilt package is intentionally not committed to this repository because of its size; bootstrap scripts fetch the pinned release tarball.
- HolisticLandmarker is used first for implementation simplicity. Separate Face/Hand/Pose tasks are an optimization fallback only if profiling proves necessary.
- Windows hardware requirements remain intentionally undefined until P0 measurements exist.

## Upgrade policy

Do not casually float package versions during P0.

A patch/version change must record:

1. reason for change
2. previous failing or blocking evidence
3. new compatibility result
4. performance delta
