# Tracking

## Principle

VirtualCharacterRender includes basic motion capture while also accepting higher-grade external tracking.

All tracking devices and algorithms are interchangeable input sources. No tracking implementation owns character transforms directly.

## Pipeline

```text
Built-in / External Source
           ↓
         Adapter
           ↓
Validation / Timestamp / Confidence
           ↓
    Normalized Tracking State
           ↓
       Routing / Mixer
           ↓
     Character Runtime
```

## Built-in basic capture

The initial product must support a simple setup without another VTuber application.

### Webcam

Target capabilities:

- head pose
- basic facial landmarks/expressions
- eye openness/gaze where reliable
- mouth openness/shapes where reliable
- optional hands/body after performance and quality validation

Webcam inference can be disabled completely and must obey Lightweight performance budgets.

### Apple mobile face tracking

Support an iPhone/iPad ARKit-compatible facial tracking source.

Internally this is treated as ARKit face-tracking data rather than Face ID authentication data.

Expected data includes head pose and expression/face coefficients supported by the selected mobile capture implementation.

Desktop transport/discovery is defined separately from the normalized tracking schema.

### Audio fallback

Microphone-derived mouth/body motion can provide a fallback or low-cost mode when camera tracking is unavailable or intentionally disabled.

## External/candidate sources

- VMC
- OpenSeeFace
- MediaPipe-class face/hand/body tracking
- SteamVR-class trackers
- mocopi-class tracking
- other protocol/plugin sources

## Normalized domains

Face:

- head position/rotation
- left/right eye openness
- gaze
- brows
- mouth openness
- mouth-shape coefficients
- smile/frown/generic expressions

Body:

- root transform
- humanoid bone transforms
- optional confidence per joint

Hands:

- wrist pose
- finger curl or joint pose according to available source quality

## Source health

Each adapter reports:

- availability/connection
- last-update timestamp
- update rate
- stale state
- confidence where available
- error state
- active inference/transport state

## Mixing

A character can route sources by region.

```text
Face  = ARKit mobile
Eyes  = ARKit mobile
Body  = VMC
Hands = Webcam/other adapter
```

Routing policy is explicit and persisted.

## Hot switching

Changing between webcam, mobile face tracking, VMC, or another compatible source must not require the character to be reloaded.

The mixer transitions according to configured fallback and smoothing policy.

## Timing

Source timestamps are preserved where practical. Network and camera sources are not assumed to update at render-frame frequency.

Interpolation/extrapolation policy belongs to mixer/runtime logic rather than source adapters.
