#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UNITY = ROOT / "unity"
VCR = UNITY / "Assets" / "VCR"

parser = argparse.ArgumentParser(
    description="Validate repository structure and Unity project reproducibility evidence."
)
parser.add_argument(
    "--strict-reproducibility",
    action="store_true",
    help=(
        "Treat missing Unity-generated reproducibility files as errors. "
        "Use this for release/promotion gates after opening the project with the pinned editor."
    ),
)
args = parser.parse_args()

errors: list[str] = []
warnings: list[str] = []


def require(path: Path, label: str | None = None) -> None:
    if not path.is_file():
        errors.append(f"missing required file: {label or path.relative_to(ROOT)}")


def load_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        errors.append(f"invalid JSON: {path.relative_to(ROOT)}: {exc}")
        return {}


require(UNITY / "ProjectSettings" / "ProjectVersion.txt")
require(UNITY / "Packages" / "manifest.json")
require(ROOT / "tools" / "bootstrap-mediapipe.sh")
require(ROOT / "tools" / "bootstrap-mediapipe.ps1")

required_latest_chain = [
    VCR / "Editor" / "P11" / "P11BatchValidation.cs",
    VCR / "Editor" / "P11" / "VCR.Editor.P11.asmdef",
    VCR / "Editor" / "P12" / "P12BatchValidation.cs",
    VCR / "Editor" / "P12" / "VCR.Editor.P12.asmdef",
    VCR / "Editor" / "P13" / "P13BatchValidation.cs",
    VCR / "Editor" / "P13" / "VCR.Editor.P13.asmdef",
    VCR / "Runtime" / "Tracking" / "SnapshotArrayOwnership.cs",
    VCR / "Runtime" / "Tracking" / "BorrowedHumanoidPose.cs",
    VCR / "Runtime" / "Tracking" / "MediaPipe" / "PendingSubmissionTracker.cs",
    VCR / "Runtime" / "Protocols" / "Vmc" / "VmcPacketReader.cs",
    ROOT / "tools" / "validate-p12-source-free.sh",
    ROOT / "tools" / "validate-p12-source-free.ps1",
    ROOT / "tools" / "validate-p13-source-free.sh",
    ROOT / "tools" / "validate-p13-source-free.ps1",
]
for path in required_latest_chain:
    require(path)


def require_source_contains(path: Path, token: str, label: str) -> None:
    if not path.is_file():
        return

    source = path.read_text(encoding="utf-8", errors="replace")
    if token not in source:
        errors.append(
            f"hot-path source contract missing: {label}: {path.relative_to(ROOT)}"
        )


def forbid_source_pattern(path: Path, pattern: str, label: str) -> None:
    if not path.is_file():
        return

    source = path.read_text(encoding="utf-8", errors="replace")
    if re.search(pattern, source, flags=re.MULTILINE):
        errors.append(
            f"hot-path source regression: {label}: {path.relative_to(ROOT)}"
        )


def require_source_order(
    path: Path,
    first: str,
    second: str,
    label: str,
) -> None:
    if not path.is_file():
        return

    source = path.read_text(encoding="utf-8", errors="replace")
    first_index = source.find(first)
    second_index = source.find(second)
    if (
        first_index < 0
        or second_index < 0
        or first_index >= second_index
    ):
        errors.append(
            f"source ordering contract missing: {label}: {path.relative_to(ROOT)}"
        )


osc_writer = VCR / "Runtime" / "Protocols" / "Osc" / "OscPacketWriter.cs"
require_source_contains(
    osc_writer,
    "TryAppendBundleMessage",
    "OSC writer must retain reusable-buffer bundle append support",
)
forbid_source_pattern(
    osc_writer,
    r"\bMemoryStream\b",
    "OSC writer must not reintroduce MemoryStream staging",
)
forbid_source_pattern(
    osc_writer,
    r"\bArrayPool\s*<",
    "OSC writer must not require transient pooled encoding buffers",
)

vmc_sender = VCR / "Runtime" / "Protocols" / "VmcUnity" / "VmcUdpSender.cs"
require_source_contains(
    vmc_sender,
    "TryAppendBundleMessage",
    "VMC sender must write directly into its reusable OSC packet buffer",
)
require_source_contains(
    vmc_sender,
    "ISelectiveNormalizedMotionSnapshotProvider",
    "VMC sender must use selective snapshot capture when the provider supports it",
)
require_source_contains(
    vmc_sender,
    "includeExpressions:",
    "VMC selective capture must propagate the expression-send setting",
)
require_source_contains(
    vmc_sender,
    "IBorrowedHumanoidPoseProvider",
    "VMC sender must retain pose-only borrowed compatibility",
)
require_source_contains(
    vmc_sender,
    "IBorrowedNormalizedMotionProvider",
    "VMC sender must prefer combined borrowed motion when available",
)
require_source_contains(
    vmc_sender,
    "protocol.vmc.send.borrowed_motion_packets",
    "VMC borrowed-motion usage must remain observable in runtime metrics",
)
require_source_order(
    vmc_sender,
    "IBorrowedNormalizedMotionProvider borrowedMotionProvider",
    "IBorrowedHumanoidPoseProvider;",
    "VMC sender must evaluate combined borrowed motion before pose-only compatibility",
)
require_source_order(
    vmc_sender,
    "selective.TryCaptureMotion(",
    "borrowedProvider.TryBorrowHumanoidPose(",
    "VMC sender must finish expression capture before borrowing provider-owned pose buffers",
)
forbid_source_pattern(
    vmc_sender,
    r"OscPacketWriter\s*\.\s*WriteBundle\s*\(",
    "VMC sender must not rebuild a bundle from per-message byte arrays",
)

p0_vmc_validation = (
    VCR
    / "Editor"
    / "P0"
    / "P0VmcSetupMenu.cs"
)
require_source_contains(
    p0_vmc_validation,
    "ValidateBorrowedSenderEquivalence",
    "P0 VMC validation must compare borrowed and immutable sender packet bytes",
)
require_source_contains(
    p0_vmc_validation,
    "MakeByRefType()",
    "P0 VMC validation must invoke the borrowed in-parameter serialization overload",
)
require_source_contains(
    p0_vmc_validation,
    "ValidateDirectVmcPacketPath",
    "P0 VMC validation must cover direct packet parity and malformed-packet atomicity",
)

locked_bounded_queues = [
    VCR
    / "Runtime"
    / "Events"
    / "Unity"
    / "NormalizedEventHub.cs",
    VCR
    / "Runtime"
    / "Protocols"
    / "OscEventsUnity"
    / "OscNormalizedEventUdpReceiver.cs",
    VCR
    / "Runtime"
    / "Protocols"
    / "WebSocketUnity"
    / "WebSocketEventClientTransport.cs",
]
for locked_queue in locked_bounded_queues:
    require_source_contains(
        locked_queue,
        "Queue<",
        "bounded Unity ingress queues must use reusable locked Queue<T> storage",
    )
    require_source_contains(
        locked_queue,
        "_queueSync",
        "bounded Unity ingress queues must retain explicit synchronization",
    )
    forbid_source_pattern(
        locked_queue,
        r"\bConcurrentQueue\s*<",
        "lock-protected bounded queues must not reintroduce redundant ConcurrentQueue segment management",
    )

vmc_direct_reader = (
    VCR
    / "Runtime"
    / "Protocols"
    / "Vmc"
    / "VmcPacketReader.cs"
)
require_source_contains(
    vmc_direct_reader,
    "ValidatePacket(",
    "direct VMC parsing must validate a complete packet before state mutation",
)
require_source_contains(
    vmc_direct_reader,
    "ApplyPacket(",
    "direct VMC parsing must retain a separate post-validation apply pass",
)
require_source_order(
    vmc_direct_reader,
    "if (!ValidatePacket(",
    "if (!ApplyPacket(",
    "direct VMC parsing must complete validation before applying packet state",
)
forbid_source_pattern(
    vmc_direct_reader,
    r"new\s+OscMessage\b",
    "direct VMC packet parsing must not allocate transient OscMessage objects",
)
forbid_source_pattern(
    vmc_direct_reader,
    r"new\s+OscArgument\s*\[",
    "direct VMC packet parsing must not allocate transient OSC argument arrays",
)

vmc_udp_receiver = (
    VCR
    / "Runtime"
    / "Protocols"
    / "VmcUnity"
    / "VmcUdpReceiver.cs"
)
require_source_contains(
    vmc_udp_receiver,
    "TryProcessPacket(",
    "VMC UDP receiver must parse directly from its reusable datagram buffer",
)
forbid_source_pattern(
    vmc_udp_receiver,
    r"OscPacketReader\s*\.\s*TryReadMessages",
    "VMC UDP receiver must not rebuild generic OSC message objects per datagram",
)

humanoid_names = (
    VCR
    / "Runtime"
    / "Tracking"
    / "HumanoidBoneNames.cs"
)
require_source_contains(
    humanoid_names,
    "TryParseAscii(",
    "VMC humanoid bone names must support allocation-free byte-span lookup",
)

standard_expression_names = (
    VCR
    / "Runtime"
    / "Tracking"
    / "StandardExpressionNames.cs"
)
require_source_contains(
    standard_expression_names,
    "TryParseAscii(",
    "VMC standard expression names must support allocation-free byte-span lookup",
)

udp_receivers = [
    VCR / "Runtime" / "Protocols" / "VmcUnity" / "VmcUdpReceiver.cs",
    VCR
    / "Runtime"
    / "Protocols"
    / "OscEventsUnity"
    / "OscNormalizedEventUdpReceiver.cs",
    VCR
    / "Runtime"
    / "Tracking"
    / "ArKitUnity"
    / "IFacialMocapUdpReceiver.cs",
]
for udp_receiver in udp_receivers:
    require_source_contains(
        udp_receiver,
        "ReceiveFrom(",
        "UDP receive hot paths must reuse caller-owned datagram buffers",
    )
    forbid_source_pattern(
        udp_receiver,
        r"\b(?:receiver|_receiver)\s*\.\s*Receive\s*\(",
        "UDP receive hot paths must not allocate one byte array per datagram",
    )

mediapipe_submission_tracker = (
    VCR
    / "Runtime"
    / "Tracking"
    / "MediaPipe"
    / "PendingSubmissionTracker.cs"
)
require_source_contains(
    mediapipe_submission_tracker,
    "_submissionOrder",
    "MediaPipe submission correlation must retain fixed-ring history",
)
require_source_contains(
    mediapipe_submission_tracker,
    "_evictionCount",
    "MediaPipe pending timestamp eviction must remain observable",
)

mediapipe_submission_sources = [
    VCR / "Runtime" / "Tracking" / "MediaPipe" / "MediaPipeFaceSource.cs",
    VCR / "Runtime" / "Tracking" / "MediaPipe" / "MediaPipeHolisticSource.cs",
]
for mediapipe_submission_source in mediapipe_submission_sources:
    require_source_contains(
        mediapipe_submission_source,
        "PendingSubmissionTracker",
        "MediaPipe LIVE_STREAM sources must use bounded submission correlation",
    )
    forbid_source_pattern(
        mediapipe_submission_source,
        r"_submittedAtUs\s*\.\s*Clear\s*\(",
        "MediaPipe sources must not drop all in-flight latency correlation at capacity",
    )

borrowed_pose_contract = (
    VCR
    / "Runtime"
    / "Tracking"
    / "BorrowedHumanoidPose.cs"
)
require_source_contains(
    borrowed_pose_contract,
    "IBorrowedHumanoidPoseProvider",
    "borrowed humanoid pose providers must retain an explicit synchronous contract",
)
require_source_contains(
    borrowed_pose_contract,
    "IBorrowedNormalizedMotionProvider",
    "combined borrowed motion providers must retain a synchronous zero-copy contract",
)
require_source_contains(
    borrowed_pose_contract,
    "BorrowedExpressionState",
    "borrowed motion must retain a reusable expression view",
)
require_source_contains(
    borrowed_pose_contract,
    "BorrowedMotionSample",
    "borrowed motion must retain a combined pose/expression sample",
)
require_source_contains(
    borrowed_pose_contract,
    "The backing arrays remain owned by the provider",
    "borrowed pose lifetime must remain documented next to the type",
)

motion_snapshot_contract = (
    VCR
    / "Runtime"
    / "Tracking"
    / "INormalizedMotionSnapshotProvider.cs"
)
require_source_contains(
    motion_snapshot_contract,
    "ISelectiveNormalizedMotionSnapshotProvider",
    "motion snapshot providers must retain the optional selective capture contract",
)
require_source_contains(
    motion_snapshot_contract,
    "NormalizedMotionSnapshotRequest",
    "selective motion capture must retain an explicit domain request value",
)

vrm_snapshot_provider = (
    VCR
    / "Runtime"
    / "Character"
    / "Vrm10MotionSnapshotProvider.cs"
)
require_source_contains(
    vrm_snapshot_provider,
    "request.IncludeExpressions",
    "VRM snapshot provider must skip expression capture when not requested",
)
require_source_contains(
    vrm_snapshot_provider,
    "IBorrowedHumanoidPoseProvider",
    "VRM snapshot provider must expose reusable borrowed pose buffers for synchronous consumers",
)
require_source_contains(
    vrm_snapshot_provider,
    "IBorrowedNormalizedMotionProvider",
    "VRM snapshot provider must expose combined borrowed motion sampling",
)
require_source_contains(
    vrm_snapshot_provider,
    "SampleBorrowedExpressions(",
    "VRM borrowed expression sampling must reuse provider-owned buffers",
)
require_source_contains(
    vrm_snapshot_provider,
    "Array.Clear(",
    "VRM borrowed buffers must clear stale presence/expression state before reuse",
)
require_source_contains(
    vrm_snapshot_provider,
    "SamplePose(",
    "VRM owned and borrowed pose paths must share one sampling implementation",
)
require_source_contains(
    vrm_snapshot_provider,
    "request.IncludeHumanoidPose",
    "VRM snapshot provider must skip pose capture when not requested",
)

application_ui = (
    VCR
    / "Runtime"
    / "UI"
    / "ApplicationUiController.cs"
)
require_source_contains(
    application_ui,
    "_buttonLabels",
    "application UI must cache button label components across refreshes",
)
require_source_contains(
    application_ui,
    "_buttonLabels.TryGetValue",
    "application UI button label refresh must use the component cache",
)
require_source_contains(
    application_ui,
    "currentAppearance",
    "appearance UI refresh must reuse one current-state snapshot within a refresh pass",
)

mixer_runtime = (
    VCR
    / "Runtime"
    / "Tracking"
    / "Mixing"
    / "MotionExpressionMixer.cs"
)
require_source_contains(
    mixer_runtime,
    "_additionalPoseLayerFrames",
    "mixer must reuse one additional-layer provider sample for change detection and blending",
)

snapshot_ownership = (
    VCR
    / "Runtime"
    / "Tracking"
    / "SnapshotArrayOwnership.cs"
)
require_source_contains(
    snapshot_ownership,
    "SnapshotArrayOwnership.Transfer",
    "snapshot arrays must retain an explicit zero-copy ownership-transfer mode",
)
require_source_contains(
    snapshot_ownership,
    "SnapshotArrayOwnership.Copy",
    "snapshot arrays must retain a defensive-copy ownership mode",
)

snapshot_hot_paths = [
    VCR / "Runtime" / "Tracking" / "MediaPipe" / "MediaPipeFaceNormalizer.cs",
    VCR / "Runtime" / "Tracking" / "MediaPipe" / "MediaPipeHolisticNormalizer.cs",
    VCR / "Runtime" / "Tracking" / "Mixing" / "ExpressionMixerMath.cs",
    VCR / "Runtime" / "Tracking" / "Mixing" / "HumanoidPoseMixerMath.cs",
    VCR / "Runtime" / "Protocols" / "Vmc" / "VmcFrameAccumulator.cs",
]
for snapshot_hot_path in snapshot_hot_paths:
    require_source_contains(
        snapshot_hot_path,
        "SnapshotArrayOwnership.Transfer",
        "fresh hot-path snapshot arrays must explicitly transfer ownership instead of cloning",
    )

ifacial_frame = (
    VCR
    / "Runtime"
    / "Tracking"
    / "ArKit"
    / "IFacialMocapFrame.cs"
)
require_source_contains(
    ifacial_frame,
    "ReadOnlySpan<float> Coefficients",
    "raw iFacialMocap coefficients must not expose a mutable array",
)
require_source_contains(
    ifacial_frame,
    "DetachCoefficientOwnership",
    "raw iFacialMocap coefficients must retain explicit one-shot transfer",
)
forbid_source_pattern(
    ifacial_frame,
    r"public\s+float\[\]\s+Coefficients",
    "raw iFacialMocap coefficients must not expose mutable array ownership",
)

vmc_accumulator = (
    VCR
    / "Runtime"
    / "Protocols"
    / "Vmc"
    / "VmcFrameAccumulator.cs"
)
require_source_contains(
    vmc_accumulator,
    "MaxCustomExpressions",
    "VMC custom expression state must retain a hard entry limit",
)
require_source_contains(
    vmc_accumulator,
    "MaxCustomExpressionNameCharacters",
    "VMC custom expression names must retain a hard length limit",
)
require_source_contains(
    vmc_accumulator,
    "_customExpressionStaging.Count >=",
    "VMC accumulator must enforce its custom expression entry limit",
)

vmc_source = (
    VCR
    / "Runtime"
    / "Protocols"
    / "Vmc"
    / "VmcTrackingSource.cs"
)
require_source_contains(
    vmc_source,
    "_accumulator.ResetState();",
    "VMC source stop/dispose paths must clear retained session state",
)

ifacial_parser = (
    VCR
    / "Runtime"
    / "Tracking"
    / "ArKit"
    / "IFacialMocapFrameParser.cs"
)
require_source_contains(
    ifacial_parser,
    "ReadOnlySpan<char>",
    "iFacialMocap parser must retain span-based token parsing",
)
forbid_source_pattern(
    ifacial_parser,
    r"\.Substring\s*\(",
    "iFacialMocap packet parsing must not allocate substring tokens",
)
forbid_source_pattern(
    ifacial_parser,
    r"\.Split\s*\(",
    "iFacialMocap head parsing must not allocate split arrays/strings",
)

ifacial_receiver = (
    VCR
    / "Runtime"
    / "Tracking"
    / "ArKitUnity"
    / "IFacialMocapUdpReceiver.cs"
)
require_source_contains(
    ifacial_receiver,
    "StrictUtf8.GetChars(",
    "iFacialMocap UDP decode must reuse a caller-owned character buffer",
)
forbid_source_pattern(
    ifacial_receiver,
    r"StrictUtf8\.GetString\s*\(",
    "iFacialMocap UDP receive must not allocate one full packet string per datagram",
)

if VCR.is_dir():
    for path in VCR.rglob("*"):
        if not path.is_file():
            continue

        if path.suffix in {".cs", ".asmdef"}:
            if path.stat().st_size == 0:
                errors.append(f"empty source file: {path.relative_to(ROOT)}")

            meta = Path(str(path) + ".meta")
            if not meta.is_file():
                errors.append(f"missing Unity meta file: {meta.relative_to(ROOT)}")
            elif meta.stat().st_size == 0:
                errors.append(f"empty Unity meta file: {meta.relative_to(ROOT)}")
            else:
                meta_text = meta.read_text(encoding="utf-8", errors="replace")
                if "fileFormatVersion:" not in meta_text or "guid:" not in meta_text:
                    errors.append(
                        f"invalid Unity meta file: {meta.relative_to(ROOT)} "
                        "(missing fileFormatVersion or guid)"
                    )

        if path.suffix == ".asmdef":
            document = load_json(path)
            if document and not document.get("name"):
                errors.append(f"assembly definition has no name: {path.relative_to(ROOT)}")

manifest_path = UNITY / "Packages" / "manifest.json"
if manifest_path.is_file():
    manifest = load_json(manifest_path)
    dependencies = manifest.get("dependencies", {})
    mediapipe = dependencies.get("com.github.homuler.mediapipe")
    expected = "file:LocalPackages/com.github.homuler.mediapipe-0.16.3.tgz"
    if mediapipe != expected:
        errors.append(
            "MediaPipe manifest dependency is not pinned to the expected local package "
            f"({expected!r}); got {mediapipe!r}"
        )

p12_batch = VCR / "Editor" / "P12" / "P12BatchValidation.cs"
if p12_batch.is_file() and "P11BatchValidation.RunChecks()" not in p12_batch.read_text(encoding="utf-8"):
    errors.append("P12 batch validation no longer inherits the P11 validation chain")

p13_batch = VCR / "Editor" / "P13" / "P13BatchValidation.cs"
if p13_batch.is_file() and "P12BatchValidation.RunChecks()" not in p13_batch.read_text(encoding="utf-8"):
    errors.append("P13 batch validation no longer inherits the P12 validation chain")

# These files must be generated by the pinned Unity editor. Hand-authored
# substitutes are not accepted. Development checks keep the gap visible as a
# warning; release/promotion checks can opt into a strict failure gate.
for path in [
    UNITY / "Packages" / "packages-lock.json",
    UNITY / "ProjectSettings" / "ProjectSettings.asset",
]:
    if not path.is_file():
        message = (
            f"reproducibility evidence still missing: {path.relative_to(ROOT)} "
            "(generate/commit from the pinned Unity editor, do not hand-author)"
        )
        if args.strict_reproducibility:
            errors.append(message)
        else:
            warnings.append(message)

for message in warnings:
    print(f"WARNING: {message}")

if errors:
    for message in errors:
        print(f"ERROR: {message}", file=sys.stderr)
    print(f"Repository structure validation: FAIL ({len(errors)} error(s))", file=sys.stderr)
    raise SystemExit(1)

mode = (
    "strict reproducibility"
    if args.strict_reproducibility
    else "development"
)
print(
    "Repository structure validation: PASS "
    f"(mode={mode}; {len(warnings)} non-blocking warning(s))"
)
