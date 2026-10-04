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
- optional accessory anchors: explicit Transform or humanoid Animator bone
- accessory local position/rotation offsets, optional local scale override, and restore-original-transform policy
- authored appearance presets
- default preset id / apply-on-awake
- convention hierarchy names and auto-discovery policy

Convenience authoring includes:

- add empty outfit/accessory/preset rows
- select a Transform as an accessory anchor
- select a humanoid Animator + HumanBodyBones anchor
- resolve the runtime humanoid Animator from the authoring window
- capture the accessory's current position/rotation as anchor-local offsets
- reset authored anchor offsets
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

Accessory application remains transactional as well. The runtime snapshots active states and accessory Transform parent/local state before switching. Transform-anchored accessories are reparented and offset only for the requested appearance; inactive anchored accessories can restore their original hierarchy transform. Any anchor/apply failure restores both active-state and Transform-state snapshots. Humanoid-bone anchors resolve through a humanoid `Animator.GetBoneTransform` and fail closed if the animator/bone is unavailable.

## External rigid accessory packages

P12 now includes a declarative accessory package v1 and importer. The format is intentionally limited to one rigid FBX model plus logical slot/id and optional humanoid-bone anchor metadata. External prefabs, scripts, executable payloads, and skinned meshes are rejected.

`VCR/P12/Open Appearance Authoring` exposes `Import Accessory Package`. Import is transactional: existing unrelated pending authoring edits must be validated first, then the importer copies the manifest/FBX, validates the Unity import result, creates an inactive scene instance, appends the explicit binding, and immediately runs `RebuildConfiguration`. The package is retained only if the full asset + scene + runtime-registration transaction succeeds. Humanoid-bone packages fail closed unless the selected runtime exposes the requested bone through a humanoid Animator.

Package asset/scene/runtime-registration failures roll back the imported package folder, created scene instance, and serialized binding changes. `PackageId + PackageVersion` maps to one canonical project folder; importing the same version twice is rejected rather than silently creating suffixed duplicates.

See `ACCESSORY_PACKAGES.md` for the v1 manifest, safety rules, and importer contract.

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
- Transform-anchor reparent + authored local offset application
- inactive accessory original-parent/local-transform restoration
- repeated reactivation after restoration
- descendant-anchor cycle rejection at both authoring-helper and runtime-configuration boundaries
- non-finite accessory anchor pose rejection before preview/runtime use
- serialized anchor property contract
- valid rigid accessory package manifest + humanoid anchor metadata
- traversal/non-FBX/unsafe-id/future-version/invalid-bone/zero-scale/scene-Transform-anchor rejection
- package-local FBX path resolution before Unity import
- canonical package/version installation identity and duplicate-version rejection

These checks are implemented as Unity Editor validation code but have not been executed in the current environment because Unity Editor/runtime execution is unavailable here.

## Deferred evidence

P12 is not runtime/device validated.

Required later evidence includes:

- real VRM wardrobe hierarchy authoring
- outfit/accessory root activation against a loaded UniVRM character
- accessory Transform/humanoid-bone anchor stability during face/body/hand/full-body tracking
- failed authoring validation rollback inside the real Unity Editor
- scene save/reload preservation of explicit bindings
- real imported rigid-FBX accessory rendering/anchor evidence
- skinned-outfit/accessory compatibility checks
- performance/memory behavior with realistic wardrobe counts

## Next P12 source work

- real humanoid Animator/bone anchor verification on loaded VRM characters
- real rigid-FBX package import/render verification
- optional compatible skinned-outfit compatibility checks
- richer appearance preset preview before activation
- interactive transition dependency graph editing/grouping
- additional motion adapters beyond BVH when a concrete format/import contract is justified
- visual event/node tooling and richer environment/prop/effect automation

