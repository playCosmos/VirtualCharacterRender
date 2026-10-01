# Architecture Decision Records

ADR files record consequential technical decisions and their reasoning.

## Status

- Proposed — under evaluation.
- Accepted — active decision.
- Rejected — evaluated and not selected.
- Superseded — replaced by a later ADR.
- Deferred — intentionally postponed.

## Rules

1. Do not silently rewrite an Accepted ADR to change its decision.
2. Clarifications may be added without changing the original outcome.
3. A materially different decision requires a new ADR that supersedes the old one.
4. Proposed ADRs may evolve while evidence is collected.
5. Each ADR lists consequences and revisit conditions.
6. Implementation docs link to ADRs instead of duplicating rationale.

## Index

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-layered-runtime-boundaries.md) | Layered runtime boundaries | Accepted |
| [0002](0002-unity-urp-primary-renderer.md) | Unity URP as primary renderer | Accepted |
| [0003](0003-univrm-for-vrm-runtime.md) | UniVRM for VRM runtime | Proposed |
| [0004](0004-custom-shaders-first-class.md) | Custom shaders as first-class feature | Accepted |
| [0005](0005-normalized-tracking-state.md) | Normalized tracking state | Accepted |
| [0006](0006-vmc-first-class-interoperability.md) | VMC as first-class interoperability protocol | Proposed |
| [0007](0007-transparent-window-primary-output.md) | Transparent window as initial broadcast output | Proposed |
| [0008](0008-windows-macos-first-class-platforms.md) | Windows and macOS as first-class targets | Accepted |
| [0009](0009-single-scalable-capability-pipeline.md) | One scalable one-character capability pipeline | Accepted |
| [0010](0010-built-in-basic-motion-capture.md) | Built-in basic motion capture | Accepted |
| [0011](0011-environment-first-class-runtime.md) | Environment as first-class runtime subsystem | Accepted |
| [0012](0012-capability-lazy-initialization.md) | Capability-based lazy initialization | Accepted |
| [0013](0013-runtime-profile-vs-graphics-quality.md) | Runtime capabilities and graphics quality are separate | Accepted |
| [0014](0014-performance-budget-and-diagnostics.md) | Performance budget and diagnostics are architectural requirements | Accepted |
| [0015](0015-plugin-execution-security-boundary.md) | Plugin execution/security boundary | Proposed |
| [0016](0016-character-scene-environment-asset-separation.md) | Character, scene, and environment asset separation | Accepted |
| [0017](0017-physics-domain-separation.md) | Character-secondary and world physics separation | Proposed |
| [0018](0018-single-character-product-scope.md) | Single active character product scope | Accepted |
| [0019](0019-3d-first-2d-extension.md) | 3D-first product; 2D as separate extension | Accepted |
| [0020](0020-external-broadcast-event-adapters.md) | External broadcast events through normalized adapters | Accepted |
| [0021](0021-tracking-derived-subject-presence.md) | Tracking-derived subject presence | Accepted |
| [0022](0022-mediapipe-arkit-tracking-stack.md) | MediaPipe webcam tracking with ARKit face priority | Accepted |
| [0023](0023-mediapipe-holistic-live-stream-execution.md) | MediaPipe Holistic live-stream execution in Unity | Accepted |
