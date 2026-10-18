# M3 operator handbook — signed Windows/macOS distribution

Status: **implementation/preflight available; distribution approval DEFERRED**.
Scope: one-character desktop product; signed Windows x64 and Developer ID notarized
Apple Silicon macOS arm64. This is the action-oriented supplement to
[M3_DISTRIBUTION_PLAN.md](M3_DISTRIBUTION_PLAN.md).

## What current CI does *not* do

The existing alpha workflow publishes **unsigned development-alpha** ZIPs
without production signing/notarization. A reproducible Unity build, successful
source verification, source-bound checksum or macOS AppKit compilation does
**not** authorize telling users they have signed production packages.

Do not enable automatic production publishing from a generic feature branch.
Do not insert signing keys, notary credentials or PFX passwords into scripts,
GitHub comments, diagnostics, repository contents or command examples.

## A. Audit independently downloaded source-bound artifacts

Download the **two ZIPs**, `SHA256SUMS.txt`, and
`build-provenance.json` from one *specific* immutable release to a fresh
directory, independently rehash and verify:

~~~bash
python3 tools/verify-m3-distribution.py \
  --artifacts /path/to/release-files \
  --version 0.1.0-alpha.3 \
  --source EXACT_40_HEX_REVIEWED_COMMIT \
  --report /path/to/private-m3-preflight.json
~~~

The report may say only `SOURCE_BOUND_ARCHIVES_VERIFIED_ONLY`.
It compares the supplied source assertion and producer's release manifest,
rehashes both ZIPs, rejects duplicate/unsafe ZIP members and inspects basic
Windows/macOS bundle structure. **It does not establish trusted publisher
identity or determine whether a ZIP is signed, notarized or installable.**
The archive manifests are statements made by the producer; review exact
Git commit, Actions workflow and source independently.

**Signing changes artifact bytes**. After signing and stapling, repackage the
final binaries, generate NEW SHA256SUMS/provenance for the final archives and
verify again. Never reuse unsigned ZIP checksums or source assertions as
evidence of signed binary integrity.

## B. Windows x64 signing (operator-controlled credentials)

On the designated Windows signing workstation/runner:

1. Start from a source-matched unsigned build. Verify every packaged Windows
   executable and relevant project-owned native binary; inventory SDK/native
   redistributables and their own upstream signatures/licenses. Do not
   overwrite original distributable or publish an unsigned installer as signed.
2. Use an organizational Authenticode code-signing certificate managed by
   hardware-backed provider/approved key service; protect signing-key access.
   Use Windows SDK `signtool` with SHA-256 and an approved RFC3161 timestamp
   service. Conceptual command (substitute an approved real endpoint):

~~~powershell
signtool sign /fd SHA256 /tr https://YOUR-APPROVED-RFC3161-SERVICE /td SHA256 /a "VirtualCharacterRender.exe"
signtool verify /pa /v "VirtualCharacterRender.exe"
python tools/verify-m3-native-signatures.py --platform windows --path "VirtualCharacterRender.exe" --archive "final-signed-windows.zip" --archive-sha256 YOUR_INDEPENDENTLY_REHASHED_FINAL_ZIP_SHA256 --expected-windows-thumbprint YOUR_APPROVED_CERT_SHA1_THUMBPRINT
~~~

3. The approved Windows certificate thumbprint and Apple Developer Team ID must
be obtained from the publisher's independently reviewed signing policy,
not copied from a potentially untrusted downloaded binary.

The native verifier requires a `Valid` Authenticode signature and observed
   timestamp certificate, plus an exact member-by-member match against the
   final **already signed** distribution ZIP specified by `--archive`.
   The example ZIP must actually exist and contain the signed EXE; use
   fresh final-archive hashes, not unsigned-alpha hashes. Record the expected publisher and certificate
   thumbprint separately in private release approval. A valid signature
   does **not** guarantee Microsoft SmartScreen reputation.
4. Repackage the **signed** ZIP/installer, hash the final bytes, install on a
   clean Windows x64 account with no Unity/Python/SDK, test first-run prompts,
   focus/overlay, permissions, uninstall and app-data retention.

Microsoft's [SignTool guidance](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
and [timestamping best practices](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures)
are normative.

## C. Apple Silicon macOS Developer ID and notarization

On a controlled macOS signing host with an active Apple Developer ID
Application identity, perform nested-code signing **from the innermost
framework/dylib outward**, then sign the containing `.app`. Don't apply
`--deep` as a substitute for deliberate signing of nested code. Enable
hardened runtime; grant only justified entitlements required by native
plugins/camera access. Unity bundle settings/entitlements must be reviewed
on the actual built application.

Representative final-app operations (credential and identity names are
operator-provided; do not paste sensitive key material):

~~~bash
codesign --force --options runtime --timestamp \
  --sign "Developer ID Application: YOUR ORGANIZATION (TEAMID)" \
  "VirtualCharacterRender.app"
codesign --verify --strict --deep --verbose=2 "VirtualCharacterRender.app"
ditto -c -k --keepParent "VirtualCharacterRender.app" "VCR-notarization.zip"
xcrun notarytool submit "VCR-notarization.zip" --keychain-profile "VCR_NOTARY_PROFILE" --wait
# Only if notarization returned Accepted:
xcrun stapler staple "VirtualCharacterRender.app"
python3 tools/verify-m3-native-signatures.py \
  --platform macos --path "VirtualCharacterRender.app" \
  --archive "final-stapled-macos.zip" \
  --archive-sha256 YOUR_INDEPENDENTLY_REHASHED_FINAL_ZIP_SHA256 \
  --expected-macos-team-id YOUR_TEAM_ID
~~~

The verifier independently checks that each member in the final ZIP matches
the final stapled app's files or symlink targets, then invokes `codesign`,
`lipo`, `stapler validate` and `spctl --assess` on that same app.
**Create the final ZIP after stapling, before running this command**; do not
reuse the intermediate notarization-upload ZIP.
The verifier requires arm64,
Developer ID Application and hardened runtime. A notarization ticket is
not equivalent to a verified GUI application launch or correct camera/OBS
permission prompts. Repackage the **stapled** app (preserving bundle symlinks
and permissions), rehash, then test a fresh non-developer Apple Silicon
account after downloading through the normal distribution path.

Apple [notarization documentation](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
and [hardened runtime requirements](https://developer.apple.com/documentation/security/hardened-runtime)
are normative. `altool` is not supported for current Apple notarization.

## D. Portable ZIP installation, rollback and removal

Current packaging is a portable ZIP and macOS `.app`, not a Windows MSI or
a signed package manager installer. Do not claim an installer exists.

- Windows: extract ZIP into a dedicated directory (not directly into a system
  directory). Launch `VirtualCharacterRender.exe`; review Windows
  firewall/SmartScreen prompts and validate real transparent output. Never
  run multiple versions against the same settings files concurrently.
- macOS: extract `.app` preserving symlinks and permissions; move to
  `/Applications` only after user approval. Verify Gatekeeper launch on a
  fresh local account without bypassing macOS security controls.
- Application settings, recent VRM paths and diagnostic snapshots live under
  Unity `Application.persistentDataPath`, separate from the portable bundle.
  Its concrete resolved path must be captured on each supported OS. The
  versioned JSON backup/recovery rules are in
  [CONFIGURATION_RECOVERY.md](CONFIGURATION_RECOVERY.md).
- Upgrade: close app, retain a private copy of current JSON + `.bak`,
  record exact source/binary SHA, unpack NEW version in a different folder,
  launch, verify settings recovery, VRM, overlay and optional input devices.
  Never overwrite a running app or delete settings on upgrade.
- Rollback: close new version; keep source-matched prior signed release and
  independently copied settings. Verify the configuration schema of the
  older version; restore the prior JSON/backup only if safe. No silent
  downgrades from future schemas.
- Uninstall: delete the portable app/bundle **only**. Do not silently delete
  user settings, models, camera logs, private evidence or backups. Provide
  a separate explicit user-reviewed data deletion procedure if requested.
- Clean-profile test: use a separate fresh OS account rather than wiping
  current user's working model/diagnostic data. Test corrupt-config recovery,
  wrong permissions, no webcam, no OBS, firewall denied, VRM missing, and
  startup/quit with Ultraleap disconnected.

## E. Mandatory release acceptance record

Record without publishing private machine identifiers: reviewed commit,
target version, final ZIP SHA256s and provenance, signer thumbprint/team ID,
timestamp and Apple notary ticket result, native verification output,
OS/build/architecture, clean-account install, permissions, upgrade/rollback,
uninstall/data preservation, VRM/OBS/tracking G-level evidence, license audit,
privacy review, localization/keyboard/DPI, and support reproductions.

`verify-m3-native-signatures.py` produces a narrowly scoped native verification
record, **never** `distributionAcceptance=true`. M3 is FAIL/DEFERRED unless
all mandatory operator R/G evidence is present. Do not merge, retag, or
publish a production release from this document alone.
