# Privacy, diagnostics and customer support — M3

Status: pre-release practices and operator instructions. This document is
**not** a completed legal/privacy audit, a compliance certification or a
promise that all optional third-party endpoints have been tested on hardware.

## What this application processes

VirtualCharacterRender renders a locally selected VRM 0.x/1.0 character and
may use user-enabled webcam/face/body/hand tracking, ARKit/iFacialMocap, VMC,
OSC or Ultraleap sources. Depending on enabled features, it can handle camera
frames, motion/expression tracking, model/asset paths, optional event/network
messages, and local app/window/capture settings.

- It can run a one-character session without a model, camera or external
  tracking device. Optional sources should remain disabled until configured.
- Camera/tracking and local network/protocol integrations serve the requested
  character-rendering features. Do not expose a listener to public/untrusted
  networks without authentication/segmentation review.
- OBS or other screen-capture applications are **separate software**. Their
  recording, streaming and cloud-service privacy settings are not controlled
  by VirtualCharacterRender.
- Third-party Unity/native packages have distinct license and security
  properties. The dependency inventory is not consent or legal clearance.

## Local storage and diagnostics

Unity `Application.persistentDataPath` may contain configuration JSON and
backup, system/frame/tracking diagnostics, saved snapshots, and log/evidence
files. Explicit GUI evidence options write to paths chosen by the operator;
M2 stores frame/memory CSV, session metadata and normalized event CSV **only**
under an explicit standalone command-line option. An intentionally started
network source may transmit or receive its configured protocol messages.
A future update or external service would require its own disclosure and
review; do not assume all network adapters are inactive after enabling one.

The current documented application behavior is **no automatic upload of
camera imagery, VRM models or collected diagnostics by the M2/P10 toolchain**.
This source-level statement is not a replacement for an independent network
inspection on a clean physical machine before commercial release.

Private values can appear in apparently harmless evidence: account names and
absolute home directories, VRM filenames and metadata, OS/CPU/GPU identifiers,
IP addresses, source IDs, screenshot windows, application logs, minidumps,
transcripts, scene names and debugging settings.

## Before sharing a support ZIP

1. Ask for explicit consent for the specific diagnostic transfer and explain
   why it is needed; record recipient and retention/deletion agreement.
2. Prefer a short reproduction description and non-sensitive error excerpt
   over sending the full environment, private VRM, raw camera frames, entire
   home directory or native memory dump.
3. Inspect each file locally. Remove or redact home paths, login names,
   IPs/ports, chat/event payloads, tokens, customer model filenames and
   identifiable video/audio. **Redaction is not guaranteed by a ZIP filename,
   checksum, or automatic CSV verifier.**
4. Never include signing keys, PFX/keystore passwords, cloud credentials,
   private software license keys or an unreviewed crash dump.
5. Preserve original diagnostic files privately for reproducibility if
   authorized, but upload only approved redacted copies over an agreed channel.
6. Customer support should use a narrowly scoped issue ID, verified build
   SHA, reproduction steps, OS version, and expected/actual behavior. Request
   more only when technically necessary.

There is no reason to grant remote desktop, upload a private avatar, disable
OS security or share signing credentials for routine basic support.

## Safe startup/troubleshooting sequence

1. Confirm source/build version and whether the issue reproduces with **no
   loaded model and no camera/tracking/OBS**. Preserve configuration files
   before a reset.
2. Test plain render/quit; then add one real VRM, one input source, one OBS
   capture, in that order, stopping at the first reproducible change.
3. For slow shutdown, capture bounded local timing logs and native collector
   output. Differentiate app cleanup, native teardown, compositor shutdown
   and the external capture process. Do not blame Ultraleap without evidence.
4. For bad/corrupted configuration, use
   [CONFIGURATION_RECOVERY.md](CONFIGURATION_RECOVERY.md); the normal quit
   guard prevents silently replacing unrecovered settings.
5. For transparent output, test native OS/window composition and OBS alpha
   separately. A `captureReady` flag is not evidence of correct alpha pixels.
6. For model failures, collect the VRM version and privacy-preserving
   model fingerprint if allowed, rendering backend, Unity version and
   error text; do not ask for copyrighted model bytes by default.
7. For tracking loss, check requested source permissions and connection
   transitions, normalize source-loss vs subject-loss, and separately
   verify physical/protocol reconnect where required.
8. If a problem is reproducible, open an issue with redacted logs and exact
   test environment. Respect customer-provided confidentiality restrictions.

## Security and retention gates before commercial distribution

The operator must verify firewall/network listeners when inactive and when
each source is enabled; external file path validation and size limits;
unsupported shader package fallback; arbitrary executable plugins disabled;
no credentials in packaged files or logs; OS camera permissions; local data
storage/access permissions; diagnostic ZIP review; support access control,
deletion/retention and incident response procedure. None of these items are
assumed PASS merely because the source-free tests are green.

## Known current release limitations

- Initial release targets **one active VRM character** on Windows x64 and
  Apple Silicon macOS; 2D SDK integration and executable third-party plugin
  workflows remain out of the first commercial gate.
- Real VRM 0.x/1.0 model diversity, webcam/ARKit/Ultraleap/VMC device behavior,
  OBS transparency and 4h endurance remain hardware evidence gates.
- Development alpha archives are **not yet certified signed/notarized**
  production distributions; stable 720p60/1080p60 is conditional on published
  real hardware and reference scene/model testing.
- Keyboard navigation, localization, high-DPI and native window Settings
  behavior require final clean-account GUI tests on both OSes.

No personal data is automatically deleted by uninstalling a portable app
directory: model files, recordings and persistent app data are independently
owned by the user and must be handled explicitly.
