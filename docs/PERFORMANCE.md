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

The desktop MediaPipe task coroutines use direct `faceTask` gating/submission rather than `Func<bool>`/`Action<Image,long>` dispatch, and wait on `AsyncGPUReadbackRequest.done` directly rather than allocating a `WaitUntil` closure. The desktop MediaPipe baseline currently uses separate LIVE_STREAM Face and Holistic submissions. The optional low-light preprocessor intentionally freezes one processed RenderTexture per Unity frame so Face/Holistic asynchronous readbacks observe the same image; do not re-blit that shared target mid-frame when preprocessing settings change. Missing/unsupported preprocessing shaders are resolved once per preprocessor lifecycle rather than searched every frame, and an existing RenderTexture is recreated if Unity reports that its graphics resource is no longer created. CPU async readback and MediaPipe `Image` creation remain task-specific. Do not share one `Image` between both native tasks until the pinned plugin's ownership/lifetime contract is proven under concurrent LIVE_STREAM use. Submission timestamp correlation is capped at the most recent 64 submissions per task; capacity pressure evicts only the oldest still-pending timestamp rather than clearing every in-flight latency record. Use the readback metrics together with `tracking.mediapipe.face.pending_submissions`, `tracking.mediapipe.holistic.pending_submissions`, and the two `timestamp_evictions` counters before changing the capture topology.

## Managed-allocation gate

The 60 FPS baseline is also a managed-GC target, not only a CPU/GPU frame-time target.

For steady-state tracking and UI operation:

- expression custom-channel merge scratch storage must be reused; do not reintroduce per-frame `Dictionary`, `HashSet`, or `List` construction in the mixer hot path,
- immutable output snapshots may allocate when a genuinely new tracking state is published, but temporary merge containers are not part of that allowance; immutable humanoid pose snapshots store bone-presence in a compact `ulong` mask (current humanoid bone count is below 64), so hot VMC/VRM/mixer/baked/procedural pose producers allocate the owned bone-pose array but not a parallel `bool[]` presence array,
- array-backed snapshots use explicit ownership: freshly allocated hot-path arrays use `SnapshotArrayOwnership.Transfer` with no second clone, while external/reused caller buffers must use `Copy`; a transferred array must never be mutated or returned to a pool after publication,
- expression smoothing must not publish replacement frames when smoothing time does not advance; fully zero pose masks preserve the existing immutable base-pose reference, full-weight/full-mask pose overrides reuse the immutable layer pose when it covers the base, and a pure no-base full-weight expression override reuses the immutable layer expression state,
- UI refresh must reuse cached navigation/button label components, route status/title/content updates through unchanged-text suppression, and must not allocate a full section snapshot on every refresh tick; appearance status/current state is sampled once and reused within each refresh pass,
- stable Events/Settings numeric `InputField` refreshes compare the currently displayed parsed value before formatting, and refresh-only input updates use unchanged-text suppression; unchanged max-command/render-scale/target-FPS values must not allocate replacement formatted strings or trigger redundant uGUI input notifications,
- UI summary rendering reuses one `StringBuilder` scratch buffer for tracking-control text and appearance preset-order formatting; the 2 Hz refresh path must not allocate temporary tracking-line `List<string>` or preset-order `string[]` containers,
- Runtime diagnostics sorts subsystem metrics once at report cadence before publishing the immutable `RuntimeDiagnosticsSnapshot.Metrics` array; Diagnostics UI reuses that published order and the shared summary `StringBuilder`, so UI refresh performs no metric-array clone/sort,
- optional CSV diagnostics evidence reuses a dedicated `StringBuilder` across reports and appends/sanitizes metric text directly into that buffer instead of allocating a per-report metrics builder, `string.Format` line, and post-format replacement copy,
- Events/Settings UI navigation uses allocation-free indexed lookup (`EventRuntimeHost.RuleCount`/`GetRuleAt`, `CapabilityRegistry.StatusCount`/`TryGetStatusAt`) instead of cloning rule/capability arrays on each 2 Hz refresh; capability ids are sorted once at registration time and snapshot capture reuses that maintained order,
- Materials UI navigation uses `MaterialOverrideController.SlotCount`/`TryGetSlotAt` instead of `GetSlots().ToArray()` during refresh; defensive slot snapshots remain available for explicit snapshot consumers but are not part of the 2 Hz UI path,
- optional 2D parameter mapping validates duplicate/enumeration constraints without temporary `HashSet`/`Enum.IsDefined` boxing and pre-counts emitted mapped values so each changed snapshot allocates at most the one exact-size parameter array required by the current array-based backend contract; zero emitted values reuse `Array.Empty`,
- `Character2DRuntime` caches mapping validation for the current backend/profile/revision and uses the validated evaluator on changed tracking frames; `Character2DParameterMappingProfile` increments a non-serialized revision on Configure/OnValidate so in-place authoring changes invalidate the cache without requiring a new profile object,
- the application UI caches the defensive `IAppearanceRuntime.Current` snapshot from `AppearanceChanged` and reads it once on runtime binding; repeated refreshes use the cached snapshot, while preset/outfit ID-only paths use `AppearanceRuntimeStatus` and do not trigger accessory-array copies,
- missing optional dependencies may trigger bounded discovery retries, not an unbounded per-frame `FindObjectsByType` scan; event-hub auto-rebinding uses a 1 Hz player-only lifecycle check,
- event/appearance backlogs must remain bounded; QueueAll appearance transitions default to 32 pending requests and expose depth/limit/rejection metrics,
- normalized event hub, OSC-event ingress, and WebSocket ingress use lock-protected reusable `Queue<T>` storage; do not layer `ConcurrentQueue<T>` underneath the same lock because its segment-management overhead adds no concurrency benefit,
- opt-in event-rule tracing keeps a copy-on-write subscriber array so each emitted trace entry does not allocate through `Delegate.GetInvocationList()`; subscriber changes may allocate and a failing trace subscriber remains isolated from later subscribers,
- tracking route-policy priority arrays repair missing/empty serialized defaults once and cache the repaired arrays; per-frame priority lookup must not allocate fallback arrays,
- if the serialized routing policy itself becomes null at runtime, `PriorityTrackingRouter` recreates it once and stores it back; priority comparisons must not allocate throwaway default policy objects,
- diagnostics snapshot notification keeps a copy-on-write subscriber array so repeated report/manual-capture notifications preserve per-subscriber failure isolation without `Delegate.GetInvocationList()` allocation; subscription changes may allocate because they are not the reporting hot path,
- diagnostics caches discovered `IRuntimeMetricsSource` instances and refreshes scene discovery at a 30-second default interval instead of allocating a `FindObjectsByType` result every report; destroyed sources are skipped immediately and force the next report to refresh, while the console report reuses one `StringBuilder` scratch object,
- `PriorityTrackingRouter` reuses the selected immutable child `TrackingFrame` for face, body/hands, full-body, and expression outputs instead of allocating route-envelope frames; Mixer, VRM targets, and diagnostics detect new immutable snapshots by frame reference identity so provider replacement/restart remains visible even when source ID and sequence are reused,
- face routing samples a candidate frame once per router update and reuses that sample for activation/priority selection/output; when the healthy preferred source outranks fallback, the fallback face provider is not polled at all for that update,
- because routing no longer allocates repair envelopes, producers must publish correct `TrackingFrame.ValidRegions` bits themselves; VMC expression frames mark `TrackingRegion.Expressions` at accumulation time rather than depending on router-side normalization,
- when no expression overlay contributes and expression smoothing is disabled, `MotionExpressionMixer` passes the routed base expression `TrackingFrame` through unchanged instead of allocating a mixer envelope; blended or smoothed expression output still publishes an owned mixer frame,
- additional pose-layer providers are sampled once per mixer update and the sampled frame is reused for both change detection and blending; do not call a layer provider twice in the same update,
- baked/procedural motion cues still publish owned immutable pose snapshots while active, but their stable `TrackingFrame.SourceId` string is cached and rebuilt only when runtime/cue identity changes rather than concatenated every frame,
- recurring environment target dispatch must skip destroyed Unity targets instead of repeatedly throwing/catching stale-interface exceptions,
- VMC sender dependency discovery is bounded to retry intervals rather than per-frame scene scans; OSC serialization writes directly into one reusable packet buffer plus reusable argument scratch, so the sender must not reintroduce per-message `byte[]` bundle staging,
- VMC uses `ISelectiveNormalizedMotionSnapshotProvider` when available; disabling expression transmission must skip expression capture/allocation at the VRM snapshot source rather than capturing and discarding it afterward,
- synchronous VMC serialization first prefers `IBorrowedNormalizedMotionProvider`: VRM pose, standard-expression, and custom-expression scratch buffers are provider-owned and reused, so after custom-expression capacity stabilizes the normal VMC path does not create `TrackingFrame`, pose arrays, or expression arrays per packet; pose-only borrowed and immutable snapshot paths remain compatibility fallbacks, and retained/recorded snapshots still allocate owned immutable arrays,
- the pinned UniVRM `v0.131.2` expression runtime exposes its stable `ExpressionKeys` list and `GetWeight`; VRM snapshot sampling uses indexed key access rather than `foreach` over the `IDictionary` returned by `GetWeights()`, avoiding an interface-enumerator box on the high-frequency borrowed-expression path; immutable capture also reuses `Array.Empty<NamedExpressionValue>()` when no custom channels are present,
- P0 codec validation byte-compares combined borrowed-motion serialization against the immutable snapshot serializer for the same root/bone/expression state; performance paths must not change wire output semantics,
- iFacialMocap UDP receive decodes UTF-8 into one reusable worker character buffer and parses a `ReadOnlySpan<char>`; packet-wide strings, part substrings, coefficient-name strings, and head-component split strings are not created, while immutable coefficient/frame output still owns its published arrays,
- OSC receive parsing must not allocate transient bundle-tag or type-tag strings; generic OSC-event and VMC UDP both use two-pass direct packet readers over their reusable datagram buffers (full packet validation first, then mapping/apply), avoiding transient `OscMessage`, argument-array, and address-string allocations. Generic `/vcr/event` decoding materializes only string fields retained by accepted `NormalizedEvent` values, while VMC additionally resolves standard bone/expression names from byte spans; a valid VMC custom expression name materializes its string/owned UTF-8 bytes on first sight and repeated identical wire names reuse that bounded cache, while hash collisions deliberately fall back to decoding rather than aliasing channels. ARKit UDP likewise reuses one datagram buffer per worker,
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
