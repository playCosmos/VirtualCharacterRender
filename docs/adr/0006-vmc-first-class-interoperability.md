# ADR-0006: VMC as first-class motion interoperability protocol

- Status: Proposed
- Date: 2026-10-01

## Context

The project should interoperate with existing virtual-character tracking and motion tools instead of requiring all sources to be implemented natively.

VMC is OSC/UDP-based, widely used by VRM motion applications, and explicitly supports partial implementation. The internal runtime must not become VMC-shaped.

## Proposed decision

Support VMC input and output as an early interoperability protocol while keeping VMC outside the internal normalized-state model.

Current P0 implementation now has:

- bounded OSC message/bundle codec for the types required by VMC P0
- UDP VMC receiver with sender filtering and stale-source handling
- source-neutral `HumanoidPoseState` with explicit `OriginalLocal` / `NormalizedLocal` pose space
- normalized standard/custom expression domain
- VRM0 and VRM1 expression-name compatibility mapping
- optional full-body routing independent from face-source priority
- VMC original-to-normalized bone-posture conversion using UniVRM initial rotations
- VMC full-body application through UniVRM ControlRig/fallback bones
- normalized final-character snapshot provider
- VMC UDP sender using source-neutral snapshots
- default VRM0 expression-name transmission for compatibility
- runtime packet/error/latency diagnostics
- region-specific presence evidence so VMC full-body presence does not mask face loss
- neutral/reference fallback when full-body VMC tracking is lost

The implementation follows the published VMC guidance that VRM1 senders should transmit original/non-normalized humanoid bones by default rather than ControlRig-normalized bones.

## P0 validation still required

ADR acceptance remains blocked until physical interoperability evidence exists.

Required validation:

- receive representative VMC data from at least one external application
- verify root/bone local coordinate conventions
- verify full-body retargeting across at least two different VRM rigs
- verify VRM0 expression names are converted correctly into VRM1 runtime expressions
- send runtime pose/expression data to at least one external VMC receiver
- confirm stale/missing sender recovery
- confirm malformed packets are ignored without destabilizing the runtime
- verify coexistence with ARKit/MediaPipe routing
- verify optional VMC does not add baseline recurring cost when disabled

## Alternatives

- project-specific OSC schema only
- WebSocket-only motion transport
- direct integration for every tracking application

## Revisit conditions

Accept this ADR after the P0 interoperability gates pass.

Reject first-class status if interoperability, maintenance cost, or required data coverage proves insufficient. Generic OSC may remain supported independently.
