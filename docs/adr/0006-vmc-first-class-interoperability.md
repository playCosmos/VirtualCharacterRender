# ADR-0006: VMC as first-class motion interoperability protocol

- Status: Proposed
- Date: 2026-10-01

## Context
The project should interoperate with existing virtual-character tracking and motion tools instead of requiring all sources to be implemented natively.

## Proposed decision
Support VMC input and output as an early interoperability protocol while keeping VMC outside the internal normalized-state model.

## P0 validation
- receive representative VMC data
- convert it to normalized/runtime state
- send runtime pose/expression data
- verify coordinate conventions
- handle missing/stale senders
- interoperate with at least one external VMC-compatible application

## Alternatives
- project-specific OSC schema only
- WebSocket-only motion transport
- direct integration for every tracking application

## Revisit conditions
Reject first-class status if interoperability, maintenance cost, or required data coverage proves insufficient. Generic OSC may remain supported independently.
