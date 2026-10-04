# Event Node Editor

P12 adds the first visual authoring surface for the existing P9 Event Runtime.

Menu:

```text
VCR > P12 > Open Event Node Editor
```

The editor does not add a second graph execution engine. It edits the serialized `EventRuntimeRule[]` owned by an `EventRuntimeHost`, validates those rules, and applies them back through the existing `EventRuntimeHost.SetRules` / `EventRuntimeEngine` path.

## Graph semantics

The first visual slice mirrors the runtime pipeline:

```text
Event / Filter
      ↓
Conditions (AND)
      ↓
State Mutations
      ↓
Actions
```

The stage order is intentionally fixed because it is already part of the P9 runtime semantics.

- the Filter node edits `EventRuleFilter`
- Condition nodes edit ordered `EventStateCondition[]`; every condition must pass
- Mutation nodes edit ordered `EventStateMutation[]`
- Action nodes edit ordered `EventActionTemplate[]`
- cooldown, rate-limit, enabled state and StopAfterMatch remain rule-level properties

The editor supports:

- previous/next rule navigation
- optional per-rule `Graph Label` / `Graph Group` authoring metadata
- grouped-rule overview with member/enabled counts, same-group Prev/Next navigation, and exact-group Enable/Disable authoring actions
- nested `GraphGroup` paths using `/`, with implicit parent hierarchy display
- hierarchy-wide Enable/Disable with optional descendant inclusion
- hierarchy Rename/Move and exact-vs-recursive Clear without changing EventRuntime semantics
- hierarchy Duplicate that deep-clones the selected subtree, assigns deterministic copy ids, and rewrites the destination group path
- hierarchy Export that emits only the selected exact/subtree rules through the existing validated rule-library package path
- add/duplicate/delete rule
- visual stage/node selection
- add/delete/reorder Condition, Mutation and Action nodes
- full serialized field editing for the selected node
- deterministic unique rule-id generation
- built-in starter templates for local manual restore-default, subject-lost restore-default, chat → appearance preset, donation → effect, manual → prop toggle, and chat → environment fade workflows
- export of the selected rule or the complete host rule set as a versioned JSON rule-library package
- import/merge of a rule-library package after current pending edits pass authoring validation
- deterministic `-2`, `-3`, ... suffixing when imported rule ids collide with existing ids
- `VCR/P12/Open Event Rule Library` project browser with package/rule-id/description/tag/path/error search
- valid and invalid project packages remain visible; external JSON can be validated and added to the project library
- same-PackageId revision history with deterministic revision ordering, Previous/Next navigation, Compare Previous, and added/removed/changed rule summaries
- duplicate valid files for the same PackageId+Revision are treated as ambiguous and revision navigation/diff fails closed instead of picking one silently
- asset ping/path copy and pending-package handoff back into the Event Node Editor
- Validate & Apply
- rollback to the last valid serialized host snapshot when validation fails

## Authoring validation

P12 adds fail-closed authoring validation before applying edited rules.

It rejects:

- null rules
- blank or duplicate rule ids
- negative/non-finite cooldown
- negative/non-finite rate-limit windows
- half-configured rate limits where only window or count is positive
- non-finite filter amount limits
- minimum amount greater than maximum amount
- Condition nodes without a state key
- non-finite numeric Condition values
- Mutation nodes without a state key
- non-finite Mutation numeric constants/scales/offsets
- Action nodes without `ActionType`
- non-finite Action numeric values
- malformed `GraphGroup` paths with empty segments or `.` / `..`; valid paths are canonicalized on validation

Action-handler availability is deliberately not hard-coded into the editor. Handler registration remains a runtime capability of the selected `EventRuntimeHost`, so custom action handlers can continue to work without modifying the node editor. P12 Scene Automation authors logical Prop/Effect bindings while Environment state ids remain owned by the established P6 runtime.

## Rule templates and library packages

The editor includes editor-only authoring conveniences that do not change runtime semantics.

Built-in templates create ordinary `EventRuntimeRule` instances using existing normalized event/action contracts. Placeholder ids such as `preset-id` or `effect-id` are intentionally visible and must be edited to match the selected runtime configuration.

Rule-library package v2 contains:

```text
Version
PackageId
Description
Tags[]
Revision
Rules[]
```

Version 1 packages migrate automatically to v2 with an empty description, no tags, and revision 1. Description/tags/revision are editor-library metadata only and never alter EventRuntime execution.

Import validates the package version, metadata, and every contained rule through the same `P12EventRuleAuthoringUtility` used by the graph editor. Unsupported newer versions fail closed. Revision must be >=1; blank or case-insensitive duplicate tags are rejected. Existing host rules are preserved and imported rules are appended; id collisions are renamed deterministically rather than silently replacing existing behavior.

Template insertion/import/export first applies the current serialized edits and requires them to pass validation. This prevents a library operation from discarding an invalid pending graph silently.

## Scope

This remains a visual editor for the current linear rule contract. It does not pretend that the runtime already supports arbitrary graph control flow.

`Graph Label` and `Graph Group` are authoring metadata only and are preserved through normal rule serialization/library packages; they never change EventRuntime matching or execution order. `GraphGroup` supports canonical nested `/` paths. Parent/subtree enable-disable, rename/move/clear, deep-clone duplicate, and subtree package export are authoring operations over ordinary rules, not new runtime control-flow semantics. Future P12 extensions may add deeper revision workflows such as explicit branch/merge semantics or visual collapse/lasso only when justified. Any new branching/dependency runtime semantics must first be defined in the runtime contract rather than being hidden inside editor-only behavior.

## Validation status

`VCR/P12/Run Source Validation` covers:

- one valid Filter/Condition/Mutation/Action rule
- duplicate rule-id rejection
- invalid amount-range rejection
- incomplete rate-limit rejection
- blank condition-key rejection
- blank action-type rejection
- non-finite action numeric rejection
- deterministic unique rule ids
- every built-in starter template validates against the existing runtime rule contract
- rule-library v2 JSON metadata/rule round trip
- v1→v2 metadata migration
- blank/duplicate tag validation
- deterministic imported-id collision suffixing
- unsupported newer library-version rejection
- project library valid/invalid package indexing
- package-id and rule-id search matching
- revision-history grouping/sorting and Previous/Next lookup
- previous-revision metadata/rule diff
- duplicate-revision ambiguity rejection
- Graph Label / Graph Group package round trip
- grouped rule summary, same-group navigation, and exact-group enable/disable helpers
- nested group parent-path capture, subtree enable/disable, rename/move, exact/recursive clear, self-nesting/malformed-path rejection
- hierarchy rule capture plus deep-clone duplicate with deterministic ids and destination path rewrite
- rule-library JSON round trip preserves canonical nested GraphGroup metadata
- the SerializedProperty contract used by the editor, including Graph Label / Graph Group

These source validations have not been executed in the current environment because Unity Editor/runtime execution is unavailable here.
