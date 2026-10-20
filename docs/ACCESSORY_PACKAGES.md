# Accessory Packages

P12 introduces a declarative accessory-package format for importing rigid accessories into the one-character appearance workflow.

This format is intentionally narrow. Version 1 supports a single rigid FBX accessory and optional humanoid-bone anchor metadata. It does not load scripts, DLLs, prefabs, arbitrary Unity serialized objects, or skinned meshes.

## Package layout

A package is a directory selected through its JSON manifest.

Example:

```text
my-hat/
  manifest.json
  models/
    hat.fbx
```

The model path in the manifest is always relative to the manifest directory.

## Manifest version 1

```json
{
  "FormatVersion": 1,
  "PackageId": "creator.example.hat",
  "PackageVersion": "1.0.0",
  "SlotId": "Head",
  "AccessoryId": "ExampleHat",
  "ModelFile": "models/hat.fbx",
  "AnchorMode": "HumanoidBone",
  "HumanoidBone": "Head",
  "LocalPosition": {
    "x": 0.0,
    "y": 0.02,
    "z": 0.0
  },
  "LocalEulerAngles": {
    "x": 0.0,
    "y": 0.0,
    "z": 0.0
  },
  "OverrideLocalScale": false,
  "LocalScale": {
    "x": 1.0,
    "y": 1.0,
    "z": 1.0
  },
  "RestoreOriginalTransformWhenInactive": true
}
```

### Required fields

- `FormatVersion`: must equal `1`
- `PackageId`: safe logical package id
- `PackageVersion`: non-empty version string
- `SlotId`: target appearance slot id
- `AccessoryId`: logical accessory id
- `ModelFile`: canonical relative path to one `.fbx`

Safe logical ids use letters, digits, `.`, `_`, and `-`.

### AnchorMode

Version 1 accepts:

- `None`
- `HumanoidBone`

`HumanoidBone` requires a valid Unity `HumanBodyBones` name such as `Head`, `LeftHand`, or `RightHand`.

External packages cannot name an arbitrary scene Transform. Explicit Transform anchors remain scene-authoring data because they are references to the currently loaded character/scene.

## Import flow

Use:

```text
VCR > P12 > Open Appearance Authoring
```

Then choose `Import Accessory Package`.

The importer:

1. parses and validates the manifest,
2. rejects unsafe/path-traversal model paths,
3. resolves the FBX strictly inside the package directory,
4. enforces a 256 MiB model-size ceiling,
5. copies the manifest and FBX into `Assets/VCR/ImportedAccessories` or the configured destination,
6. imports the FBX through Unity,
7. requires a `GameObject` with at least one rigid `MeshRenderer`,
8. rejects any `SkinnedMeshRenderer`,
9. resolves requested humanoid-bone anchors against the selected appearance runtime,
10. instantiates the imported model into the authoring scene,
11. adds an explicit accessory binding,
12. immediately validates the complete appearance configuration through `RebuildConfiguration`, and
13. commits the import only when the full asset + scene + runtime-registration transaction succeeds.

Imported scene instances are initially inactive so importing a package does not silently replace the current appearance. The importer refuses to run while unrelated appearance edits are pending; those edits must be handled with `Validate & Apply` first.

## Safety and compatibility rules

The v1 importer fails closed for:

- unsupported manifest versions
- unsafe package/slot/accessory ids
- absolute paths or `..` traversal
- non-FBX model payloads
- missing/empty/oversized model files
- FBX import failure
- FBX without a rigid `MeshRenderer`
- any `SkinnedMeshRenderer`
- missing humanoid Animator/bone when `HumanoidBone` anchoring is requested
- duplicate `SlotId/AccessoryId` binding in the active authoring runtime
- an already installed identical `PackageId + PackageVersion`
- runtime configuration failure after scene binding creation

A package version has one canonical destination under `Assets/VCR/ImportedAccessories`. Re-importing the same package/version is rejected instead of producing `-2`, `-3`, and similar duplicate installs. If asset import, scene registration, or runtime registration fails, the imported folder, created scene instance, and serialized binding changes are rolled back.

## Why skinned accessories are rejected

A skinned accessory can depend on skeleton topology, bind poses, bone names, bone order, rest pose, avatar scale, and renderer-specific data. Accepting it as a generic accessory without compatibility validation can deform incorrectly or corrupt the intended attachment behavior.

Compatible skinned-outfit/accessory packages therefore remain a separate P12 task with explicit skeleton compatibility checks.

## Runtime anchor behavior

Scene-authored accessory bindings support:

- no anchor
- explicit Transform anchor
- humanoid-bone anchor
- local position/rotation offset
- optional local scale override
- restore-original-transform-when-inactive

Appearance switching snapshots both active state and accessory Transform state. If anchor application or any appearance activation fails, the previous active states and Transform hierarchy/local transforms are restored.

## Validation status

Source validation covers manifest security, version checks, path rules, portable anchor metadata, scale validation, package-path resolution, and canonical duplicate-version rejection.

Actual FBX import, real VRM humanoid-bone attachment, rendering, tracking stability, and memory/performance behavior require Unity Editor/runtime evidence and are not marked PASS by source inspection alone.
