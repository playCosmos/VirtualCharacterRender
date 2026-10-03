# Appearance Quick Change

Updated: 2026-10-03

## Purpose

VirtualCharacterRender needs a one-character appearance system that can change clothing and accessories without replacing the active performer/avatar runtime.

This is separate from loading another VRM file. The active character identity, tracking bindings, motion/expression mixer, event bindings, camera/output state, and application session remain intact while appearance changes.

## Product scope

The baseline quick-change path supports:

- named appearance presets
- named outfit variants
- accessory slots
- immediate manual switching from the Character UI
- event-triggered switching through the existing event runtime
- per-character persistence
- deterministic rollback when a requested appearance cannot be applied

One active character remains the product scope.

## Baseline runtime model

Appearance state is character-owned and non-destructive.

```text
Active Character
  └─ Appearance Runtime
       ├─ Outfit Variant
       ├─ Accessory Slots
       │    ├─ Head
       │    ├─ Face
       │    ├─ Neck
       │    ├─ Back
       │    ├─ Left Hand
       │    ├─ Right Hand
       │    └─ Custom named slots
       └─ Appearance Preset
            ├─ outfit id
            └─ accessory selections
```

A preset is a named snapshot of outfit and accessory selections. Applying a preset must be atomic from the user's point of view.

## Outfit variants

The first supported outfit path is a registered variant belonging to the active character.

Preferred implementations, in order:

1. activate/deactivate registered outfit roots already compatible with the loaded character
2. load a character-specific appearance package that declares the compatible character/skeleton
3. only later, support external skinned-garment replacement when skeleton/bind-pose compatibility can be proven

The baseline must not assume that an arbitrary skinned mesh can be attached safely to any VRM skeleton.

Changing an outfit must not:

- reload the whole VRM unless explicitly requested
- reset tracking
- reset motion/expression mixer state
- reset environment, events, or broadcast output
- destructively edit the source VRM

## Accessories

Accessories are attached through named slots or explicit humanoid-bone/character anchors.

Each accessory definition contains at minimum:

- stable accessory id
- asset/resource id
- target slot or anchor
- local position
- local rotation
- local scale
- optional visibility/default-enabled state

A slot is single-selection by default. A slot may explicitly allow multiple stacked accessories when needed.

Accessory attach/detach must use the same active character and must not create a second character runtime.

## Quick-change behavior

Default switch behavior is immediate.

The runtime sequence is:

1. validate requested outfit/accessories
2. prepare required objects/assets inactive where possible
3. verify required anchors and compatibility
4. apply the complete target appearance
5. publish the new appearance state
6. if application fails, restore the previous complete appearance state

No partially applied preset should remain after a failed switch.

Optional fade/dissolve presentation can be added later through existing material/environment transition facilities; it is not required for the baseline quick-change contract.

## UI plan

P11 Character UI gains an Appearance / Quick Change area with:

- current preset
- previous/next preset
- direct preset selection
- outfit selection
- accessory slot selection
- clear accessory action
- save current selection as a user preset
- restore character default appearance

The initial UI may use compact controls. Platform-native file browsing for importing external appearance assets remains behind the platform/UI adapter boundary.

## Event integration

The event runtime should target application concepts rather than GameObjects.

Planned actions:

```text
appearance.set_preset
appearance.set_outfit
appearance.set_accessory
appearance.clear_accessory
appearance.restore_default
```

This allows hotkeys, OSC/WebSocket events, donations, chat rules, or local automation to trigger the same quick-change runtime used by the UI.

## Persistence

Appearance presets are stored per character profile, keyed by a stable character identity/path profile rather than globally mutating the VRM.

Persist:

- preset id/name
- outfit id
- accessory slot selections
- user-defined accessory transforms when explicitly allowed

Do not persist Unity object instance references.

## Compatibility and failure policy

A requested appearance is rejected when:

- required outfit/accessory asset is missing
- required anchor does not exist
- character compatibility metadata does not match
- skinned-mesh skeleton/bind-pose compatibility cannot be established
- package validation fails

Failure keeps the previous appearance active and exposes a user-facing error plus diagnostics.

## Performance policy

Quick change is not a frame-by-frame system.

- inactive presets must not keep update loops running
- unused accessories should be unloaded or pooled according to measured cost
- switching may allocate/load assets, but steady-state rendering cost should only include the active appearance
- no additional tracking inference is introduced

## Phase placement

### P11 — Application UI

Add the Character > Appearance / Quick Change controls and application-facing appearance state/actions.

### P12 — Advanced One-Character Scene Tooling

Add appearance authoring/import tooling:

- register outfit roots
- create/edit accessory slots and anchors
- build appearance presets
- import validated external accessory packages
- optional compatible skinned-outfit package workflow
- preview and validate a preset before making it active

## Deferred evidence

The feature is planned, not yet validated.

Required evidence includes:

- real VRM outfit-root switching
- accessory bone/anchor stability during tracking and full-body motion
- material/shader preservation across switches
- no tracking/mixer reset during quick change
- failed-switch rollback
- memory and frame-time behavior with multiple prepared presets
- Windows/macOS asset import and path handling
