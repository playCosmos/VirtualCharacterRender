# U2 — UI-independent remembered VRM session (first integrated slice)

**Base:** U1 Draft PR #44 at `10157e145b273465df502557bb0eef32477236af`. This is a stacked implementation; the user's instruction to *assume* the physical U1 gates have passed is a planning premise, **not** an assertion that physical evidence was collected. Nothing here merges PR #44, removes legacy uGUI, tags a release or claims hardware certification.

## Implemented

- `ModelSessionController` owns the unchanged `VCR.Character.LastSuccessfulPath.v1` PlayerPrefs key, restore candidate policy, successful interactive load persistence and explicit unload deletion. The original v1 key remains compatible; there is no new or silently migrated file format.
- `ApplicationRuntimeBootstrap` owns remembered-file restore in standalone Players after runtime initialization/configuration. Explicit `--vcr-vrm` has priority over the saved model; a failed explicit CLI load never silently loads a different remembered model. In both UI modes, an enabled Player bootstrap suppresses a second competing automatic restore; Editor and scenes without bootstrap retain their existing fallback.
- Supported automatic restores are VRM only. Missing or unsupported paths preserve the old reference and produce a single actionable inventory warning; no directory scan, FBX or thumbnail subsystem is implied. Unreadable VRM load failures are logged using basename and exception type, and optional scene state recovery returns to Ready *without resetting already applied render, camera or light configuration*. The old remembered path remains for explicit operator cleanup.
- U1 and legacy UI forward confirmed manual load and explicit unload to the same UI-independent preference API. CLI load and reload do not rewrite the last *manual* reference.
- A P1 regression checks genuine PlayerPrefs v1 interoperability, empty/missing/unsupported candidate decisions, CLI precedence, active-model precedence, success-only persistence and explicit unload, with integration source assertions.

## Out of scope and remaining gate

U2 still needs a bounded opt-in model library/catalog, versioned model-session schema and corrupt-record migration/rollback design. No generic FBX adapter, simultaneous VRM instances, automatic filesystem indexing, or source file upload. Native runtime/CI runs on the exact U2 commit are required before acceptance; physical U1 verification is not fabricated. The legacy uGUI retirement mandated by `docs/OBS_UI_U1_VALIDATION.md` remains a separate cleanup/change-review step until all references are mapped and all source/Player checks pass. A stacked U2 PR remains Draft; neither PR should be merged prematurely.
