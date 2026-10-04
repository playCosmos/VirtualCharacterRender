# Scene Automation Authoring

P12 adds a source-level authoring surface for scene automation without adding a second runtime automation engine.

Menu:

```text
VCR > P12 > Open Scene Automation
```

The window authors and inspects logical ids consumed by the existing Event Runtime handlers:

- environment state ids handled by `EnvironmentStateEventActionHandler` are shown read-only from the existing P6 runtime
- prop ids handled by `PropEventActionHandler`
- effect ids handled by `EffectEventActionHandler`
- timed logical sequences handled by `SceneSequenceEventActionHandler`

Event rules continue to emit ordinary `EventActionCommand` values. The scene authoring window makes logical ids, scene-object bindings, and timed composition easier to define without introducing a second event engine.

## Environment states

The Environment section intentionally reads the existing `BasicEnvironmentRuntime` configuration instead of duplicating P6 environment authoring.

It shows:

- Environment Id
- current State Id
- ordered existing state ids and their GameObject roots
- Copy ID for event/sequence authoring

Environment state creation, transition-target configuration, lighting, and space-mode authoring remain owned by the P6 runtime/tooling contract.

## Props

The Prop section edits `PropEventActionHandler` bindings.

Each prop maps:

```text
prop id -> one or more GameObject roots
```

`Add Prop From Selection` creates a binding whose first root is the selected GameObject.

Runtime actions remain:

- `prop.set_active`
- `prop.toggle`

The runtime validator rejects:

- blank ids
- duplicate ids
- empty root lists
- null roots
- the same root listed twice in one prop
- a root owned by more than one logical prop id

## Effects

The Effect section edits `EffectEventActionHandler` bindings.

Each effect may define:

- Effect Id
- optional root GameObject
- zero or more ParticleSystems
- Restart On Play
- Deactivate On Stop

`Add Effect From Selection` assigns the selected GameObject as the root and discovers ParticleSystems below it, including inactive children.

Runtime actions remain:

- `effect.play`
- `effect.stop`

The existing handler remains responsible for restarting particle systems, completion probing, stopping, and optional root deactivation.

## Validation and rollback

Prop, Effect, and timed Sequence sections each use `Validate & Apply` through their existing runtime validation boundaries:

```text
PropEventActionHandler.RebuildBindings
EffectEventActionHandler.RebuildBindings
SceneSequenceEventActionHandler.RebuildBindings
```

Each editable handler keeps a last-valid EditorJson snapshot. Invalid edits are restored to that snapshot and the runtime registry is rebuilt before the authoring UI is rebound.

Environment ids are read-only in this window, so an invalid Scene Automation edit cannot partially mutate the P6 environment registry.

## Timed Scene Sequences

A sequence maps one logical `SequenceId` to ordered action steps plus optional cancellation cleanup.

Each normal step contains:

```text
TimeSeconds
ActionType
TargetId
Name
Text
Value / HasValue
Required
```

Rules:

- only one sequence runs at a time
- step times must be finite, non-negative, and non-decreasing
- sequence steps reuse existing logical `IEventActionHandler` implementations
- `scene.sequence_play` and `scene.sequence_cancel` cannot be nested recursively inside a sequence
- required actions must resolve to exactly one handler before playback begins
- an optional action with no handler may be skipped
- an ambiguous action handler match fails closed
- required-step failure runs cancellation cleanup before the sequence terminates
- cancellation cleanup executes immediately and therefore every cleanup step must use time 0
- `scene.sequence_cancel` is rejected if the active sequence has no explicit cleanup contract
- disabling the handler stops the active sequence and attempts its cleanup contract

Runtime actions:

- `scene.sequence_play` — command text is the logical SequenceId
- `scene.sequence_cancel`

The handler exposes a completion probe, so a successfully completed timed sequence can itself be awaited by an existing transition dependency.

The Scene Automation window provides `Add Scene Change Starter`, which authors a simple timed Environment → Prop → Effect sequence plus Effect/Prop cleanup placeholders.

## Composite Event Rule templates

The Event Node Editor also provides starter rules that compose the registered scene ids through the existing ordered `Actions[]` contract.

### Manual Scene Sequence

```text
local.manual
  -> environment.set_state  state-id  Fade 0.5s
  -> prop.set_active        prop-id   true
  -> effect.play            effect-id
```

The rule is grouped under:

```text
scene-automation/manual
```

### Donation Scene Burst

```text
broadcast.donation
  -> prop.set_active        prop-id   true
  -> effect.play            effect-id
```

The rule is grouped under:

```text
scene-automation/broadcast
```

These remain synchronous ordered command templates. For actual delays and cancellation cleanup, use a `SceneSequenceEventActionHandler` sequence and the `Scene / Manual → Timed Sequence` Event Node starter rule.

Appearance Transition timelines remain the richer appearance-specific tool when choreography needs named markers, Blocking actions, or All/Any completion dependencies tied to wardrobe switching.

## Project-local Effect Presets

`VCR/P12/Open Scene Automation Authoring` also supports Effect Preset v1.

A preset is an editor authoring asset over the existing `EffectEventActionHandler` contract. It does not add a second effect runtime.

The window can create a reusable prefab + preset asset from the selected scene ParticleSystem root, then install that preset as a normal logical effect binding.

Effect Preset v1 deliberately permits only Transform, ParticleSystem, and ParticleSystemRenderer components. Arbitrary MonoBehaviours, AudioSource, Animator, physics, skinned renderers, missing scripts, and other components are rejected before asset creation/installation.

Creation writes under `Assets/VCR/EffectPresets` by default. Installation is transactional: a scene instance is created, all ParticleSystems are discovered, one `EffectBinding` is appended, and `RebuildBindings` must succeed or both the handler mutation and created instance are rolled back.

See `EFFECT_PRESETS.md` for the exact contract and limits. `VCR/P12/Open Effect Preset Library` indexes project presets, keeps invalid entries visible for diagnostics, supports search, and hands valid presets back to Scene Automation Authoring for installation through the same validated binding path.

## Scope and evidence

This source implementation does not prove:

- real ParticleSystem visuals
- real environment transition presentation
- prop visibility against a production scene
- performance with production effect counts
- OBS / standalone interaction

Those require Unity Editor/standalone evidence on real project scenes and remain deferred in the current environment.
