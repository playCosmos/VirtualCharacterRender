# ADR-0003: UniVRM for VRM runtime

- Status: Proposed
- Date: 2026-10-01

## Context
The initial 3D runtime is expected to support VRM 0.x and VRM 1.0. Independently implementing the format, humanoid conventions, expressions, and MToon would add substantial format-specific work.

## Proposed decision
Use UniVRM as the Unity-side VRM import/runtime implementation.

## P0 validation
- VRM 0.x load
- VRM 1.0 load
- expressions
- humanoid bones
- MToon
- runtime loading
- material override compatibility
- selected Unity/URP compatibility
- required animation/runtime behavior

## Boundary
UniVRM objects remain inside model/backend integration. Higher layers reference VirtualCharacterRender concepts.

## Alternatives
- custom VRM/glTF implementation
- another VRM library
- web backend with three-vrm

## Revisit conditions
Reject if supported engine versions, runtime loading, material pipeline, or VRM-version support conflict with renderer requirements.
