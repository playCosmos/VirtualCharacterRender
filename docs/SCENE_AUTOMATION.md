# Scene Automation Authoring

P12 adds a source-level authoring surface for scene automation without adding a second runtime automation engine.

Menu:

```text
VCR > P12 > Open Scene Automation Authoring
```

The window authors the logical ids already consumed by the existing Event Runtime handlers:

- environment state ids handled by `EnvironmentStateEventActionHandler`
- prop ids handled by `PropEventActionHandler`
- effect ids handled by `EffectEventActionHandler`

Event rules continue to emit ordinary `EventActionCommand` values. The scene authoring window only makes those logical ids and scene-object bindings easier to define and validate.

## Environment states

The Environment section edits the existing `BasicEnvironmentRuntime` serialized configuration:

- Environment Id
- active / initial State Id
- default transition mode
- default transition duration
- ordered state bindings

Each state binding maps:

```text
state id -> GameObject root
```

`Add State From Selection` creates a new logical state from the currently selected GameObject. The generated id is normalized for authoring convenience and receives deterministic numeric suffixes when needed.

`BasicEnvironmentRuntime.RebuildStateBindings` reuses the existing `ConfigureStateBindings` validation path. It does not introduce separate runtime state semantics.

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

`Validate & Apply` applies the pending SerializedObject edits and then validates all configured registries through their existing runtime boundaries:

```text
BasicEnvironmentRuntime.RebuildStateBindings
PropEventActionHandler.RebuildBindings
EffectEventActionHandler.RebuildBindings
```

The window stores the last valid EditorJson snapshot of every assigned component.

If any configured registry fails validation:

1. the environment, prop, and effect components are all restored from the last valid snapshots
2. each runtime registry is rebuilt again
3. the authoring UI is rebound to the restored serialized state

This prevents a valid environment edit from remaining partially applied when a prop or effect edit in the same authoring operation is invalid.

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

These are synchronous ordered command templates. They do **not** introduce waits, delays, parallel branches, or another sequencer.

Use Appearance Transition timelines when a change requires timed choreography, markers, Blocking actions, or All/Any completion dependencies.

## Scope and evidence

This source implementation does not prove:

- real ParticleSystem visuals
- real environment transition presentation
- prop visibility against a production scene
- performance with production effect counts
- OBS / standalone interaction

Those require Unity Editor/standalone evidence on real project scenes and remain deferred in the current environment.
