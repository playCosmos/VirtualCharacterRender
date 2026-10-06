# Performance

## Product performance target

VirtualCharacterRender is optimized for one active 3D character.

The runtime must defend 60 FPS when the host PC is otherwise not under meaningful load, using the validated baseline configuration.

## Reference hardware status

### macOS

Minimum reference hardware class:

- Apple Silicon
- M1 or newer

M1 is the current minimum macOS performance-validation floor.

This does not mean every M1 configuration guarantees every optional advanced feature at 1080p60. Feature-specific guarantees remain tied to the validated baseline workload.

### Windows

Minimum/recommended Windows hardware is currently **TBD**.

Do not publish a Windows CPU/GPU minimum until P0 measurements establish a defensible baseline.

Windows hardware tiers will be defined from measured 720p60/1080p60 behavior rather than guessed specifications.

## Resolution tiers

### Minimum supported target

- 1280×720
- 60 FPS
- transparent overlay
- one character
- built-in basic tracking enabled
- default/MToon-class material path
- normal broadcast UI/services

This tier is the minimum release support target.

### Recommended target

- 1920×1080
- 60 FPS
- same baseline feature set

1080p60 is the primary optimization and validation target.

### Higher resolutions

1440p, 4K, ultrawide, and custom resolutions are allowed.

They are supported as configurable output modes, but performance guarantees are documented separately by platform/hardware class and are not implied by the 720p60/1080p60 targets.

## Frame-time gates

At 60 FPS the nominal frame budget is 16.67 ms.

For validated baseline sessions:

- average frame rate: 60 FPS target
- P95 frame time: <= 16.67 ms target
- P99 frame time: <= 25 ms target
- sustained frame drops must be diagnosable by subsystem
- startup/transitions may have separate documented transient budgets

Average FPS alone is not a release gate.

## Baseline session

Performance measurements use an explicitly versioned reference workload:

```text
1 active VRM character
1 primary camera
1 environment
MediaPipe webcam tracking
basic face/head/hand/upper-body tracking
optional ARKit face/head source
basic expressions/motion
MToon/default material path
transparent overlay
no heavy optional post effects
no advanced event graph workload
no full-body tracking
```

A reference model/content package and platform test machines must be recorded before performance claims are treated as release guarantees.

## Tracking latency targets

Initial engineering targets:

- tracking adapter -> normalized state: <= 30 ms P95 where source permits
- local VMC -> runtime: <= 30 ms P95
- mobile ARKit-compatible LAN input -> runtime: <= 70 ms P95
- webcam capture -> visible character response: <= 80 ms target

These are engineering targets subject to P0 hardware/source validation.

## Runtime cost attribution

Diagnostics must attribute at least:

- render CPU time
- render GPU time
- tracking/inference time
- motion/expression mixer time
- shader/post-processing cost
- environment cost
- event/protocol cost
- memory use
- render-target/texture allocation
- active capabilities/services
- MediaPipe Face/Holistic GPU→CPU readback count and latest readback wait time when webcam tracking is active
- MediaPipe pending LIVE_STREAM submissions and timestamp-correlation eviction counts
- VMC combined-borrowed, pose-only-borrowed, and immutable-snapshot packet counts

The desktop MediaPipe baseline currently uses separate LIVE_STREAM Face and Holistic submissions. The optional low-light preprocessor intentionally freezes one processed RenderTexture per Unity frame so Face/Holistic asynchronous readbacks observe the same image; do not re-blit that shared target mid-frame when preprocessing settings change. Missing/unsupported preprocessing shaders are resolved once per preprocessor lifecycle rather than searched every frame, and an existing RenderTexture is recreated if Unity reports that its graphics resource is no longer created. CPU async readback and MediaPipe `Image` creation remain task-specific. Do not share one `Image` between both native tasks until the pinned plugin's ownership/lifetime contract is proven under concurrent LIVE_STREAM use. Submission timestamp correlation is capped at the most recent 64 submissions per task; capacity pressure evicts only the oldest still-pending timestamp rather than clearing every in-flight latency record. Use the readback metrics together with `tracking.mediapipe.face.pending_submissions`, `tracking.mediapipe.holistic.pending_submissions`, and the two `timestamp_evictions` counters before changing the capture topology.

## Managed-allocation gate

The 60 FPS baseline is also a managed-GC target, not only a CPU/GPU frame-time target.

For steady-state tracking and UI operation:

- expression custom-channel merge scratch storage must be reused; do not reintroduce per-frame `Dictionary`, `HashSet`, or `List` construction in the mixer hot path,
- immutable output snapshots may allocate when a genuinely new tracking state is published, but temporary merge containers are not part of that allowance; immutable humanoid pose snapshots store bone-presence in a compact `ulong` mask (current humanoid bone count is below 64), so hot VMC/VRM/mixer/baked/procedural pose producers allocate the owned bone-pose array but not a parallel `bool[]` presence array,
- array-backed snapshots use explicit ownership: freshly allocated hot-path arrays use `SnapshotArrayOwnership.Transfer` with no second clone, while external/reused caller buffers must use `Copy`; a transferred array must never be mutated or returned to a pool after publication,
- expression smoothing must not publish replacement frames when smoothing time does not advance; fully zero pose masks preserve the existing immutable base-pose reference, full-weight/full-mask pose overrides reuse the immutable layer pose when it covers the base, and a pure no-base full-weight expression override reuses the immutable layer expression state,
- UI refresh must reuse cached navigation/button label components, avoid redundant `Text.text` assignments when labels are unchanged, and must not allocate a full section snapshot on every refresh tick; appearance status/current state is sampled once and reused within each refresh pass,
- missing optional dependencies may trigger bounded discovery retries, not an unbounded per-frame `FindObjectsByType` scan; event-hub auto-rebinding uses a 1 Hz player-only lifecycle check,
- event/appearance backlogs must remain bounded; QueueAll appearance transitions default to 32 pending requests and expose depth/limit/rejection metrics,
- normalized event hub, OSC-event ingress, and WebSocket ingress use lock-protected reusable `Queue<T>` storage; do not layer `ConcurrentQueue<T>` underneath the same lock because its segment-management overhead adds no concurrency benefit,
- additional pose-layer providers are sampled once per mixer update and the sampled frame is reused for both change detection and blending; do not call a layer provider twice in the same update,
- baked/procedural motion cues still publish owned immutable pose snapshots while active, but their stable `TrackingFrame.SourceId` string is cached and rebuilt only when runtime/cue identity changes rather than concatenated every frame,
- recurring environment target dispatch must skip destroyed Unity targets instead of repeatedly throwing/catching stale-interface exceptions,
- VMC sender dependency discovery is bounded to retry intervals rather than per-frame scene scans; OSC serialization writes directly into one reusable packet buffer plus reusable argument scratch, so the sender must not reintroduce per-message `byte[]` bundle staging,
- VMC uses `ISelectiveNormalizedMotionSnapshotProvider` when available; disabling expression transmission must skip expression capture/allocation at the VRM snapshot source rather than capturing and discarding it afterward,
- synchronous VMC serialization first prefers `IBorrowedNormalizedMotionProvider`: VRM pose, standard-expression, and custom-expression scratch buffers are provider-owned and reused, so after custom-expression capacity stabilizes the normal VMC path does not create `TrackingFrame`, pose arrays, or expression arrays per packet; pose-only borrowed and immutable snapshot paths remain compatibility fallbacks, and retained/recorded snapshots still allocate owned immutable arrays,
- P0 codec validation byte-compares combined borrowed-motion serialization against the immutable snapshot serializer for the same root/bone/expression state; performance paths must not change wire output semantics,
- iFacialMocap UDP receive decodes UTF-8 into one reusable worker character buffer and parses a `ReadOnlySpan<char>`; packet-wide strings, part substrings, coefficient-name strings, and head-component split strings are not created, while immutable coefficient/frame output still owns its published arrays,
- OSC receive parsing must not allocate transient bundle-tag or type-tag strings; generic OSC-event and ARKit UDP loops reuse one datagram buffer per worker, while VMC UDP additionally uses a two-pass direct packet reader (full validation, then apply) so standard VMC addresses, humanoid bone names, and standard expression names are consumed from byte spans without transient `OscMessage`, argument-array, address-string, or bone-name allocations; a valid custom expression name materializes its string/owned UTF-8 bytes on first sight and repeated identical wire names reuse that bounded cache, while hash collisions deliberately fall back to decoding rather than aliasing channels,
- OSC send serialization must keep exact-size single-buffer message writes and reusable-buffer bundle append paths free of intermediate `MemoryStream`/per-message staging allocations,
- OSC float serialization/type-tag/string encoding must not reintroduce per-float or per-type-tag temporary allocations; network parser fanout remains bounded,
- VMC custom expression staging is bounded to 256 names with 256 characters per custom name; over-limit names are dropped and counted, while existing names remain updatable at capacity,
- VRM custom-expression application state is also bounded to 256 tracked names; custom `ExpressionKey` values and name scratch storage are reused, names absent from a newer expression frame fade to zero and are pruned, and neutral-return processing must not allocate a per-frame name array,
- audio-driven mouth fallback keeps source-health sampling current every update but suppresses replacement immutable expression frames while the mouth value remains within the publication epsilon; exact 0/1 boundary changes are still published,
- VMC Stop/Dispose clears retained bone/expression session state before a later Start so stale transforms or custom names cannot leak across receiver sessions,
- Profiler evidence for 720p60 and 1080p60 must record GC.Alloc/frame and GC spikes alongside frame time before release claims are accepted.

A temporary allocation that is necessary for an immutable published frame is evaluated separately from avoidable scratch allocation. Do not trade correctness or frame immutability for unsafe pooling without ownership/lifetime evidence.

## Disabled capability rule

A disabled optional capability should create no meaningful recurring frame cost.

Examples:

- no camera inference when webcam tracking is disabled
- no full-body solver when full tracking is disabled
- no protocol polling/listener if not enabled
- no event graph evaluation if no graph is active
- no heavy post-processing allocation when unused

## Stability gates

Candidate release soak tests should include:

- 1 hour normal interactive session
- 8 hour extended session
- repeated tracker disconnect/reconnect, including stop/restart races while UDP/native callbacks are in flight
- repeated model/environment reload, including target/component destruction and replacement
- repeated shader-package load/unload while recording managed/native memory; do not add `Resources.UnloadUnusedAssets` to interactive paths without measured hitch evidence
- shader failure and fallback
- sleep/wake or display changes where platform behavior permits

Crash, runaway allocation, unrecovered device loss, and continuously increasing memory are release blockers until characterized.

## Performance policy

New features must report:

1. disabled recurring cost
2. enabled incremental CPU cost
3. enabled incremental GPU cost
4. memory/VRAM delta
5. latency impact where applicable

Performance regression is an architectural concern, not a final optimization phase.
