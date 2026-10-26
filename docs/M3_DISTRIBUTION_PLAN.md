# M3 commercial distribution, privacy and support — execution plan

Status: **IMPLEMENTATION IN PROGRESS — DISTRIBUTION APPROVAL DEFERRED**
Baseline: stacked on M2 draft PR #33 at `dac0ff7f1afc7c54486a75cefd85fa76fe19d8ba` (which itself depends on M1 #27 and exit fixes #26).
Scope authority: [commercial readiness plan](COMMERCIAL_READINESS_PLAN.md), M3.

## Rules

- Proceed with M3 source, tooling and documentation while M2 G-level VRM/OBS/real-device acceptance remains blocked on real physical tests. Do **not** call M2 PASS or publish a commercial release.
- A regular CI-built alpha ZIP is **not** a signed or notarized commercial distribution. Existing alpha release workflow is immutable-development-alpha machinery, **not** the M3 production workflow. Do not overwrite historical releases or reuse their tags.
- Every automated checker reports its actual assurance level. Source-bound archive integrity is not signature trust, safe execution, malware absence, legal clearance or a clean-machine installation.
- Never put Apple ID passwords, Developer ID certificates/private keys, PFX, notary API keys, customer model data or camera/OBS captures in git, CI output, logs or publicly shared diagnostics.

## Required M3 workstreams and exit criteria

| Gate | Implementation | Required independent acceptance |
| --- | --- | --- |
| M3-A Artifact provenance | Verify version/source SHA and both independently downloaded ZIP hashes against SHA256SUMS and build-provenance; reject unsafe ZIP paths and duplicate entries | Rehash actual distributed signed/stapled archives from release host; independently verify source association |
| M3-B Windows signing | Document Authenticode signing, timestamping, and native Windows verification on final distributed EXE/installer; no unsigned fallback | Valid trusted certificate chain and timestamp on supported Windows account; SmartScreen/reputation not implied |
| M3-C macOS distribution | Document Developer ID sign with hardened runtime, notarization, staple and Gatekeeper checks for final `.app` | Native Apple Silicon clean-user launch, codesign + notarization + Gatekeeper pass and permission prompts |
| M3-D Lifecycle | Portable ZIP or installer semantics, upgrade/rollback/uninstall, config preservation, clean-profile migration tests | Physical Windows/macOS clean-install, upgrade/downgrade & rollback evidence; user data preserved |
| M3-E Dependencies and licenses | Existing pinned Unity/package/bootstrap inventory plus reviewed component license/notice checklist | Human-approved redistribution of all native libraries, models, fonts and third-party assets; separately verify Unity terms |
| M3-F Security and privacy | Audit inputs/network ports/shader/plugin trust; public-facing data practices and diagnostics-sharing guardrails | Test denial/malformed inputs, no unexpected upload or startup listener, consented diagnostics with redaction |
| M3-G Support | First-run, crash and stuck-shutdown runbook, support evidence submission checklist and known limits | Accessible keyboard/DPI/localization tests and support-guided clean-PC reproduction |

## Execution order

1. **A + D:** implement a *read-only*, offline stdlib release-archive provenance preflight and negative self-tests. This checks packaging only and never signs or releases anything. Document the separate Windows/macOS signing/notarization path and operator review.
2. **E + F:** produce license/security review worksheet based on existing `audit-third-party-dependencies.py`; report unreviewed components rather than marking them approved. Publish clear local-only privacy/disclosure instructions.
3. **G:** first-run, portable lifecycle and support runbook with reproducible OS-specific acceptance matrix.
4. **B + C + R acceptance:** operator provides credentials locally or in appropriately restricted release infrastructure, signs build outputs, notarizes/staples Apple app and runs independent clean-machine tests. Only when artifacts and signed native evidence exist can M3 be marked PASS.

## Checkpoints

- Each coherent workstream receives its own commit, no unrelated shader upgrades or runtime dependencies.
- Repository and targeted self-tests must be green at exact head; Unity compilation and native Player smoke must remain green when source affects runtime/build.
- Signing/notarization, distribution licenses and clean OS behavior remain **DEFERRED** until independently collected. M3 remains draft/unmerged and M4/R0 cannot be declared approved by documentation alone.
