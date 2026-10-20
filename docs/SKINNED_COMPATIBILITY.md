# Skinned Compatibility Analysis

P12 includes a structural compatibility analyzer for evaluating a skinned accessory/outfit against the active humanoid target before any automatic rebinding is considered.

Menu:

```text
VCR > P12 > Open Skinned Compatibility
```

The tool accepts:

- one source `SkinnedMeshRenderer`
- one target humanoid `Animator`

It does not modify the renderer, bones, bind poses, materials, or avatar.

## What the analyzer checks

The analyzer verifies:

- source mesh exists
- source bone list is non-empty
- source bindpose count equals source bone count
- every bindpose matrix is finite and non-singular
- every source bone can map to one target bone
- humanoid-bone identity is preferred when the source has a valid humanoid Animator
- non-humanoid/extra bones may fall back to unique exact-name mapping
- ambiguous target bone names are rejected
- missing target bones are rejected
- mapped source parent relationships remain ancestors of mapped target children
- source root bone is mapped

The report lists each source → target bone mapping and whether the mapping used humanoid identity or exact-name fallback.

## Result semantics

A successful report may say:

```text
Structurally compatible
```

This means only that the skeleton structure is plausible enough for further evaluation.

It does **not** prove:

- bind-pose equivalence
- garment fit
- correct deformation
- lack of clipping
- avatar-scale compatibility
- correct blendshapes
- correct material/shader behavior
- stability while tracking
- performance suitability

Every structurally compatible result therefore keeps `RequiresBindPosePreview = true`.

## Why automatic rebind is still disabled

A skinned mesh stores bind poses authored against a particular skeleton/rest pose. Two humanoid skeletons can expose the same logical bones while still differing in:

- rest pose
- proportions
- local bone orientation
- hierarchy helper bones
- scale
- bind matrices

Replacing `SkinnedMeshRenderer.bones` solely because names or humanoid mappings match can produce severe deformation.

P12 therefore separates:

1. structural compatibility analysis,
2. non-destructive target-skeleton rebind preview,
3. real target-avatar visual/deformation evidence,
4. only then, a future explicit accepted rebind/import action.

## Non-destructive rebind preview

After a structural pass, the compatibility window can create a temporary preview renderer.

The preview:

- creates a new `[VCR Preview] ...` GameObject
- uses `DontSaveInEditor | DontSaveInBuild`
- reuses the source mesh/materials without modifying them
- maps the preview renderer's bone array/root bone to the analyzed target mapping
- copies source blendshape weights
- is destroyed when removed, re-analyzed, or the window closes
- never changes the source `SkinnedMeshRenderer.bones`, root bone, mesh, materials, or bind poses

The preview also reports average/max current-pose bind-matrix delta. This is diagnostic evidence only. A low value does not prove acceptable fit and a high value does not by itself define an automatic rejection threshold; actual target-avatar visual/deformation evidence remains required.

There is intentionally no `Accept/Rebind` commit button yet.

## Source validation

P12 source validation covers:

- matching exact-name skeletons
- missing target bone rejection
- ambiguous target bone-name rejection
- bindpose/bone-count mismatch rejection
- mandatory bind-pose-preview flag on structurally compatible results
- temporary preview creation on mapped target bones without source-renderer mutation
- preview root-bone/bone-array mapping and DontSave flags
- finite bind-matrix diagnostics
- preview disposal/destruction and incompatible-report rejection

Real humanoid-to-humanoid mapping and visual deformation quality require Unity Editor/runtime evidence with actual VRM/skinned assets.
