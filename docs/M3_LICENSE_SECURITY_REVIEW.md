# M3 dependency, license and security acceptance ledger

**Current state: UNAPPROVED.** Inventory and source pin checks are automated,
but this document is not an assertion that redistribution is permitted.

## Reproducible source-level inventory

~~~bash
python3 tools/audit-third-party-dependencies.py --output dependency-inventory.json
python3 tools/generate-m3-sbom.py --output m3-source-sbom.cdx.json
~~~

The first report records direct and transitive Unity packages and pinned
MediaPipe/Ultraleap/UniWinC/Pretendard download inputs. The second report is a
**partial CycloneDX 1.6** inventory, explicitly labeled
`PARTIAL_SOURCE_PIN_INVENTORY_ONLY` and `license-audit=NOT_APPROVED`.
CI retains both reports as review artifacts. They are not a binary
software bill of materials and contain **no guessed SPDX license IDs**.

## Redistribution review checklist — all entries require approval

| Software/asset family | Current origin | Human verification required | Status |
| --- | --- | --- | --- |
| Unity Editor + Player runtime | Unity 6000.3.25f1 and builtin modules | Unity commercial/editor/player entitlement and distribution restrictions | PENDING |
| Unity URP, ShaderGraph and transitive packages | locked Unity package versions | Bundle license files, source/object shipping and attribution | PENDING |
| UniVRM / UniGLTF / VRM / VRM10 | three tag-pinned v0.131.2 Git packages | Source notices, shader/subpackage licenses and redistribution requirements | PENDING |
| MediaPipe Unity plugin and native/model package | pinned 0.16.3 local tarball; SHA256 bootstrap | License, model/data licenses, native binary/ML dependency inventory and notices | PENDING |
| Ultraleap Unity adapter/native libraries | pinned vendor source bootstrap | SDK license and redistribution terms, embedded LeapC/runtime binaries, user permissions | PENDING |
| UniWinC/UniWindowController | pinned vendor source bootstrap | Native implementation license, binary dependencies, OS restrictions and attribution | PENDING |
| Pretendard fonts | 3 pinned blobs, license file under Assets | Exact OFL license/notice compliance, embedding and packaging requirements | PENDING |
| Project-owned AppKit settings window and native outputs | compiled from project sources | Validate no third-party binary redistributions or unintended notarization entitlements | PENDING |
| User/third-party VRM models, textures, animations | user-selected/local or bundled samples | **Per-asset** author license, attribution, commercial use and optional streaming/derivatives | PENDING |
| OBS and optional external software | separate user-provided applications | Do not imply bundled OBS license or user footage ownership | PENDING |
| Native macOS/Windows plugins and transitive libraries | actual final archive | Independently enumerate every shipped DLL, dylib, framework and EXE; verify exact hashes/signatures/notices | PENDING |

No row can be marked PASS from a package name alone. Record license text
location, version/hash, reviewer, date, conditions, required notices and
the **exact final distribution** into a non-secret restricted release
ledger. If a license cannot be established, block packaging that item.

## Security review: real tests required

- **Runtime input:** verify absolute/relative VRM and custom asset paths,
  extension/content limits, damaged/future formats, resource caps and rollback.
  An arbitrary external shader bundle is data, not permission to load dynamic
  executable plugins, native DLLs or untrusted shell commands.
- **Network:** inspect ARKit/iFacialMocap/VMC/OSC/WebSocket ports and bind
  addresses for default startup and enabled sources. Validate explicit
  activation, firewall rejection, malformed packets, source spoofing and
  disable/cleanup. A normalized tracking source presence signal does not
  authenticate its sender.
- **Camera/tracking:** exercise OS permission denied/revoked, disconnected
  devices, lost frames, reconnect loops and no-source neutral return.
- **File writes:** test settings atomic-save/backup, invalid permissions,
  link/symlink collisions, support bundles, local data retention, private
  absolute paths in logs, and no unannounced upload.
- **Executable supply chain:** verify exact source SHA and final archive
  hashes, dependencies and native binary signatures. Signing a top-level
  app does not approve the provenance/legal status of every nested binary.
- **Privacy & accessibility:** inspect log/model/capture disclosure;
  [M3_PRIVACY_SUPPORT.md](M3_PRIVACY_SUPPORT.md) governs support collection.
  Validate keyboard, accessibility and localization on both systems.

## Approval separation

- S: locked packages, generated partial CycloneDX, deterministic lint/tests.
- N: reproducible native binaries and clean startup/quit.
- G: real source/network/device/OBS, clean accounts, user permission prompts.
- R: signed/stapled final artifacts, license review and install/rollback.

M3 remains **DEFERRED** until the G/R ledger is completed. No automatic
source inventory generator can make an intellectual-property or legal
redistribution approval decision.
