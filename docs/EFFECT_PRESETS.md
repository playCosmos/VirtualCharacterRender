# Effect Presets

P12 adds a project-local Effect Preset v1 for reusable ParticleSystem effects used by the existing `EffectEventActionHandler`.

This is an authoring asset, not a new runtime engine and not an external executable package format.

## Asset

Create through:

```text
Assets/Create/VCR/P12/Effect Preset
```

or from the P12 Scene Automation Authoring window with:

```text
Create Preset From Selection
```

The preset stores:

- format version
- logical `EffectId`
- one project prefab
- Restart On Play
- Deactivate On Stop
- Start Inactive

The runtime scene does not need the editor-only preset asset. Installation materializes a normal scene instance plus an ordinary `EffectEventActionHandler.EffectBinding`.

## Safe prefab scope

Effect Preset v1 deliberately accepts only ParticleSystem-only hierarchies.

Allowed components:

- Transform
- ParticleSystem
- ParticleSystemRenderer

Rejected examples:

- MonoBehaviour scripts
- AudioSource
- Animator
- Rigidbody / Collider
- SkinnedMeshRenderer
- arbitrary custom components
- missing scripts/components

At least one ParticleSystem is required.

The prefab must be a persistent project prefab asset when validating an existing preset.

The authoring helper can convert one selected scene GameObject into a new prefab + preset asset under:

```text
Assets/VCR/EffectPresets
```

The selected hierarchy must pass the same whitelist before any asset is created.

## Logical id rules

`EffectId` is required and may contain only:

- letters
- digits
- `.`
- `_`
- `-`

Blank ids, `.`, `..`, path-like ids, and unsafe punctuation are rejected.

Installing a preset whose EffectId is already registered in the selected `EffectEventActionHandler` fails closed.

## Authoring flow

Open:

```text
VCR/P12/Open Scene Automation Authoring
```

In the Effects section:

1. select a ParticleSystem root in the scene
2. enter New Preset ID
3. choose Create Preset From Selection
4. inspect or edit the generated preset asset if needed
5. assign/select the preset
6. choose Install Effect Preset
7. run Validate & Apply for the complete scene-automation configuration as usual

Preset creation and preset installation are separate operations by design.

Creation produces reusable project assets.

Installation creates one scene instance and registers it into the existing logical effect runtime.

## Installation semantics

A successful install:

- instantiates the preset prefab into the Effect handler scene
- parents the instance under the EffectEventActionHandler
- optionally starts it inactive
- discovers all ParticleSystems in the instance
- appends one serialized `EffectBinding`
- copies RestartOnPlay / DeactivateOnStop
- calls `EffectEventActionHandler.RebuildBindings`
- marks the scene dirty

The resulting logical id is immediately usable by:

```text
effect.play
effect.stop
```

This means the same installed preset can be triggered from:

- Event Runtime rules
- Scene Sequence automation
- Appearance transition action steps
- completion-dependent transition choreography

## Transactionality

Preset creation validates the source hierarchy before asset creation.

If prefab/preset asset creation fails, assets and newly created folders are rolled back.

Preset installation snapshots the Effect handler serialized state before mutation.

If registration fails:

- the previous serialized handler state is restored
- bindings are rebuilt from the restored state
- the newly created scene instance is destroyed

Unrelated pending Scene Automation edits must validate before preset installation begins.

## Deliberate limits

Effect Preset v1 does not provide:

- external downloadable effect packages
- arbitrary prefab/script execution
- audio payloads
- Animator-driven effects
- shader/material package installation
- skinned effects
- custom update-loop behaviours

Those should be separate reviewed package/runtime contracts if concrete requirements justify them.

For mixed scene logic, keep effect presentation in this logical binding layer and orchestrate it through existing Event Runtime / Scene Sequence contracts instead of embedding scripts inside the effect prefab.

## Validation boundary

P12 source validation covers:

- valid ParticleSystem-only preset acceptance
- scene root -> persistent prefab + preset authoring
- no-ParticleSystem rejection
- unsupported component rejection
- unsafe EffectId rejection
- scene installation
- inactive initial instance
- ParticleSystem discovery
- duplicate EffectId rejection
- serialized binding creation
- handler RebuildBindings success

Real particle visuals, material/shader compatibility, standalone builds, memory cost, and production effect counts still require Unity runtime evidence.
