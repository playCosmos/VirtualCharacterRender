# M3 dependency, license and security acceptance ledger

**Current state: UNAPPROVED.** Inventory and source pin checks are automated,
but this document is not an assertion that redistribution is permitted.

## Reproducible source-level inventory

~~~bash
python3 tools/audit-third-party-dependencies.py --output dependency-inventory.json
python3 tools/generate-m3-sbom.py --output m3-source-sbom.cdx.json
# Also inspect final shipped binaries from the exact SOURCE-MATCHED,
# independently downloaded ZIPs and build-provenance.json:
python3 tools/generate-m3-payload-inventory.py \
  --artifacts /path/to/final-release-archives \
  --version 0.1.0-alpha.3 \
  --source EXACT_40_HEX_REVIEWED_COMMIT \
  --output m3-shipped-payload-inventory.json
~~~

The first report records direct and transitive Unity packages and pinned
MediaPipe/Ultraleap/UniWinC/Pretendard download inputs. The second report is a
**partial CycloneDX 1.6** inventory, explicitly labeled
`PARTIAL_SOURCE_PIN_INVENTORY_ONLY` and `license-audit=NOT_APPROVED`.
CI retains both source reports as review artifacts. They are not a binary
software bill of materials and contain **no guessed SPDX license IDs**.

The separate shipped-payload inventory enumerates and hashes **every file
and symlink inside the two verified ZIPs**. Its raw path, SHA-256, size,
file category and `PENDING_MANUAL_REVIEW` license metadata allow a reviewer
to match the exact final release to a vendor/license and required notice.
It still cannot infer ownership or grant a redistribution license. For
new development-alpha releases the CI publishing workflow preserves this
report as a separate restricted-review Actions artifact rather than adding
unsupported certification claims to the publicly distributed ZIPs.
**Regenerate after final signing/stapling**, because bytes and signatures
change from the unsigned development alpha.

## Exact-shipped-file THIRD_PARTY_NOTICES generation

The repository contains **one** bundled Pretendard license notice at
`unity/Assets/VCR/ThirdParty/Pretendard/LICENSE.txt`. That is evidence for
font notice text only; it does not authorize guessing license IDs for the
other Unity/native/model components.

The immutable alpha workflow retains a **pending, per-file review template**
(`m3-license-review-PENDING.json`) as a private Actions review artifact.
For a locally downloaded and independently verified final distribution,
generate an equivalent template with:

~~~bash
python3 tools/build-m3-notices.py \
  --artifacts /path/to/final-release-archives \
  --version 0.1.0-alpha.3 \
  --source EXACT_40_HEX_REVIEWED_COMMIT \
  --template m3-license-review-PENDING.json
~~~

A qualified reviewer must evaluate **every** row against the actual shipped
file and record exact archive SHA-256, payload SHA-256, component owner,
reviewer/date and an explicit `FIRST_PARTY` or `THIRD_PARTY_NOTICE`
decision. Third-party rows additionally require a verified SPDX-style license
identifier, relative `noticePath`, and SHA-256 of the exact UTF-8 notice
text in an operator-controlled review folder. The generator does **not**
automatically guess component owners or license grants.

When all review evidence is available, run:

~~~bash
python3 tools/build-m3-notices.py \
  --artifacts /path/to/final-release-archives \
  --version 0.1.0-alpha.3 \
  --source EXACT_40_HEX_REVIEWED_COMMIT \
  --review m3-license-review-REVIEWED.json \
  --notices-root /path/to/reviewed-license-text \
  --output THIRD_PARTY_NOTICES.txt
~~~

Pending rows, duplicate/missing files, changed archives, incorrect payload
digests, absent/modified license text, unsafe notice paths, or absent reviewers
**block notice generation**. Successful notice generation validates the
mechanics of reviewed declarations, **not legal redistribution approval**.
Re-run against the signed/stapled final ZIP, not the unsigned development
alpha; its bytes may differ.

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
