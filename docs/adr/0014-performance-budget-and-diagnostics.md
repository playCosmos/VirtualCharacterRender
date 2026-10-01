# ADR-0014: Performance budget and diagnostics are architectural requirements

- Status: Accepted
- Date: 2026-10-01

## Context

The application must remain lightweight in its default one-character configuration while still allowing advanced features.

## Decision

Performance gates and per-subsystem diagnostics are part of feature acceptance.

Primary product targets:

- minimum supported output: 720p60
- recommended output: 1080p60
- higher/custom resolutions allowed without the same blanket performance guarantee
- one active character
- otherwise-unloaded host PC should sustain 60 FPS for the validated baseline configuration

Each major feature reports disabled recurring cost and enabled incremental cost.

## Consequences

- reference workloads/test machines must be versioned
- average FPS alone is insufficient
- profiling/diagnostics are implemented early
- regressions can block feature acceptance

## Revisit conditions

Numerical budgets may be tightened or separated by hardware class based on P0/P1 evidence without changing the core policy.
