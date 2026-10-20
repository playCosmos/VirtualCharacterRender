# ADR-0030: Backend-neutral 2D presentation extension boundary

- Status: Accepted
- Date: 2026-10-04

## Context

ADR-0019 established that VirtualCharacterRender remains a 3D-first product and that 2D support should arrive as a separate extension after the shared tracking/event/output contracts are stable.

P13 now needs a concrete boundary for evaluating 2D runtimes without coupling the existing VRM/Unity character pipeline to one vendor or one 2D rig format.

The product requirements that matter for a 2D backend are:

- exactly one active performer/avatar remains the supported product path
- Windows and macOS are first-class desktop targets
- macOS baseline remains Apple Silicon/M1+
- existing MediaPipe/ARKit/VMC routing must remain reusable
- existing normalized face/expression state must remain the source of truth
- overlay/output/event systems must not depend on a concrete 2D SDK
- unused 2D support must not impose meaningful recurring runtime cost
- SDK/licensing failure must not make the 3D runtime fail to compile or start

## Current backend evaluation

### Inochi2D

Current official/public evidence checked on 2026-10-04:

- Inochi2D is open source.
- The official Unity package repository is:
  - https://github.com/Inochi2D/com.inochi2d.inochi2d-unity
- its package manifest currently reports version `0.8.7`
- its package manifest reports `BSD-2-Clause`
- its README explicitly describes the Unity plugin as beta
- the public `Native` directory currently exposes `windows-x86_64`

This is a promising open backend, but the currently published Unity package is not sufficient evidence for the project's Windows + macOS first-class requirement. The absence of a public macOS native artifact in this package is treated as a present integration blocker, not as a claim that Inochi2D itself can never support macOS.

### Live2D Cubism

Current official evidence checked on 2026-10-04:

- Cubism SDK for Unity officially lists Windows and macOS support:
  - https://docs.live2d.com/en/cubism-sdk-manual/platform/
- official FAQ also lists Windows/macOS Unity support:
  - https://docs.live2d.com/en/cubism-sdk-manual/faq/
- Cubism development can begin under Live2D's SDK license terms, but publishing/distribution has a separate Publication / SDK Release License boundary and usage-specific conditions:
  - https://www.live2d.com/en/sdk/license/
  - https://www.live2d.com/en/sdk/about/

Live2D is therefore the stronger current cross-platform prototype candidate, but its proprietary Core/licensing boundary prevents it from becoming an unconditional dependency of the open/core runtime.

## Decision

P13 introduces a backend-neutral 2D presentation boundary.

Core/runtime code must not reference Inochi2D, Live2D Cubism, or another 2D SDK type.

The shared contract is:

```text
Normalized Tracking / Expression
             ↓
   Character2DRuntime
             ↓
  ICharacter2DBackend
      ├─ optional Live2D adapter
      ├─ optional Inochi2D adapter
      └─ future backend adapter
             ↓
      existing Output layer
```

`ICharacter2DBackend` owns only backend presentation responsibilities:

- backend identity
- supported normalized input domains
- model load/unload
- latest backend status/error
- application of one source-neutral input snapshot

It does not own:

- webcam/ARKit/VMC acquisition
- tracking routing
- event dispatch
- overlay/window composition
- broadcast platform integration
- multi-character orchestration

### Input policy

2D adapters consume the existing immutable `TrackingFrame` domains.

No second face-expression schema is introduced.

A backend declares a `Character2DInputDomain` mask. The host polls only the intersection of:

- inputs requested by the application
- inputs supported by the active backend

The first baseline is Face + Expressions. Body/Hands or HumanoidPose are optional backend capabilities rather than baseline requirements.

The host uses immutable frame-object identity to avoid re-applying an unchanged snapshot. Sequence value alone is not used for deduplication because a source/router replacement can legitimately produce a new immutable object with the same sequence value.

### Lifecycle policy

A 2D backend is optional.

The P13 host:

- does not require a backend SDK assembly
- remains idle when no model/backend/provider is active
- resets frame caches on model unload/reload
- keeps backend failures contained behind the adapter
- reports load/apply counts and failures through runtime diagnostics

Backend SDK packages belong in backend-specific optional assemblies/packages.

### Backend selection policy

No canonical production 2D backend is accepted yet.

Prototype priority is:

1. Live2D adapter, only after repository/package/licensing acceptance for the intended distribution model, because its current Unity platform support matches Windows + macOS.
2. Inochi2D adapter as the preferred open alternative when the Unity/native macOS path is available or the project establishes and validates its own acceptable macOS native build/package path.

This ordering is an integration/evidence decision, not a quality claim about the model formats themselves.

## Consequences

Positive:

- 3D/VRM runtime remains independent of 2D SDK installation
- existing tracking, event, diagnostics, and output systems remain reusable
- backend experimentation does not fork upstream tracking logic
- licensing differences remain isolated at adapter/package boundaries
- Inochi2D and Live2D can be evaluated against the same host contract
- a future backend can be added without changing input protocols

Costs:

- each backend needs its own parameter/rig mapping
- backend model load/status/error semantics must be translated into the common contract
- visual feature parity is not assumed
- backend-specific editor/import tooling remains separate

## Rejected alternatives

### Make Live2D the core character runtime

Rejected because it would add a proprietary SDK/Core and distribution-license boundary to code paths that do not need 2D.

### Make Inochi2D the only backend immediately

Rejected for now because the current official/public Unity package does not yet provide evidence for the required macOS first-class path.

### Force 2D and VRM to share renderer primitives

Rejected. They share upstream normalized state and downstream output abstractions, not mesh/material/rig implementation.

### Create a second tracking/expression pipeline for 2D

Rejected. It would duplicate MediaPipe/ARKit/VMC mapping and create divergent expression semantics.

## Validation required before production backend acceptance

For each accepted backend:

- Windows standalone model load/render
- macOS Apple Silicon standalone model load/render
- face/head/eye/mouth response from existing normalized tracking
- expression response from existing normalized expressions
- tracking loss/restore behavior
- transparent overlay/OBS capture
- 720p60 minimum and 1080p60 recommended output evidence where applicable
- idle/disabled recurring cost
- model unload/reload without stale tracking state
- packaging/signing/notarization impact
- license/distribution review

## Revisit conditions

Revisit the default backend ordering when:

- the official Inochi2D Unity package publishes and validates a macOS/Apple Silicon native path
- the project validates an acceptable maintained macOS Inochi native build path
- Live2D licensing/package constraints change
- another backend demonstrates stronger cross-platform/runtime evidence
- real 2D authoring requirements expose a missing domain in the backend-neutral contract
