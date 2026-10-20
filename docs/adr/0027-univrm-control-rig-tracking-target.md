# ADR-0027: Apply normalized tracking through the UniVRM control rig

- Status: Accepted
- Date: 2026-10-01

## Context

UniVRM 0.131.2 runtime loading generates a VRM 1.0 control rig by default.

Directly editing raw humanoid bones before `Vrm10Runtime.Process()` can be overwritten when the control rig processes later in the frame.

The character adapter must also keep MediaPipe and other source implementations out of VRM code.

## Decision

`Vrm10TrackingTarget` consumes only normalized tracking payloads.

When UniVRM provides a runtime control rig, tracking drives the control-rig bones first.

If a control-rig bone is unavailable, the adapter falls back to the corresponding raw humanoid bone.

Execution occurs before the `Vrm10Instance` runtime processing order so UniVRM can propagate the control-rig pose, constraints, expressions, and spring bones afterward.

## Face/head mapping

- the first valid face frame establishes a neutral head reference
- subsequent head rotation is applied as a relative delta
- blink and eye-look coefficients map to VRM standard expression presets
- MediaPipe geometric mouth coefficients use conservative P0 mappings to VRM `aa/ih/ou/ee/oh`
- mouth mappings are explicitly provisional and replaceable by a later calibrated lip-sync/expression mixer

Missing VRM expression presets are safe because UniVRM ignores `SetWeight` calls for keys not present in the model.

## Upper-body mapping

The first valid upper-body frame establishes a calibration reference for:

- torso orientation
- left/right upper-arm direction
- left/right lower-arm direction

Later frames apply relative rotation deltas to the cached VRM/control-rig reference pose.

This is a P0 retargeting path, not the final IK/full-body solver.

## Hands

Hand joint payloads are preserved in normalized tracking state, but finger-bone retargeting is deferred until hand-quality and CPU measurements are available.

## Consequences

- tracking sources do not manipulate VRM objects
- default UniVRM control-rig loading is respected
- VRM 0.x models migrated through the VRM10 runtime can use the same character target
- upper-body calibration is simple enough for P0 but will need production refinement
- expression and bone behavior can be tested independently of MediaPipe

## Revisit conditions

Revisit when:

- production IK/retargeting replaces the P0 relative-direction solver
- finger retargeting is added
- a mixer owns calibration/smoothing globally
- UniVRM changes the control-rig runtime contract
