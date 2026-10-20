# ADR-0010: Built-in basic motion capture

- Status: Accepted
- Date: 2026-10-01

## Context

A single performer should be able to animate a character without purchasing or configuring a dedicated external mocap application. The product must also accept higher-grade external tracking when desired.

## Decision

Provide built-in basic motion capture through tracking adapters for one active performer.

Baseline tracking domains:

- face, with eyes and mouth prioritized
- head
- hands
- upper body

Initial sources include:

- ordinary webcam tracking
- iPhone/iPad ARKit-compatible facial tracking
- optional audio-driven fallback

Full-body tracking is a separate optional capability and may use external trackers, VMC, camera-based solvers, or future adapters.

Apple device input is treated as ARKit face-tracking data, not authentication logic. Product UI may describe compatible TrueDepth/Face-ID-class devices for user clarity.

All built-in and external sources produce the same normalized tracking state.

## Consequences

- basic use has no dependency on another VTuber application
- face quality, especially eye/mouth behavior, is a primary tracking-quality target
- hand and upper-body inference are part of baseline capability/performance validation
- users can upgrade or mix source quality without changing the character pipeline
- full-body solvers do not tax the baseline when disabled
- mobile-device transport/discovery requires a defined protocol
- camera/microphone permissions require platform-specific handling

## Validation

- webcam tracking reaches normalized face/eye/mouth/head/hand/upper-body state at acceptable latency and stability
- iOS/iPadOS face tracking can transmit head/expression data into desktop runtime
- source loss produces stable fallback behavior
- switching between webcam, mobile face tracking, and VMC does not require reloading the character
- disabled tracking capabilities leave no meaningful inference/network work active
- multi-person identity tracking is not required

## Revisit conditions

Specific tracking libraries/models may change independently. The baseline domains remain a product requirement unless superseded.
