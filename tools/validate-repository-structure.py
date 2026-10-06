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


def require_source_occurrences(
    path: Path,
    token: str,
    expected: int,
    label: str,
) -> None:
    if not path.is_file():
        return

    source = path.read_text(encoding="utf-8", errors="replace")
    actual = source.count(token)
    if actual != expected:
        errors.append(
            f"hot-path source occurrence mismatch: {label}: "
            f"{path.relative_to(ROOT)}: expected {expected}, got {actual}"
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

p3_tracking_validation = (
    VCR
    / "Editor"
    / "P3"
    / "P3BuiltInTrackingValidation.cs"
)
require_source_contains(
    p3_tracking_validation,
    "ValidateAudioSnapshotSuppression",
    "P3 validation must cover unchanged audio expression snapshot suppression",
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
require_source_contains(
    p0_vmc_validation,
    "_customExpressionWireNames",
    "P0 VMC validation must cover repeated direct custom-name cache reuse",
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

event_text_transform = (
    VCR
    / "Runtime"
    / "EventRuntime"
    / "EventTextTransform.cs"
)
require_source_contains(
    event_text_transform,
    "string.Concat(",
    "event text prefix/suffix application must build the final affixed string without an intermediate concatenation",
)
forbid_source_pattern(
    event_text_transform,
    r"prefix\s*\+",
    "event text prefix application must not allocate an intermediate prefix result",
)

event_runtime_engine = (
    VCR
    / "Runtime"
    / "EventRuntime"
    / "EventRuntimeEngine.cs"
)
require_source_contains(
    event_runtime_engine,
    "_traceSubscribers",
    "event tracing must reuse a copy-on-write subscriber snapshot",
)
forbid_source_pattern(
    event_runtime_engine,
    r"GetInvocationList\s*\(",
    "event tracing must not allocate a delegate invocation array per trace entry",
)

p9_event_runtime_validation = (
    VCR
    / "Editor"
    / "P9"
    / "P9EventRuntimeValidation.cs"
)
require_source_contains(
    p9_event_runtime_validation,
    "synthetic trace subscriber failure",
    "P9 event validation must retain trace subscriber failure-isolation coverage",
)
require_source_contains(
    p9_event_runtime_validation,
    "text transform must preserve the original string when no work is configured",
    "P9 event validation must cover text-transform reference passthrough and affix null semantics",
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
    "_customExpressionWireNames",
    "VMC custom expression wire names must retain a bounded repeat-name cache",
)
require_source_contains(
    vmc_accumulator,
    "ApplyCustomBlendUtf8(",
    "VMC custom expression wire-name cache must be applied before string materialization",
)
require_source_contains(
    vmc_accumulator,
    "Array.Empty<",
    "VMC expression snapshots must reuse the shared empty custom-expression array when no custom channels exist",
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
require_source_contains(
    vmc_direct_reader,
    "ApplyCustomBlendUtf8(",
    "direct VMC parsing must route repeated custom names through the bounded wire-name cache",
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

osc_event_mapper = (
    VCR
    / "Runtime"
    / "Protocols"
    / "OscEvents"
    / "OscNormalizedEventMapper.cs"
)
require_source_contains(
    osc_event_mapper,
    "internal static class OscNormalizedEventPacketReader",
    "generic OSC event ingress must retain its specialized direct packet reader",
)
require_source_contains(
    osc_event_mapper,
    "ValidatePacket(",
    "direct OSC event parsing must validate the complete packet before exposing events",
)
require_source_contains(
    osc_event_mapper,
    "ReadPacketEvents(",
    "direct OSC event parsing must retain a separate post-validation mapping pass",
)
forbid_source_pattern(
    osc_event_mapper,
    r"new\s+OscMessage\b",
    "direct OSC event packet parsing must not allocate transient OscMessage objects",
)
forbid_source_pattern(
    osc_event_mapper,
    r"new\s+OscArgument\s*\[",
    "direct OSC event packet parsing must not allocate transient OSC argument arrays",
)

osc_event_receiver = (
    VCR
    / "Runtime"
    / "Protocols"
    / "OscEventsUnity"
    / "OscNormalizedEventUdpReceiver.cs"
)
require_source_contains(
    osc_event_receiver,
    "OscNormalizedEventPacketReader",
    "OSC event UDP receive must use the specialized direct packet reader",
)
forbid_source_pattern(
    osc_event_receiver,
    r"OscPacketReader\s*\.\s*TryReadMessages",
    "OSC event UDP receive must not rebuild generic OSC message objects per datagram",
)

p8_protocol_validation = (
    VCR
    / "Editor"
    / "P8"
    / "P8ProtocolEventAdapterValidation.cs"
)
require_source_contains(
    p8_protocol_validation,
    "ValidateOscDirectPacketPath",
    "P8 protocol validation must cover direct OSC event parsing and malformed-packet atomicity",
)

websocket_event_adapter = (
    VCR
    / "Runtime"
    / "Protocols"
    / "WebSocketUnity"
    / "WebSocketEventInjectionAdapter.cs"
)
soop_event_adapter = (
    VCR
    / "Runtime"
    / "Broadcast"
    / "SoopUnity"
    / "SoopBridgeEventAdapter.cs"
)
for json_event_adapter in [
    websocket_event_adapter,
    soop_event_adapter,
]:
    require_source_contains(
        json_event_adapter,
        "_documentScratch",
        "JSON event ingress must reuse one DTO wrapper instead of allocating one per message",
    )
    require_source_contains(
        json_event_adapter,
        "_parseSync",
        "reusable JSON ingress DTO state must be protected against concurrent TryHandleText calls",
    )
    require_source_contains(
        json_event_adapter,
        "JsonUtility.FromJsonOverwrite(",
        "JSON event ingress must overwrite its reusable DTO wrapper",
    )
    require_source_contains(
        json_event_adapter,
        "ResetDocument(",
        "reusable JSON ingress DTO fields must be reset before each overwrite",
    )
    forbid_source_pattern(
        json_event_adapter,
        r"JsonUtility\s*\.\s*FromJson\s*<",
        "JSON event ingress must not allocate a new DTO wrapper per message",
    )

require_source_contains(
    p8_protocol_validation,
    "ValidateJsonIngressScratchReset",
    "P8 protocol validation must cover omitted-field reset for reusable JSON ingress DTOs",
)
require_source_contains(
    p8_protocol_validation,
    "SOOP JSON scratch reuse must reset omitted user/nickname fields",
    "P8 protocol validation must prevent stale SOOP scratch fields from leaking across messages",
)
require_source_contains(
    p8_protocol_validation,
    "WebSocket JSON scratch reuse must reset every omitted optional field",
    "P8 protocol validation must prevent stale WebSocket scratch fields from leaking across messages",
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

mediapipe_webcam_runner = (
    VCR
    / "Runtime"
    / "Tracking"
    / "MediaPipe"
    / "MediaPipeWebcamTrackingRunner.cs"
)
require_source_contains(
    mediapipe_webcam_runner,
    "ShouldSubmitTask(",
    "MediaPipe webcam task loops must use direct task gating instead of delegate dispatch",
)
forbid_source_pattern(
    mediapipe_webcam_runner,
    r"new\s+WaitUntil\s*\(",
    "MediaPipe webcam readback wait must not allocate a WaitUntil closure per task lifecycle",
)
forbid_source_pattern(
    mediapipe_webcam_runner,
    r"Func<bool>\s+shouldSubmit|Action<Image,\s*long>\s+submit",
    "MediaPipe webcam task loops must not retain delegate-based hot-path submission dispatch",
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

appearance_runtime = (
    VCR
    / "Runtime"
    / "Appearance"
    / "Unity"
    / "BasicCharacterAppearanceRuntime.cs"
)
require_source_contains(
    appearance_runtime,
    "ClearCurrentPresetAndNotify(",
    "appearance preset invalidation must publish a snapshot so cached consumers cannot go stale",
)
require_source_contains(
    appearance_runtime,
    "NotifyAppearanceChanged(",
    "appearance change notifications must be routed through subscriber isolation",
)
require_source_contains(
    appearance_runtime,
    "NotifyStatusChanged(",
    "appearance status notifications must be routed through subscriber isolation",
)
require_source_order(
    appearance_runtime,
    "_currentPresetId =\n                    targetId;",
    "if (!ReplaceUserPresets(",
    "active user-preset rename must stage the new current id before registry replacement to avoid transient invalidation",
)
require_source_contains(
    appearance_runtime,
    "_currentPresetId =\n                        sourceId;",
    "failed active user-preset rename must restore the previous current id",
)
require_source_contains(
    appearance_runtime,
    "CurrentAppearanceMatchesPreset(",
    "user-preset registry replacement must invalidate a current preset identity whose definition no longer matches the actual appearance",
)
require_source_contains(
    appearance_runtime,
    "_appearanceSubscriberFailureCount",
    "appearance subscriber failures must be observable through diagnostics",
)
require_source_contains(
    appearance_runtime,
    "_statusSubscriberFailureCount",
    "appearance status subscriber failures must be observable through diagnostics",
)
require_source_contains(
    appearance_runtime,
    "appearance.subscriber.appearance_failures",
    "appearance subscriber failure diagnostics metric must remain exposed",
)
require_source_contains(
    appearance_runtime,
    "appearance.subscriber.status_failures",
    "appearance status subscriber failure diagnostics metric must remain exposed",
)
require_source_contains(
    appearance_runtime,
    "TryCanExecute(",
    "appearance transition executor capability probes must be exception-contained",
)
require_source_contains(
    appearance_runtime,
    "TryCanTrackCompletion(",
    "appearance transition completion capability probes must be exception-contained",
)
require_source_contains(
    appearance_runtime,
    "TryCountExecutors(",
    "appearance transition required-executor validation must use the contained probe path",
)
require_source_contains(
    appearance_runtime,
    "TryCountCompletionProbes(",
    "appearance transition completion validation must use the contained probe path",
)
require_source_contains(
    appearance_runtime,
    "appearance.transition.executor_probe_failures",
    "appearance transition executor probe failures must remain visible in diagnostics",
)
forbid_source_pattern(
    appearance_runtime,
    r"AppearanceChanged\?\.Invoke",
    "appearance runtime must not let one AppearanceChanged subscriber abort committed state transitions",
)
forbid_source_pattern(
    appearance_runtime,
    r"StatusChanged\?\.Invoke",
    "appearance runtime must not let one StatusChanged subscriber abort runtime state transitions",
)

require_source_contains(
    appearance_runtime,
    "TransitionExecutorDiscoveryRetrySeconds = 1.0;",
    "missing appearance transition executors must use bounded discovery retry",
)
require_source_contains(
    appearance_runtime,
    "_nextTransitionExecutorResolveAt",
    "appearance transition executor discovery must retain its negative-cache deadline",
)
require_source_contains(
    appearance_runtime,
    "Time.realtimeSinceStartupAsDouble",
    "appearance transition executor retry must use monotonic realtime",
)

appearance_transition_action_executor = (
    VCR
    / "Runtime"
    / "EventRuntime"
    / "Unity"
    / "AppearanceTransitionActionExecutor.cs"
)
require_source_contains(
    appearance_transition_action_executor,
    "HandlerDiscoveryRetrySeconds = 1.0;",
    "missing transition event handlers must use bounded discovery retry",
)
require_source_contains(
    appearance_transition_action_executor,
    "_nextHandlerResolveAt",
    "transition event-handler discovery must retain its negative-cache deadline",
)
require_source_contains(
    appearance_transition_action_executor,
    "Time.realtimeSinceStartupAsDouble",
    "transition event-handler retry must use monotonic realtime",
)
require_source_contains(
    appearance_transition_action_executor,
    "TryFindHandlerCount(",
    "appearance transition action bridge must contain delegated CanHandle probe failures",
)
require_source_contains(
    appearance_transition_action_executor,
    "TryResolveCompletionProbe(",
    "appearance transition action bridge must contain delegated completion probe failures",
)
require_source_contains(
    appearance_transition_action_executor,
    "appearance.transition.action_handler_probe_failures",
    "delegated action-handler probe failures must remain visible in diagnostics",
)
require_source_contains(
    appearance_transition_action_executor,
    "appearance.transition.action_completion_probe_failures",
    "delegated completion probe failures must remain visible in diagnostics",
)

application_runtime_bootstrap = (
    VCR
    / "Runtime"
    / "Application"
    / "ApplicationRuntimeBootstrap.cs"
)
require_source_contains(
    application_runtime_bootstrap,
    "public bool Suspend()",
    "application bootstrap must expose scene suspend success/failure",
)
require_source_contains(
    application_runtime_bootstrap,
    "return sceneRuntime.Suspend();",
    "application bootstrap suspend must propagate the scene lifecycle result",
)
require_source_contains(
    application_runtime_bootstrap,
    "public bool Resume()",
    "application bootstrap must expose scene resume success/failure",
)
require_source_contains(
    application_runtime_bootstrap,
    "return sceneRuntime.Resume();",
    "application bootstrap resume must propagate the scene lifecycle result",
)
require_source_contains(
    application_runtime_bootstrap,
    "\"Runtime configuration capture/save failed: \"",
    "application configuration save must contain scene snapshot/adapter getter exceptions",
)

application_ui = (
    VCR
    / "Runtime"
    / "UI"
    / "ApplicationUiController.cs"
)
single_character_scene_runtime = (
    VCR
    / "Runtime"
    / "Scene"
    / "SingleCharacterSceneRuntime.cs"
)
require_source_contains(
    single_character_scene_runtime,
    "public bool TryCaptureRenderSettings(",
    "scene runtime must expose render-bootstrap availability without guessing from fallback settings",
)
require_source_contains(
    single_character_scene_runtime,
    "settings =\n                    RenderRuntimeSettings.Default1080p;",
    "missing render bootstrap must preserve the historical default render-settings fallback while reporting unavailable",
)

require_source_contains(
    single_character_scene_runtime,
    "OptionalServiceDiscoveryRetrySeconds = 1.0;",
    "scene runtime must bound negative optional-service discovery retries",
)
require_source_contains(
    single_character_scene_runtime,
    "ref _nextEnvironmentRuntimeResolveAt",
    "missing environment runtime discovery must be retry-throttled",
)
require_source_contains(
    single_character_scene_runtime,
    "ref _nextOverlayOutputResolveAt",
    "missing overlay output discovery must be retry-throttled",
)
require_source_contains(
    single_character_scene_runtime,
    "Time.realtimeSinceStartupAsDouble",
    "optional-service retry throttling must use monotonic realtime rather than frame time",
)
require_source_contains(
    single_character_scene_runtime,
    "_optionalServiceBehaviourScratch",
    "scene runtime optional-service discovery must reuse one MonoBehaviour scratch list",
)
require_source_contains(
    single_character_scene_runtime,
    "GetComponentsInChildren<MonoBehaviour>(\n                true,\n                _optionalServiceBehaviourScratch);",
    "scene runtime optional-service discovery must use the non-alloc List overload",
)
forbid_source_pattern(
    single_character_scene_runtime,
    r"GetComponentsInChildren<MonoBehaviour>\(true\)",
    "scene runtime optional-service discovery must not allocate a MonoBehaviour array",
)

require_source_contains(
    single_character_scene_runtime,
    "RenderBootstrapDiscoveryRetrySeconds = 1.0;",
    "missing render bootstrap discovery must use bounded retry",
)
require_source_contains(
    single_character_scene_runtime,
    "_nextRenderBootstrapResolveAt",
    "render bootstrap discovery must retain its negative-cache deadline",
)
require_source_contains(
    single_character_scene_runtime,
    "private void ResolveRenderBootstrap()",
    "scene runtime must isolate render-bootstrap discovery from full scene dependency discovery",
)
require_source_contains(
    single_character_scene_runtime,
    "public bool TryCaptureRenderSettings(\n            out RenderRuntimeSettings settings)\n        {\n            ResolveRenderBootstrap();",
    "render-settings capture must not resolve unrelated scene dependencies",
)
require_source_contains(
    single_character_scene_runtime,
    "public BroadcastCaptureReadiness EvaluateBroadcastCaptureTarget(\n            BroadcastCaptureTarget target)\n        {\n            ResolveRenderBootstrap();",
    "broadcast target evaluation must not resolve unrelated scene dependencies before render sampling",
)

require_source_contains(
    single_character_scene_runtime,
    "NotifyStatusChanged(",
    "scene status notifications must isolate subscriber failures",
)
require_source_contains(
    single_character_scene_runtime,
    "scene.status_subscriber_failures",
    "scene status subscriber failures must remain visible in diagnostics",
)
require_source_contains(
    single_character_scene_runtime,
    "catch (OperationCanceledException)",
    "scene character loading must handle cancellation separately from faults",
)
require_source_contains(
    single_character_scene_runtime,
    "RestoreStateAfterCancelledCharacterLoad(",
    "scene character loading must restore idle state after an isolated cancellation",
)
require_source_order(
    single_character_scene_runtime,
    "_overlayOutput.Apply(settings);",
    "_overlayConfiguration =\n                nextConfiguration;",
    "scene overlay configuration must commit only after the adapter apply succeeds",
)
require_source_contains(
    single_character_scene_runtime,
    "var previousConfiguration =\n                CaptureConfiguration();",
    "scene configuration apply must capture a rollback snapshot before mutating runtime state",
)
require_source_contains(
    single_character_scene_runtime,
    "RollbackConfiguration(",
    "scene configuration apply must rollback earlier mutations after a later apply failure",
)
require_source_contains(
    single_character_scene_runtime,
    "\"Scene suspend failed: \"",
    "scene Suspend must contain external adapter shutdown exceptions",
)
require_source_contains(
    single_character_scene_runtime,
    "\"Scene resume failed: \"",
    "scene Resume must contain external presentation restore exceptions",
)
require_source_contains(
    single_character_scene_runtime,
    "public bool TryRecoverOverlayOutput(\n            out string error)\n        {\n            error = null;",
    "overlay recovery must initialize and return structured errors through its Try contract",
)
require_source_contains(
    single_character_scene_runtime,
    "catch (Exception exception)\n            {\n                error =\n                    exception.Message;\n                return false;",
    "overlay recovery must convert lifecycle rejection exceptions into false/error",
)
require_source_contains(
    single_character_scene_runtime,
    "RunRollbackStep(",
    "scene configuration rollback must isolate failures so later restore steps still run",
)
require_source_contains(
    single_character_scene_runtime,
    "_state !=\n                    SceneRuntimeState.LoadingCharacter",
    "cancelled-load recovery must not overwrite newer lifecycle states",
)
forbid_source_pattern(
    single_character_scene_runtime,
    r"StatusChanged\?\.Invoke",
    "scene runtime must not let one status subscriber abort lifecycle transitions",
)

basic_environment_runtime = (
    VCR
    / "Runtime"
    / "Environment"
    / "Unity"
    / "BasicEnvironmentRuntime.cs"
)
require_source_contains(
    basic_environment_runtime,
    "NotifyStateChanged(",
    "environment state notifications must isolate subscriber failures",
)
require_source_contains(
    basic_environment_runtime,
    "environment.state_subscriber_failures",
    "environment state subscriber failures must remain visible in diagnostics",
)
require_source_contains(
    basic_environment_runtime,
    "CloneStateBindings(",
    "environment state bindings must be deep-cloned at the runtime boundary",
)
require_source_contains(
    basic_environment_runtime,
    "TryRestoreStateBindings(",
    "failed environment state-binding apply must preserve the previous active binding state",
)
require_source_contains(
    basic_environment_runtime,
    "TryApplyLightingProfileToTargets(",
    "environment lighting applies must report target execution failures through the public bool/error contract",
)
require_source_contains(
    basic_environment_runtime,
    "var previousProfile =\n                _lightingProfile;",
    "environment lighting profile changes must retain the previous profile for rollback",
)
require_source_contains(
    basic_environment_runtime,
    "lightingTargetBehaviours =\n                    previousBehaviours;",
    "failed lighting-target replacement must restore the previous target configuration",
)
require_source_contains(
    basic_environment_runtime,
    "\"Environment lighting target validation failed: \"",
    "environment lighting validation exceptions must be converted into structured failures",
)
require_source_contains(
    basic_environment_runtime,
    "TryApplyStateBinding(\n                    next,\n                    stateId,",
    "environment state-binding configuration must apply staged bindings before committing them",
)
forbid_source_pattern(
    basic_environment_runtime,
    r"StateChanged\?\.Invoke",
    "environment runtime must not let one state subscriber abort committed state/update dispatch",
)

p6_environment_validation = (
    VCR
    / "Editor"
    / "P6"
    / "P6EnvironmentRuntimeValidation.cs"
)
require_source_contains(
    p6_environment_validation,
    "throwingStateSubscriber",
    "P6 validation must prove environment state subscriber failures are isolated",
)
require_source_contains(
    p6_environment_validation,
    "\"external-day\"",
    "P6 validation must prove configured state bindings are isolated from later caller mutation",
)
require_source_contains(
    p6_environment_validation,
    "P6ConditionalThrowEnvironmentLightingTarget",
    "P6 validation must cover lighting target apply failure rollback",
)
require_source_contains(
    p6_environment_validation,
    "failedProfileAccepted",
    "P6 validation must cover lighting profile rollback after partial target execution",
)

p1_renderer_validation = (
    VCR
    / "Editor"
    / "P1"
    / "P1RendererCoreValidation.cs"
)
require_source_contains(
    p1_renderer_validation,
    "scene.TryCaptureRenderSettings(",
    "P1 validation must cover render-bootstrap availability with current settings",
)
require_source_contains(
    p1_renderer_validation,
    "throwingSceneStatusSubscriber",
    "P1 validation must prove scene status subscriber failures are isolated",
)
require_source_contains(
    p1_renderer_validation,
    "RestoreStateAfterCancelledCharacterLoad",
    "P1 validation must cover cancelled character-load state recovery",
)
require_source_contains(
    p1_renderer_validation,
    "ThrowOnSettingsRead",
    "P1 validation must inject configuration-capture failure through the overlay settings getter",
)
require_source_contains(
    p1_renderer_validation,
    "captureFailureSaveResult",
    "P1 validation must prove configuration capture failure returns false/error without stopping the scene",
)
require_source_contains(
    p1_renderer_validation,
    "stale cancelled-load completion must not overwrite a newer operation generation",
    "P1 validation must guard stale cancelled-load generations",
)
require_source_contains(
    p1_renderer_validation,
    "cancelled-load recovery must not overwrite a newer Suspend/Unload lifecycle state",
    "P1 validation must guard lifecycle states newer than a cancelled load",
)
require_source_contains(
    p1_renderer_validation,
    "failed overlay adapter apply must not commit an unapplied configuration snapshot",
    "P1 validation must cover overlay adapter failure before configuration commit",
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
    "SetSectionLabel(",
    "application UI section labels must avoid unchanged text assignments",
)
require_source_contains(
    application_ui,
    "currentAppearance",
    "appearance UI refresh must reuse one current-state snapshot within a refresh pass",
)
require_source_contains(
    application_ui,
    "_summaryBuilder",
    "application UI summary rendering must reuse one StringBuilder scratch buffer",
)
forbid_source_pattern(
    application_ui,
    r"new\s+List<string>\s*\(\s*_trackingControls\.Count\s*\)",
    "tracking summary refresh must not allocate a temporary line list",
)
forbid_source_pattern(
    application_ui,
    r"new\s+string\s*\[\s*visibleCount\s*\]",
    "appearance preset-order summary must not allocate a temporary string array",
)
require_source_contains(
    application_ui,
    "SetTextIfChanged(",
    "application UI status/title/content refresh must skip unchanged Text.text assignments",
)
forbid_source_pattern(
    application_ui,
    r"_(?:statusText|sectionTitle|contentText)\.text\s*=",
    "application UI primary text fields must route assignments through SetTextIfChanged",
)
require_source_contains(
    application_ui,
    "_subscribedAppearanceRuntime",
    "application UI must subscribe to appearance snapshots instead of polling defensive Current copies every refresh",
)
require_source_contains(
    application_ui,
    "OnAppearanceChanged(",
    "application UI must cache AppearanceChanged snapshots",
)
require_source_occurrences(
    application_ui,
    "_appearanceRuntime.Current",
    1,
    "application UI may read the defensive appearance Current snapshot only in its cache fallback",
)
forbid_source_pattern(
    application_ui,
    r"_appearanceRuntime\.Current\.PresetId",
    "application UI preset-id-only paths must use allocation-free appearance status",
)
require_source_occurrences(
    application_ui,
    "eventRuntime.CaptureRules()",
    1,
    "application UI may defensively clone event rules only for persistence, not refresh/navigation",
)
require_source_contains(
    application_ui,
    "eventRuntime.RuleCount",
    "application UI event controls must use allocation-free event rule count lookup",
)
require_source_contains(
    application_ui,
    "eventRuntime.GetRuleAt(",
    "application UI event controls must use allocation-free indexed event rule lookup",
)
forbid_source_pattern(
    application_ui,
    r"CaptureStatuses\s*\(",
    "application UI Settings refresh must not clone capability status arrays",
)
require_source_contains(
    application_ui,
    ".StatusCount",
    "application UI Settings refresh must use allocation-free capability status counts",
)
require_source_contains(
    application_ui,
    "TryGetStatusAt(",
    "application UI Settings refresh must use allocation-free indexed capability status lookup",
)
forbid_source_pattern(
    application_ui,
    r"_materialController\.GetSlots\s*\(",
    "application UI Materials refresh must not clone material slot descriptors",
)
require_source_contains(
    application_ui,
    "_materialController.TryGetSlotAt(",
    "application UI Materials refresh must use allocation-free indexed material slot lookup",
)
require_source_contains(
    application_ui,
    "_diagnosticsMetricSource",
    "application UI Diagnostics refresh must cache the source metric snapshot identity",
)
require_source_contains(
    application_ui,
    "_trackingSummaryCache",
    "application UI Tracking refresh must cache unchanged rendered summary text",
)
require_source_contains(
    application_ui,
    "_environmentSummaryCache",
    "application UI Environment refresh must cache unchanged rendered summary text",
)
require_source_contains(
    application_ui,
    "_settingsSummaryCache",
    "application UI Settings refresh must cache unchanged rendered summary text",
)
require_source_contains(
    application_ui,
    "_eventsSummaryCache",
    "application UI Events refresh must cache unchanged rendered summary text",
)
require_source_contains(
    application_ui,
    "_outputSummaryCache",
    "application UI Output refresh must cache unchanged readiness/summary state",
)
forbid_source_pattern(
    application_ui,
    r'"Transparent: "\s*\+|"Topmost: "\s*\+|"Click-through: "\s*\+',
    "output control refresh must use stable label literals instead of rebuilding unchanged strings",
)
require_source_contains(
    application_ui,
    "var outputSettings =",
    "output control refresh must sample overlay settings once per refresh pass",
)
require_source_contains(
    application_ui,
    "GetTrackingToggleLabel(",
    "tracking control refresh must cache dynamic enable/disable labels",
)
require_source_contains(
    application_ui,
    "_trackingToggleLabelHasControl",
    "tracking toggle label cache must distinguish no-control state explicitly",
)
require_source_contains(
    application_ui,
    "GetSettingsCapabilityLabel(",
    "settings capability refresh must cache dynamic enable/disable/retry labels",
)
require_source_contains(
    application_ui,
    "GetAppearanceTransitionButtonLabel(",
    "appearance transition control refresh must cache dynamic transition/progress labels",
)
require_source_contains(
    application_ui,
    "GetEnvironmentTransitionModeLabel(",
    "environment transition control refresh must use stable enum label literals",
)
require_source_contains(
    application_ui,
    "GetDiagnosticsNextPageLabel(",
    "diagnostics paging control refresh must cache dynamic page labels",
)
forbid_source_pattern(
    application_ui,
    r'"Transition: "\s*\+',
    "transition control refresh labels must not rebuild unchanged prefix strings",
)
forbid_source_pattern(
    application_ui,
    r'\$"Next Metrics \(',
    "diagnostics paging refresh must not interpolate a new label every tick",
)
forbid_source_pattern(
    application_ui,
    r'"Trace: "\s*\+|"VSync: "\s*\+|"Background: "\s*\+|"CSV Evidence: "\s*\+|"Console Log: "\s*\+',
    "boolean control refresh labels must use stable full-string literals",
)
require_source_contains(
    application_ui,
    "_materialSummaryCache",
    "application UI Materials refresh must cache unchanged rendered summary text",
)
require_source_contains(
    application_ui,
    "_characterSummaryCache",
    "application UI Character refresh must cache unchanged rendered summary text",
)
require_source_contains(
    application_ui,
    "_characterUiOperationGeneration",
    "application UI character async actions must version requests so stale completions cannot overwrite newer state",
)
require_source_contains(
    application_ui,
    "IsCurrentCharacterUiOperation(",
    "application UI character async completions must verify request generation and scene-runtime identity",
)
require_source_contains(
    application_ui,
    "operationGeneration =\n                ++_characterUiOperationGeneration;",
    "character load/reload requests must advance the UI operation generation before awaiting",
)
require_source_contains(
    application_ui,
    "_characterUiOperationGeneration++;\n\n            try\n            {\n                sceneRuntime.UnloadCharacter();",
    "character unload must invalidate outstanding async load/reload UI completions",
)
require_source_contains(
    application_ui,
    "ReferenceEquals(\n                    sceneRuntime,\n                    runtime);",
    "stale character UI completion checks must reject results from a replaced scene runtime",
)
require_source_contains(
    application_ui,
    "_motionSummaryCache",
    "application UI Motion refresh must cache unchanged rendered summary text",
)
require_source_contains(
    application_ui,
    "_contextVisibilitySection",
    "application UI context-control visibility must be cached by selected section",
)
require_source_contains(
    application_ui,
    "_statusBarCache",
    "application UI status bar must cache unchanged rendered text",
)
require_source_contains(
    application_ui,
    "MonoBehaviour[] activeDependencyBehaviours",
    "application UI dependency refresh must share one active-behaviour discovery snapshot",
)
require_source_contains(
    application_ui,
    "ResolveConcreteDependencies(",
    "application UI concrete runtime dependencies must resolve through the shared discovery snapshot",
)
require_source_occurrences(
    application_ui,
    "FindFirstObjectByType<",
    1,
    "application UI must not perform separate global FindFirstObjectByType scans for each optional runtime dependency",
)
require_source_contains(
    application_ui,
    "GetActiveDependencyBehaviours(",
    "application UI optional dependency resolvers must reuse the shared active-behaviour snapshot",
)
require_source_contains(
    application_ui,
    "ResolveCharacterFileSelectionAdapter(\n                    ref activeDependencyBehaviours);",
    "application UI file-selection discovery must participate in shared dependency scanning",
)
require_source_contains(
    application_ui,
    "ResolveAppearanceRuntime(\n                    ref activeDependencyBehaviours);",
    "application UI appearance discovery must participate in shared dependency scanning",
)

require_source_contains(
    application_ui,
    "_environmentRuntime",
    "application UI must cache the resolved environment runtime instead of polling scene discovery every refresh",
)
require_source_contains(
    application_ui,
    "_environmentRuntime =\n                        sceneRuntime?.EnvironmentRuntime;",
    "application UI environment dependency must be refreshed on the bounded dependency pass",
)
require_source_contains(
    application_ui,
    "_environmentRuntimeOwner",
    "application UI cached environment runtime must be bound to its owning scene runtime",
)
require_source_contains(
    application_ui,
    "GetCachedEnvironmentRuntime()",
    "application UI Environment availability/control/summary must reject stale cached environment runtimes",
)
require_source_contains(
    application_ui,
    "MissingTrackingControlDiscoveryRetrySeconds = 5f;",
    "missing tracking controls must use a bounded low-frequency discovery retry",
)
require_source_contains(
    application_ui,
    "_nextTrackingControlResolveTime",
    "tracking control discovery must retain its negative-cache deadline",
)
require_source_contains(
    application_ui,
    "ResolveTrackingControls(\n                    force);",
    "periodic tracking-control resolution must preserve forced startup discovery",
)
require_source_contains(
    application_ui,
    "ResolveTrackingControls(\n                    force: true);",
    "capability changes must bypass tracking-control negative caching",
)

require_source_contains(
    application_ui,
    "_uiRefreshPassActive",
    "application UI must scope runtime sample reuse to one RefreshAll pass",
)
require_source_contains(
    application_ui,
    "GetOverlayOutputForUiRefresh()",
    "application UI must share one overlay-output lookup within a refresh pass",
)
require_source_contains(
    application_ui,
    "TryGetRenderSettingsForUiRefresh(",
    "application UI must share one render-settings sample within a refresh pass",
)
require_source_contains(
    application_ui,
    "finally\n            {\n                _uiRefreshPassActive",
    "application UI refresh-pass cache must be invalidated even if refresh throws",
)
require_source_contains(
    application_ui,
    "_statusBarOutputState",
    "status-bar cache must include overlay output state",
)
require_source_contains(
    application_ui,
    "_statusBarActionMessage",
    "status-bar cache must include the last action message",
)
require_source_contains(
    application_ui,
    "RefreshContextActionVisibility(",
    "application UI context-control visibility work must be isolated from per-refresh state updates",
)
require_source_contains(
    application_ui,
    "_sectionAvailabilityCache",
    "application UI section availability/labels must only update when availability changes",
)
require_source_contains(
    application_ui,
    "Array.Clear(\n                _sectionAvailabilityCache",
    "application UI rebuild must invalidate cached section availability",
)
require_source_contains(
    application_ui,
    "_motionSummaryManualSequence",
    "motion summary cache must use manual expression source sequence to skip unchanged expression formatting",
)
require_source_contains(
    application_ui,
    "_motionPoseWeightLabelStateValid",
    "motion pose-weight label formatting must be cached across unchanged refreshes",
)
require_source_contains(
    application_ui,
    "RefreshMotionPoseWeightLabel(",
    "motion pose-weight slider callback and refresh path must share the cached label formatter",
)
require_source_contains(
    application_ui,
    "_motionSummaryExpressionInput",
    "motion summary cache must include the selected expression input text",
)
require_source_contains(
    application_ui,
    "CharacterSummaryCacheMatches(",
    "application UI Character refresh must compare displayed scene/appearance state before rebuilding text",
)
require_source_contains(
    application_ui,
    "_characterSummaryPresetIds",
    "character summary cache must retain visible user-preset ids without rebuilding order text on cache hits",
)
require_source_contains(
    application_ui,
    "AppendUserPresetOrder(",
    "character summary must append preset order directly into the shared summary builder on cache miss",
)
forbid_source_pattern(
    application_ui,
    r"FormatUserPresetOrder\s*\(",
    "character summary refresh must not allocate a standalone preset-order string",
)
require_source_contains(
    application_ui,
    "MaterialSummaryCacheMatches(",
    "application UI Materials refresh must compare selected status/descriptor state before rebuilding text",
)
require_source_contains(
    application_ui,
    "_materialSummaryRequestedSlotId",
    "material summary cache must include the displayed/requested slot id",
)
require_source_contains(
    application_ui,
    "_materialSummaryErrorCount",
    "material summary cache must include controller error count",
)
require_source_contains(
    application_ui,
    "TryCaptureRenderSettings(",
    "application UI Output refresh must sample render-bootstrap availability once for its cache key",
)
forbid_source_pattern(
    application_ui,
    r"EvaluateBroadcastCaptureTarget\s*\(",
    "application UI Output refresh must not re-enter scene broadcast readiness evaluation every 2 Hz tick",
)
require_source_contains(
    application_ui,
    "EventsSummaryCacheMatches(",
    "application UI Events refresh must compare rule/runtime counters before rebuilding text",
)
require_source_contains(
    application_ui,
    "_eventsSummaryProcessedEvents",
    "events summary cache must include processed-event counters",
)
require_source_contains(
    application_ui,
    "_eventsSummaryRuleStorePath",
    "events summary cache must include the displayed persisted-rule path",
)
require_source_contains(
    application_ui,
    "SettingsSummaryCacheMatches(",
    "application UI Settings refresh must compare displayed runtime/capability/render state before rebuilding text",
)
require_source_contains(
    application_ui,
    "_settingsSummaryCapabilityId",
    "settings summary cache must include the selected capability identity",
)
require_source_contains(
    application_ui,
    "_settingsSummaryRenderScale",
    "settings summary cache must include displayed render settings",
)
require_source_contains(
    application_ui,
    "EnvironmentSummaryCacheMatches(",
    "application UI Environment refresh must compare displayed state before rebuilding text",
)
require_source_contains(
    application_ui,
    "_environmentSummaryTransitionPercent",
    "environment summary cache must key active transitions by displayed progress percentage",
)
require_source_contains(
    application_ui,
    "TrackingSummaryCacheMatches(",
    "application UI Tracking refresh must compare displayed presence/control state before rebuilding text",
)
require_source_contains(
    application_ui,
    "_trackingSummaryEvents",
    "tracking summary cache must include transient presence-event flags in its cache key",
)
require_source_contains(
    application_ui,
    "TrackingSummaryControlState[]",
    "tracking summary cache must reuse control-state storage instead of allocating per refresh",
)
require_source_contains(
    application_ui,
    "GetSortedDiagnosticMetrics(",
    "application UI Diagnostics refresh must reuse one sorted metric snapshot per diagnostics report",
)
require_source_contains(
    application_ui,
    "_diagnosticsSummaryCache",
    "application UI Diagnostics refresh must reuse completed summary text while the snapshot/page/settings key is unchanged",
)
require_source_contains(
    application_ui,
    "InvalidateDiagnosticsSummaryCache()",
    "application UI Diagnostics summary cache must be invalidated when diagnostics binding lifecycle changes",
)
require_source_contains(
    application_ui,
    "_diagnosticsSummarySequence ==",
    "application UI Diagnostics summary cache must key reuse to the published snapshot sequence",
)
forbid_source_pattern(
    application_ui,
    r"RuntimeMetric\[\]\)\s*\n?\s*source\.Clone\s*\(",
    "application UI Diagnostics refresh must not clone already-sorted published metric arrays",
)
forbid_source_pattern(
    application_ui,
    r"new\s+(?:System\.Text\.)?StringBuilder\s*\(",
    "application UI refresh must reuse its shared StringBuilder instead of allocating a new builder",
)
require_source_contains(
    application_ui,
    "SetInputTextIfChanged(",
    "application UI refresh must suppress unchanged InputField text assignments",
)
forbid_source_pattern(
    application_ui,
    r"_(?:eventMaxCommandsInput|settingsRenderScaleInput|settingsFpsInput)\.text\s*=",
    "stable numeric UI refresh fields must not assign InputField.text directly",
)
require_source_contains(
    application_ui,
    "displayedMaxCommands",
    "event max-command refresh must compare the existing numeric value before formatting",
)
require_source_contains(
    application_ui,
    "displayedRenderScale",
    "render-scale refresh must compare the existing numeric value before formatting",
)
require_source_contains(
    application_ui,
    "displayedTargetFps",
    "target-FPS refresh must compare the existing numeric value before formatting",
)

material_override_controller = (
    VCR
    / "Runtime"
    / "Materials"
    / "Unity"
    / "MaterialOverrideController.cs"
)
require_source_contains(
    material_override_controller,
    "public bool TryGetSlotAt(",
    "material override controller must expose allocation-free indexed slot lookup",
)

character_2d_parameter_mapping = (
    VCR
    / "Runtime"
    / "Presentation2D"
    / "Character2DParameterMapping.cs"
)
forbid_source_pattern(
    character_2d_parameter_mapping,
    r"new\s+(?:System\.Collections\.Generic\.)?(?:HashSet|List)\s*<",
    "2D parameter mapping hot paths must not allocate temporary HashSet/List scratch collections",
)
forbid_source_pattern(
    character_2d_parameter_mapping,
    r"\.ToArray\s*\(",
    "2D parameter mapping hot paths must not allocate a second result array through ToArray",
)
forbid_source_pattern(
    character_2d_parameter_mapping,
    r"Enum\.IsDefined\s*\(",
    "2D parameter validation must use allocation-free enum range checks",
)
require_source_contains(
    character_2d_parameter_mapping,
    "var valueCount = 0;",
    "2D parameter snapshot API must pre-count emitted values and allocate one exact-size owned output array",
)
require_source_contains(
    character_2d_parameter_mapping,
    "public int Revision =>",
    "2D parameter mapping profiles must version in-place Configure/Inspector mutations",
)
require_source_contains(
    character_2d_parameter_mapping,
    "internal static bool TryEvaluateValidated(",
    "2D parameter mapping must retain its exact-size validated snapshot evaluator",
)
require_source_contains(
    character_2d_parameter_mapping,
    "internal static bool TryEvaluateValidatedInto(",
    "2D parameter mapping must expose a caller-buffer evaluator for the runtime hot path",
)

character_2d_runtime = (
    VCR
    / "Runtime"
    / "Presentation2D"
    / "Character2DRuntime.cs"
)
require_source_contains(
    character_2d_runtime,
    "_validatedMappingRevision",
    "2D runtime mapping validation cache must include the profile revision",
)
require_source_contains(
    character_2d_runtime,
    "EnsureConfiguredMappingValidated(",
    "2D runtime must cache mapping validation across unchanged tracking updates",
)
require_source_contains(
    character_2d_runtime,
    "_parameterScratch",
    "2D runtime must retain reusable mapped-parameter scratch storage",
)
require_source_contains(
    character_2d_runtime,
    "EnsureParameterScratchCapacity(",
    "2D runtime must grow mapped-parameter scratch only when profile capacity requires it",
)
require_source_contains(
    character_2d_runtime,
    ".TryEvaluateValidatedInto(",
    "2D runtime hot path must evaluate directly into reusable mapped-parameter scratch",
)
require_source_contains(
    character_2d_runtime,
    "_parameterScratch.AsSpan(",
    "2D runtime must pass only the emitted mapped-parameter range to the backend sink",
)
forbid_source_pattern(
    character_2d_runtime,
    r"Character2DParameterMapper\s*\n?\s*\.TryEvaluate\s*\(",
    "2D runtime hot path must not repeat full mapping validation every changed tracking frame",
)

character_2d_contracts = (
    VCR
    / "Runtime"
    / "Presentation2D"
    / "Character2DBackendContracts.cs"
)
require_source_contains(
    character_2d_contracts,
    "ReadOnlySpan<Character2DParameterValue>",
    "2D mapped-parameter sinks must synchronously borrow runtime scratch instead of receiving owned arrays",
)

p13_source_validation = (
    VCR
    / "Editor"
    / "P13"
    / "P13SourceValidation.cs"
)
require_source_contains(
    p13_source_validation,
    "same profile object mutates",
    "P13 validation must cover mapping-cache invalidation for in-place profile mutation",
)
require_source_contains(
    p13_source_validation,
    "reuse the same parameter scratch when capacity is unchanged",
    "P13 validation must cover mapped-parameter scratch reuse across changed frames",
)

capability_registry = (
    VCR
    / "Runtime"
    / "Capabilities"
    / "CapabilityRegistry.cs"
)
require_source_contains(
    capability_registry,
    "_sortedIds",
    "capability registry must maintain stable sorted ids for allocation-free indexed status lookup",
)
require_source_contains(
    capability_registry,
    "public bool TryGetStatusAt(",
    "capability registry must expose allocation-free indexed status lookup",
)
forbid_source_pattern(
    capability_registry,
    r"Array\.Sort\s*\(",
    "capability status capture must reuse the maintained sorted id index instead of sorting every snapshot",
)

event_runtime_rule = (
    VCR
    / "Runtime"
    / "EventRuntime"
    / "EventRuntimeRule.cs"
)
event_runtime_engine = (
    VCR
    / "Runtime"
    / "EventRuntime"
    / "EventRuntimeEngine.cs"
)
require_source_contains(
    event_runtime_rule,
    "public static class EventRuntimeRuleCloner",
    "event runtime deep-clone policy must be centralized in the core rule model",
)
require_source_contains(
    event_runtime_rule,
    "private static EventActionTemplate[]\n            CloneActions(",
    "event runtime deep cloner must include nested action templates",
)
require_source_contains(
    event_runtime_engine,
    "EventRuntimeRuleCloner\n                    .CloneRules(",
    "event runtime engine must deep-clone caller-owned rules before installation",
)

event_runtime_host = (
    VCR
    / "Runtime"
    / "EventRuntime"
    / "Unity"
    / "EventRuntimeHost.cs"
)
require_source_contains(
    event_runtime_host,
    "public EventRuntimeRule GetRuleAt(",
    "event runtime host must expose allocation-free indexed rule lookup",
)
require_source_contains(
    event_runtime_host,
    "public bool TryGetRule(",
    "event runtime host must expose allocation-free id rule lookup",
)
require_source_contains(
    event_runtime_host,
    "private bool TryApplyRules(",
    "event runtime host rule application must use a non-destructive Try path",
)
require_source_contains(
    event_runtime_host,
    "EventRuntimeRuleCloner\n                    .CloneRules(\n                        nextRules);",
    "event runtime host must deep-clone caller-owned rules before installation",
)
require_source_contains(
    event_runtime_host,
    "return EventRuntimeRuleCloner\n                .CloneRules(\n                    rules);",
    "event runtime host CaptureRules must deep-clone the live nested rule graph",
)
require_source_contains(
    event_runtime_host,
    "match.Enabled =\n                    previousEnabled;",
    "failed rule-enable apply must rollback the requested host mutation",
)
require_source_contains(
    event_runtime_host,
    "maxCommandsPerEvent =\n                    previousValue;",
    "failed max-command apply must rollback the requested host limit",
)
forbid_source_pattern(
    event_runtime_host,
    r"rules\s*=\s*Array\.Empty<EventRuntimeRule>\(\);\s*_engine\.TrySetRules",
    "failed event-host rule apply must not destroy the host rule set",
)

motion_cue_sources = [
    VCR
    / "Runtime"
    / "Tracking"
    / "Mixing"
    / "BakedMotionCueSource.cs",
    VCR
    / "Runtime"
    / "Tracking"
    / "Mixing"
    / "ProceduralMotionCueSource.cs",
]
for motion_cue_source in motion_cue_sources:
    require_source_contains(
        motion_cue_source,
        "_cachedSourceId",
        "active motion cues must cache their stable TrackingFrame source id",
    )
    require_source_contains(
        motion_cue_source,
        "GetSourceId(",
        "active motion cue publication must reuse its cached source id",
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
require_source_contains(
    mixer_runtime,
    "_lastBasePoseFrame",
    "mixer must track immutable input snapshots by frame identity rather than sequence alone",
)
forbid_source_pattern(
    mixer_runtime,
    r"_additionalPoseLayerSequences|_additionalPoseLayerSourceIds",
    "mixer additional-layer change detection must not retain redundant sequence/source arrays",
)

require_source_contains(
    mixer_runtime,
    "_targetExpressions != null",
    "mixer must retain zero-transform base-expression passthrough gating",
)
require_source_contains(
    mixer_runtime,
    "? baseFrame",
    "mixer must reuse the routed base expression frame when no expression transform is active",
)

p11_appearance_validation = (
    VCR
    / "Editor"
    / "P11"
    / "P11AppearanceRuntimeValidation.cs"
)
require_source_contains(
    p11_appearance_validation,
    "P11ThrowingProbeExecutor",
    "P11 validation must cover throwing transition executor capability probes",
)
require_source_contains(
    p11_appearance_validation,
    "currentInvalidationNotifications",
    "P11 appearance validation must cover current-preset invalidation notifications",
)

p5_mixer_validation = (
    VCR
    / "Editor"
    / "P5"
    / "P5ExpressionMixerValidation.cs"
)
require_source_contains(
    p5_mixer_validation,
    "unmodified base expression frame must pass through",
    "P5 validation must guard allocation-free base expression frame passthrough",
)

tracking_router = (
    VCR
    / "Runtime"
    / "Tracking"
    / "Routing"
    / "PriorityTrackingRouter.cs"
)
forbid_source_pattern(
    tracking_router,
    r"new\s+TrackingFrame\s*\(",
    "tracking router must reuse selected immutable child frames instead of allocating route envelopes",
)
require_source_contains(
    tracking_router,
    "_latestFace = selected;",
    "tracking router face output must preserve child-frame identity",
)
require_source_contains(
    tracking_router,
    "_latestBodyHands = selected;",
    "tracking router body/hands output must preserve child-frame identity",
)
require_source_contains(
    tracking_router,
    "TryGetUsableFaceCandidate(",
    "tracking router must reuse one sampled face candidate for activation, selection, and routing",
)
require_source_occurrences(
    tracking_router,
    "provider.TryGetLatestFace(",
    1,
    "tracking router must poll a face provider only through the single candidate-sampling helper",
)
require_source_contains(
    tracking_router,
    "_selectedFaceFrame",
    "tracking router duplicate suppression must use immutable frame identity",
)
require_source_contains(
    tracking_router,
    "_expressionFallbackActivation",
    "tracking router must support suspending an expensive expression fallback",
)
require_source_contains(
    tracking_router,
    "UpdateExpressionFallbackActivation(",
    "tracking router must gate expression fallback work before fallback polling",
)
require_source_contains(
    tracking_router,
    "tracking.route.fallback_expression_inference_enabled",
    "tracking router metrics must expose expression fallback activation state",
)

audio_expression_source = (
    VCR
    / "Runtime"
    / "Tracking"
    / "AudioUnity"
    / "AudioDrivenExpressionSource.cs"
)
require_source_contains(
    audio_expression_source,
    "IExpressionTrackingActivationControl",
    "audio expression fallback must expose router-controlled activation",
)
require_source_contains(
    audio_expression_source,
    "if (!_expressionTrackingEnabled)",
    "disabled audio fallback must skip per-frame audio sampling and RMS work",
)
require_source_contains(
    audio_expression_source,
    "_latest = null;",
    "disabling audio fallback must discard stale expression output",
)

expression_activation_contract = (
    VCR
    / "Runtime"
    / "Tracking"
    / "IFaceTrackingActivationControl.cs"
)
require_source_contains(
    expression_activation_contract,
    "IExpressionTrackingActivationControl",
    "tracking activation contracts must include expression fallback gating",
)
forbid_source_pattern(
    tracking_router,
    r"ChildSequence",
    "tracking router must not regress to source/sequence-only duplicate suppression",
)
require_source_contains(
    tracking_router,
    "EnsureRoutePolicy()",
    "tracking router must store and reuse a recovered default routing policy",
)
forbid_source_pattern(
    tracking_router,
    r"routePolicy\s*\?\?",
    "tracking router hot paths must not allocate throwaway default policies",
)

p4_routing_validation = (
    VCR
    / "Editor"
    / "P4"
    / "P4TrackingRoutingValidation.cs"
)
require_source_contains(
    p4_routing_validation,
    "sample the selected preferred face once",
    "P4 validation must guard single-sample preferred face routing",
)
require_source_contains(
    p4_routing_validation,
    "router must store and reuse a recovered default policy",
    "P4 validation must cover recovered route-policy reuse",
)
require_source_contains(
    p4_routing_validation,
    "without a loss gap",
    "P4 validation must cover same-source/same-sequence immutable frame replacement",
)
require_source_contains(
    p4_routing_validation,
    "must suspend audio fallback sampling",
    "P4 validation must cover expression fallback activation and skipped polling",
)

vrm_tracking_target = (
    VCR
    / "Runtime"
    / "Character"
    / "Vrm10TrackingTarget.cs"
)
require_source_contains(
    vrm_tracking_target,
    "_lastFaceFrame",
    "VRM face/body target must detect routed snapshot changes by immutable frame identity",
)

runtime_diagnostics = (
    VCR
    / "Runtime"
    / "Diagnostics"
    / "RuntimeDiagnostics.cs"
)
require_source_contains(
    runtime_diagnostics,
    "_lastFaceFrame",
    "runtime diagnostics update counting must observe immutable frame identity",
)
require_source_contains(
    runtime_diagnostics,
    "_metrics.Sort(",
    "runtime diagnostics must publish subsystem metrics in deterministic sorted order",
)
require_source_contains(
    runtime_diagnostics,
    "CompareRuntimeMetrics(",
    "runtime diagnostics must reuse its report-cadence metric comparator",
)
require_source_contains(
    runtime_diagnostics,
    "_csvBuilder",
    "runtime diagnostics CSV evidence must reuse one StringBuilder across reports",
)
require_source_contains(
    runtime_diagnostics,
    "AppendCsvSanitized(",
    "runtime diagnostics CSV evidence must sanitize metric text while appending instead of allocating a replacement string",
)
forbid_source_pattern(
    runtime_diagnostics,
    r"var\s+metricsText\s*=\s*new\s+StringBuilder",
    "runtime diagnostics CSV evidence must not allocate a separate metrics StringBuilder per report",
)
forbid_source_pattern(
    runtime_diagnostics,
    r"var\s+line\s*=\s*string\.Format\s*\(",
    "runtime diagnostics CSV evidence must build directly into the reusable CSV buffer",
)
forbid_source_pattern(
    runtime_diagnostics,
    r"\.ToString\s*\(\)\.Replace\s*\(",
    "runtime diagnostics CSV evidence must sanitize while appending instead of allocating a replacement copy",
)
require_source_contains(
    runtime_diagnostics,
    "RuntimeMetricComparison",
    "runtime diagnostics metric sorting must reuse a cached comparison delegate",
)
require_source_contains(
    runtime_diagnostics,
    "_metricSources",
    "runtime diagnostics must cache discovered metric sources between reports",
)
require_source_contains(
    runtime_diagnostics,
    "metricSourceRefreshIntervalSeconds",
    "runtime diagnostics metric-source discovery must remain lower-frequency than report collection",
)
require_source_contains(
    runtime_diagnostics,
    "_reportBuilder",
    "runtime diagnostics console reports must reuse StringBuilder scratch storage",
)
require_source_contains(
    runtime_diagnostics,
    "RefreshMetricSources(",
    "runtime diagnostics must refresh cached metric sources through the bounded discovery path",
)

p0_diagnostics_validation = (
    VCR
    / "Editor"
    / "P0"
    / "P0DiagnosticsValidation.cs"
)
require_source_contains(
    p0_diagnostics_validation,
    "P0ThrowingMetricsSource",
    "P0 diagnostics validation must retain metric-source failure isolation coverage",
)

humanoid_pose_state = (
    VCR
    / "Runtime"
    / "Tracking"
    / "HumanoidPoseState.cs"
)
require_source_contains(
    humanoid_pose_state,
    "_boneMask",
    "immutable humanoid pose snapshots must store bone presence as a bitmask",
)
require_source_contains(
    humanoid_pose_state,
    "BoneBit(",
    "humanoid pose bitmask helpers must remain available to hot producers",
)

pose_bitmask_producers = [
    VCR
    / "Runtime"
    / "Protocols"
    / "Vmc"
    / "VmcFrameAccumulator.cs",
    VCR
    / "Runtime"
    / "Character"
    / "Vrm10MotionSnapshotProvider.cs",
    VCR
    / "Runtime"
    / "Tracking"
    / "Mixing"
    / "HumanoidPoseMixerMath.cs",
    VCR
    / "Runtime"
    / "Tracking"
    / "Mixing"
    / "BakedMotionCueSource.cs",
    VCR
    / "Runtime"
    / "Tracking"
    / "Mixing"
    / "ProceduralMotionCueSource.cs",
]
for pose_bitmask_producer in pose_bitmask_producers:
    require_source_contains(
        pose_bitmask_producer,
        "boneMask",
        "hot immutable pose producers must build a compact bone-presence bitmask",
    )
    forbid_source_pattern(
        pose_bitmask_producer,
        r"new\s+bool\s*\[\s*\(int\)HumanoidBoneId\.Count\s*\]",
        "hot immutable pose producers must not allocate a second bool[] presence snapshot",
    )

audio_expression_source = (
    VCR
    / "Runtime"
    / "Tracking"
    / "AudioUnity"
    / "AudioDrivenExpressionSource.cs"
)
require_source_contains(
    audio_expression_source,
    "PublishEpsilon",
    "audio fallback must retain an unchanged-value snapshot suppression threshold",
)
require_source_contains(
    audio_expression_source,
    "_lastSampleTimestampUs",
    "audio fallback health must advance independently of immutable frame publication",
)
require_source_contains(
    audio_expression_source,
    "_lastPublishedValue",
    "audio fallback must remember its last immutable published value",
)
require_source_order(
    audio_expression_source,
    "_lastSampleTimestampUs =",
    "if (_latest != null",
    "audio fallback health sampling must advance before unchanged frame publication is suppressed",
)

humanoid_pose_target = (
    VCR
    / "Runtime"
    / "Character"
    / "Vrm10HumanoidPoseTarget.cs"
)
require_source_contains(
    humanoid_pose_target,
    "MaxTrackedCustomExpressions",
    "VRM custom-expression application state must remain bounded",
)
require_source_contains(
    humanoid_pose_target,
    "_lastPoseFrame",
    "VRM full-body target must detect immutable snapshot replacement by frame identity",
)
require_source_contains(
    humanoid_pose_target,
    "_customExpressionNameScratch",
    "VRM custom-expression neutralization must reuse scratch storage",
)
require_source_contains(
    humanoid_pose_target,
    "FadeCustomExpressionsToNeutral(",
    "missing custom expressions must fade to neutral instead of remaining latched",
)
forbid_source_pattern(
    humanoid_pose_target,
    r"new\s+string\s*\[\s*_smoothedCustomExpressions\.Count\s*\]",
    "VRM custom-expression neutralization must not allocate one name array per frame",
)

vrm_motion_snapshot_provider = (
    VCR
    / "Runtime"
    / "Character"
    / "Vrm10MotionSnapshotProvider.cs"
)
require_source_contains(
    vrm_motion_snapshot_provider,
    "ExpressionKeys",
    "VRM motion snapshot capture must enumerate expression keys without boxing an IDictionary enumerator",
)
require_source_contains(
    vrm_motion_snapshot_provider,
    "GetWeight(",
    "VRM motion snapshot capture must read weights through the stable key list",
)
forbid_source_pattern(
    vrm_motion_snapshot_provider,
    r"GetWeights\s*\(\s*\)",
    "VRM motion snapshot hot paths must not foreach the IDictionary-returning GetWeights API",
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

require_source_contains(
    vmc_accumulator,
    "regions |=\n                    TrackingRegion.Expressions;",
    "VMC expression payloads must advertise TrackingRegion.Expressions at the producer boundary",
)

p0_vmc_validation = (
    VCR
    / "Editor"
    / "P0"
    / "P0VmcSetupMenu.cs"
)
require_source_contains(
    p0_vmc_validation,
    "expressionFrame.ValidRegions",
    "P0 VMC validation must cover expression region flags",
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
require_source_contains(
    ifacial_receiver,
    "StartStreamingV2Bytes",
    "iFacialMocap handshake payload bytes must be cached instead of encoded on every retry",
)
require_source_contains(
    ifacial_receiver,
    "_handshakeSender",
    "iFacialMocap handshake retries must reuse one sender socket per receiver lifecycle",
)
require_source_contains(
    ifacial_receiver,
    "CloseHandshakeSender();",
    "iFacialMocap receiver shutdown/recovery must release a reusable or faulted handshake sender",
)
forbid_source_pattern(
    ifacial_receiver,
    r"using\s+var\s+sender\s*=\s*new\s+UdpClient",
    "iFacialMocap handshake retry must not create and dispose a UDP socket on every send",
)
forbid_source_pattern(
    ifacial_receiver,
    r"var\s+bytes\s*=\s*Encoding\.UTF8\.GetBytes",
    "iFacialMocap handshake retry must not re-encode the constant command on every send",
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
