# P12 Status — Advanced One-Character Scene Tooling

P12 starts from `checkpoint/p11-source-implementation`.

This phase keeps the one-active-performer product scope and builds authoring/import tooling over the runtime contracts established in earlier phases. It does not introduce multi-character runtime behavior.

## Implemented source slice

### Appearance / Wardrobe Authoring

Menu:

- `VCR/P12/Open Appearance Authoring`

The first P12 editor surface binds directly to `BasicCharacterAppearanceRuntime` through `SerializedObject`.

It can author:

- explicit outfit bindings
- outfit root arrays
- accessory slot/id/root bindings
- authored appearance presets
- default preset id / apply-on-awake
- convention hierarchy names and auto-discovery policy

Convenience authoring includes:

- add empty outfit/accessory/preset rows
- add the currently selected GameObject as an outfit root
- add the currently selected GameObject as an accessory root
- infer the accessory slot from `VCRAppearance/Accessories/<slot>/<item>` when selection follows the convention hierarchy
- capture the runtime's current outfit/accessory state into a new authored preset
- set an authored preset as the default
- open the existing P11 transition timeline from the same authoring workflow

### Convention → explicit binding promotion

`P12AppearanceAuthoringUtility.TryDiscoverConvention` reads:

```text
VCRAppearance/
  Outfits/
    <outfitId>/
  Accessories/
    <slotId>/
      <accessoryId>/
```

and converts direct hierarchy children into explicit `AppearanceOutfitBinding` and `AppearanceAccessoryBinding` arrays.

Discovery is fail-closed for:

- missing appearance root
- empty logical ids
- duplicate outfit ids
- duplicate slot/accessory logical keys
- hierarchy with no discoverable outfit/accessory entries

Static skeleton offsets, transitions, presets and other runtime configuration are not guessed by discovery.

### Validation / rollback behavior

The authoring window separates serialized editing from runtime registration.

`Validate & Apply`:

1. applies the edited serialized fields,
2. calls `BasicCharacterAppearanceRuntime.RebuildConfiguration`,
3. records the new configuration as the last valid authoring snapshot on success,
4. restores the last valid serialized snapshot if runtime validation fails.

This keeps invalid root sharing, duplicate ids, missing roots or invalid preset references from becoming the accepted authoring state.

The rollback uses editor-only serialized JSON over the scene component; runtime dictionaries and transition executors are rebuilt from the restored serialized fields.

## Source validation

Menu:

- `VCR/P12/Run Source Validation`

The validation source covers:

- convention hierarchy → explicit outfit/accessory binding discovery
- expected two-outfit/two-accessory discovery counts
- runtime `ConfigureBindings` / `RebuildConfiguration` readiness
- immediate authored preset application through the normal appearance runtime path
- current appearance → authored preset capture
- required serialized-property names used by the authoring window
- duplicate discovered outfit-id rejection
- stable suffix generation for duplicate logical ids

These checks are implemented as Unity Editor validation code but have not been executed in the current environment because Unity Editor/runtime execution is unavailable here.

## Deferred evidence

P12 is not runtime/device validated.

Required later evidence includes:

- real VRM wardrobe hierarchy authoring
- outfit/accessory root activation against a loaded UniVRM character
- accessory anchor stability during face/body/hand/full-body tracking
- failed authoring validation rollback inside the real Unity Editor
- scene save/reload preservation of explicit bindings
- imported accessory/skinned-outfit compatibility checks
- performance/memory behavior with realistic wardrobe counts

## Next P12 source work

- accessory anchor/bone authoring instead of root-only registration
- validated external accessory package format and importer
- optional compatible skinned-outfit compatibility checks
- richer appearance preset preview before activation
- interactive transition dependency graph editing/grouping
- additional motion adapters beyond BVH when a concrete format/import contract is justified
- visual event/node tooling and richer environment/prop/effect automation

