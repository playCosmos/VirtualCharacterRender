# Runtime configuration recovery

The desktop application stores its own `vcr-runtime-config.json` under
Unity's platform-specific `Application.persistentDataPath`, unless
`--vcr-config=<path>` explicitly selects another file. The effective
path is available through `ApplicationRuntimeBootstrap.ConfigurationPath`.

## Normal save

- A bounded (4 MiB), strict UTF-8 JSON envelope is written to a temporary
  sibling and atomically substituted for the previous file.
- After **at least two successful saves**, a single
  `vcr-runtime-config.json.bak` generation is retained beside the primary
  file. The first save has no older generation to protect.
- A successful save does not mean that every model, tracker, or external
  device is available; it only persists current application configuration.

## Corruption recovery

When the primary configuration exists but cannot be loaded, and is not a
known *newer* schema version, the application attempts to load the previous
`.bak` using the same UTF-8 size, JSON and schema checks.

On successful fallback:

1. The application uses the saved older generation; it does **not** rewrite
   or delete the unreadable primary automatically.
2. It writes a warning to Player.log; the explicit **Reload configuration**
   action displays a recovery notice in the UI.
3. An explicit **Save configuration** atomically writes the recovered/current
   values and refreshes the backup from valid JSON (avoiding a corrupt
   generation in the backup slot).

A primary envelope with a schema version newer than the application supports
is **not** silently downgraded from the old backup. Install a compatible
application version or handle migration explicitly. If both the primary and
backup fail validation, loading fails with a diagnostic rather than falsely
claiming recovery; startup can continue with baseline defaults subject to
the scene's existing failure handling.

## Operator procedure

1. Record the application version, the configured path and any warning.
2. **Before resetting anything**, copy the primary JSON and the `.bak`
   file to a safe private location. Both may contain local model paths and
   other identifying settings; never attach them publicly without review.
3. If automatic fallback succeeded, validate your render/tracking/output
   settings and explicitly save to regenerate a valid primary and backup.
4. If neither file can be loaded, close the application, retain a copy,
   move the corrupted configuration aside (do not delete it), and relaunch
   with a clean default; reconfigure and save only after confirming behavior.
5. Do not hand-edit the schema `Version` field to force acceptance.

## Verification

`P1RendererCoreValidation` covers initial save/load, retained previous
generation, corrupt JSON recovery without clobbering original bytes,
recovery-after-another-save, and rejection of a future schema even when a
valid older backup exists. This is **source-free Editor evidence**, not proof
of filesystem permissions, antivirus interaction, power-loss safety, or
successful recovery on every real Windows/macOS volume.
