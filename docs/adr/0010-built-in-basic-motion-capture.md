# ADR-0010: Built-in basic motion capture

- Status: Accepted
- Date: 2026-10-01

## Context

A user should be able to animate a character without purchasing or configuring a dedicated external mocap application. The product must also remain compatible with higher-grade external tracking systems.

## Decision

Provide built-in basic motion capture through tracking adapters.

Initial product targets:

- ordinary webcam face/head tracking
- iPhone/iPad ARKit-compatible facial tracking, including devices capable of high-quality front-camera face tracking
- audio-driven fallback mouth/body motion
- optional webcam hand/body tracking where performance and quality meet release gates

Apple device input is treated as ARKit face-tracking data, not as authentication logic. Product UI may describe compatible TrueDepth/Face-ID-class devices for user clarity, while the internal integration remains a tracking adapter.

All built-in tracking produces the same normalized tracking state consumed by external VMC or other sources.

## Consequences

- basic usage has no dependency on another VTuber application
- users can upgrade tracking quality without changing the character pipeline
- webcam inference cost must be budgeted, especially for Lightweight profile
- mobile-device transport/discovery requires a defined protocol
- privacy and camera/microphone permissions require platform-specific handling

## Validation

- webcam face/head tracking reaches normalized state at acceptable latency
- iOS/iPadOS face tracking can transmit expression/head data into the desktop runtime
- source loss produces stable fallback behavior
- switching between webcam, mobile face tracking, and VMC does not require reloading the character
- tracking can be disabled without leaving inference/network work active

## Revisit conditions

Specific tracking libraries/models may change independently. Built-in basic capture remains a product requirement unless superseded.
