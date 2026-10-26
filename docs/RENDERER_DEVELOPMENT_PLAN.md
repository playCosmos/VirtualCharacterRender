# Renderer development plan: original M2 first, dual pipeline next

Updated: 2026-10-18
Status: Planned. This document is not renderer implementation, hardware acceptance or a release gate PASS.
Owner: VirtualCharacterRenderer / renderer capability

## 1. Priority and integration order

**Do not preempt the existing commercial-readiness sequence.** The immediate work remains M2 on draft PR #33, stacked on M1 PR #27 and its predecessor PR #26. Complete real VRM/OBS/native soak evidence and investigate real failures before promoting new shader effects. This roadmap is recorded now so renderer development can follow naturally once the existing work is accepted.

1. Continue M2: verify exact-head repository/Unity/native Player CI, improve real load/unload and source-presence transition evidence, run real Windows and Apple Silicon VRM/OBS capture, sustained four-hour soak, ≥50 actual model operations and ≥50 independently substantiated physical/protocol tracking disconnect/reconnect cycles. Do not equate source-presence changes or sampled loaded-state with physical device cycles. Keep M2 acceptance #31 and P10 OBS #18 authoritative.
2. Complete inherited commercial M0–M4 criteria and open release blockers; keep actual hardware, OBS, signing and distribution evidence independent of source-free/unit/CI checks.
3. Begin renderer R0 only after the incumbent stage no longer depends on the same build/test infrastructure; future R milestones must not silently alter the validated M2 baseline. Rebaseline performance when renderers change.

## 2. Product goal and boundaries

**End goal:** an independently developed, commercially maintainable toon renderer with a *functional and perceptual quality benchmark* comparable to the licensed full NiloToonURP product, including character-focused shaders, performance, material tooling and documentation. This is not a commitment to reproduce third-party internals or establish feature parity from the tutorial shader alone.

**Intermediate targets, separately validated:**
- **URP:** reproduce the public ColinLeung URP toon tutorial's essential lighting and forward/outline/shadow/depth/depth-normals pass coverage, then an optional face-SDF workflow inspired by NoiRC256's proof of concept, without implying NiloToon full-version parity.
- **Built-in Render Pipeline (BRP):** provide a working custom toon material, skinned-VRM lighting, outlines, shadows, transparency and restoration with usable material presets, informed by lilToon's independently implemented BRP architecture. The current UniVRM BuiltIn generator branch is *not* itself proof of an end-to-end BRP product.

URP 17.3.0 (Unity 6000.3.25f1) is the primary renderer; BRP is a bounded compatibility backend and should not block URP-only Render Graph effects. Existing UniVRM 0.131.2 MToon materials remain the safe default. One active VRM character remains the baseline.

## 3. Baseline inventory and gaps (checked against 2026-10-18 M2)

Implemented:
- `Vrm10CharacterLoader` selects UniVRM URP/BuiltIn material descriptor generators and supports MToon/Lit/Standard/UniUnlit assignment;
- `DesktopRenderBootstrap` controls camera/frame pacing and URP render scale;
- P2 `MaterialOverrideController` preserves original materials, validates shader/property compatibility and rolls back;
- P7 `RuntimeShaderPackageLoader` validates precompiled Windows/macOS shader bundles, textures and material presets without executing arbitrary shader/source/DLL content;
- P0 Unity transparent-window/URP alpha setup, D3D11/BitBlt and UniWinC output adapter.

Needs independent proof or implementation:
- a versioned, committed active URP Pipeline Asset and explicit pipeline selection/build contract; project tree does not currently track `GraphicsSettings.asset`, `QualitySettings.asset` or a URP asset;
- separate URP/BRP builds and actual shader compilation/rendering on D3D11 and Metal;
- platform/pipeline-aware shader-package compatibility and variant-stripping/reload behavior;
- VRM0/VRM1 realistic MToon, outline, hair, eye, semi-transparent and alpha-to-OBS image comparisons;
- a BRP equivalent for controls currently only implemented by `UniversalRenderPipelineAsset`;
- proper native GPU frame/memory accounting and long-running, device-connected performance evidence.

## 4. Dependency architecture

```text
Scene / VRM / Appearance / Material preset / MaterialOverrideController
                             |
                   Toon Material Semantics
         profiles, feature flags, property IDs, asset references
                             |
               Reusable Shading Math (HLSL)
  shading bands, directional face SDF, rim, specular, matcap
                  /                       \
           URP ShaderLab                 BRP ShaderLab
     URP17 Forward / shadows       forward base/add / shadows
     depth / depth-normals         depth & alpha equivalents
     Render Graph extensions      non-Render-Graph alternatives
                  \                       /
             Shader registry / package verifier
                             |
             Existing VCR overlay and OBS output
```

Share shader *algorithms*, data meaning, authoring and error diagnostics. Separate Unity include files, lighting contracts, shadow access, pass layout and post-processing by pipeline. Do not force BRP to emulate every URP Render Graph facility. Prefer cohesive capability-owned files over excessive micro-abstractions.

Render profiles must preserve imported VRM MToon unless the user explicitly opts into a custom shader. Failed/incompatible application restores the same original material, not a visually approximate copy. Shader bundles remain precompiled by target and pipeline, no runtime HLSL compilation or executable plugin loading.

## 5. Milestones and objective gates

| Milestone | Scope | Exit criteria |
| --- | --- | --- |
| M2 (existing, priority 1) | Real VRM/OBS capture/performance diagnostics and endurance | #31/#18 independently signed off from real native device evidence; source-free smoke alone insufficient |
| M3–M4 (existing) | Complete original commercial-readiness/release blockers | Existing issues and release checklist closed with actual evidence |
| R0 (foundation) | Commit pipeline assets/config, implement URP/BRP build selection, record active pipeline + GPU API | Separate Windows/macOS Player builds and rendered reference VRM in each supported backend; CI rejects pipeline mismatch |
| R1 (intermediate) | URP toon forward/outline/shadow/depth/normals, BRP toon core and reliable material conversion; optional SDF face | Distinct VRM0 and VRM1 renders, shadow/outline alpha edges, restore/failover pass; no hidden rendering regression |
| R2 (shared core) | Shared profile/material semantics, limited shared HLSL, pipeline-specific pass adapters | Same profiles yield intentionally defined comparable color/shadow/outline output; precise property mapping |
| R3 (character quality) | Face normal fixes, face SDF, smooth outline normals, hair self-shadow, eye/eyebrow depth, stable rim, GGX and MatCap | Golden scenes/models with scripted camera/light sweeps, close-up face/outline failure checks |
| R4 (effects) | Dissolve/dither/fade, hair/eye enhancements, dedicated bloom/post controls, perspective corrections | Independently toggleable features; URP17 Render Graph path tested and BRP fallback documented |
| R5 (authoring) | Editor/runtime UI, presets, auto assignment, diagnostics, feature tiers, preview, rollback | Users can configure supported model materials without writing shaders; deterministic preset migration |
| R6 (commercial equivalence) | Perf/variant/stripping/memory, native OS matrix, fidelity, docs, distribution | Feature matrix + independent side-by-side visual review + 4h soak + 720p60/1080p60 measurements under declared machines |

Each R stage has its own PR, source-free tests, native render tests, changelog and committed settings. New GPU costs must be measured against the accepted M2 baseline; disabled optional effects must not add hidden per-frame work. Do not assign unsupported percentage-complete scores.

## 6. External references and license policy

Reference source snapshots reviewed on 2026-10-18:
- ColinLeung-NiloCat/UnityURPToonLitShaderExample @ `fbe276e0` — MIT tutorial, *not* full licensed NiloToon; inspect its five-pass sample and variant strategy.
- NoiRC256/URPSimpleGenshinShaders @ `a2198721` — MIT optional face-SDF proof of concept, tested by author on Unity 2022.2 / URP 14; incomplete depth-normals and face-direction controls.
- lilxyzw/lilToon @ `4a3f8683`, package 2.3.4 — MIT multi-pipeline/editor tooling reference, evaluate dependency and variant costs before any product integration.
- Reddit r/Unity3D comments/156u8fm is a discovery index, **not** a code provenance or compatibility assertion.
- Do not transplant GPL-3.0 shader code (e.g. the archived StarRailNPRShader) into a proprietary artifact without an explicit license-policy decision.
- NiloToon *paid* features in its public README are benchmarks, not authority to copy nonpublic implementation. Record provenance and retain licenses of any permissively reused code; verify sample assets' independent reuse terms.

NiloToon's published feature examples include face-normal correction, smoothed outline normal baking, 2D hair shadow, screen-stable rim, eye/eyebrow depth and stencil strategies, GGX, MatCap, runtime effects, character-volume post effects, perspective handling, shader-stripping and creator tooling. Treat this as a feature *candidate* list, not a verified full-version source inventory.

## 7. Verification and promotion policy

Automated: pinned Unity 6000.3.25f1, URP 17.3, deterministic pipeline assets, Windows/macOS shader bundle creation, compile failures, correct pass tags and source shader fallback; validate exact commit and preserve CI artifacts.

Operator/native: 2 distinct VRM0 + 2 VRM1 models, animation/blinks/expressions, varied facial shapes, stencil/transparent hair edges, moving light/camera, D3D11 + Metal + BRP where supported, screenshot metrics, OBS alpha at 720p/1080p, cold shader variant creation/hitches, GC/CPU/GPU/memory/4h soak. Record device/system and source SHA. Screenshots or code inspection are never proxies for native frame and compositor acceptance.

Do not merge renderer roadmap code into an in-flight commercial acceptance branch merely to start R0. Its presence is a recorded plan; implementation begins after the existing M2 priority work and release gates are handled.
