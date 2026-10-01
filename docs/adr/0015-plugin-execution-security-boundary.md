# ADR-0015: Plugin execution and security boundary

- Status: Proposed
- Date: 2026-10-01

## Context

Shader, tracking, broadcast-service, and advanced automation extensions may eventually require executable code. Arbitrary in-process code can compromise stability, credentials, files, and the renderer.

## Proposed decision

Prefer declarative/data packages first. Treat executable plugins as a separate trust tier with explicit lifecycle, permissions, version compatibility, resource ownership, failure isolation, and user consent.

## Validation before acceptance

- define trusted vs untrusted extension model
- define in-process vs isolated-process options
- define credential access rules
- define file/network permissions
- define crash containment and diagnostics
- define signing/identity policy if applicable

## Revisit conditions

Accept only after concrete plugin use cases cannot be served safely by declarative packages or protocol adapters.
