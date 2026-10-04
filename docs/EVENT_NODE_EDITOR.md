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
- add/duplicate/delete rule
- visual stage/node selection
- add/delete/reorder Condition, Mutation and Action nodes
- full serialized field editing for the selected node
- deterministic unique rule-id generation
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

Action-handler availability is deliberately not hard-coded into the editor. Handler registration remains a runtime capability of the selected `EventRuntimeHost`, so custom action handlers can continue to work without modifying the node editor.

## Scope

This first slice is a visual editor for the current linear rule contract. It does not pretend that the runtime already supports arbitrary graph control flow.

Future P12 extensions may add richer grouping, templates, rule libraries, and higher-level node composition, but any new branching/dependency semantics must first be defined in the runtime contract rather than being hidden inside editor-only behavior.

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
- the SerializedProperty contract used by the editor

These source validations have not been executed in the current environment because Unity Editor/runtime execution is unavailable here.
