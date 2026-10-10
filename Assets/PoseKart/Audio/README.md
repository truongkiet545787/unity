# PoseKart audio clips

The runtime audio system is in `Assets/Scripts/Vehicle/PoseKartVehicleAudio.cs` and
automatically attaches to every `CarController` in loaded scenes. It creates no audio
source for a missing clip, so an unconfigured checkout remains error-free.

Suggested licensed/CC0 files to add under these folders:

## Engine

- `Engine/engine_idle.wav`
- `Engine/engine_low_rpm.wav`
- `Engine/engine_high_rpm.wav`
- `Engine/engine_acceleration.wav` (optional)

## Nitro and effects

- `Effects/nitro_start.wav`
- `Effects/nitro_loop.wav`
- `Effects/nitro_end.wav`
- `Tires/tire_skid_loop.wav`
- `Effects/wind_rush_loop.wav`

## Collision

- `Collision/collision_light.wav`
- `Collision/collision_medium.wav`
- `Collision/collision_heavy.wav`

## Environment

- `Environment/ocean_waves_loop.wav`
- `Environment/tropical_wind_loop.wav`
- `Environment/palm_leaves_loop.wav`
- `Environment/bird_01.wav`, `bird_02.wav`, `bird_03.wav`

The project currently includes legacy engine clips in `Engine/idle.wav`, `low.wav`,
`mid.wav`, `high.wav`, plus `nitro.wav` and `nitro_loop.wav`; these remain compatible
through the legacy fallback path. It does not yet contain the sixteen separately
recorded RPM samples, tire/collision/environment effect set. Do not use Asphalt
recordings or other copyrighted game extracts. After importing licensed clips, assign
the engine, Nitro, tire and collision slots on the `PoseKartVehicleAudio` component of
one vehicle and copy those settings to the player/AI vehicles. The environment slots are on the
runtime-created `PoseKart Environment Audio` object; a mixer asset is intentionally
not claimed here because no Unity editor import/setup pass was available.

## 16-step RPM engine bank

Create an asset with `Assets > Create > PoseKart > Audio > Engine Sound Bank`.
It contains exactly these slots: 1000, 1500, 2000, 2500, 3000, 3500, 4000,
4500, 5000, 5500, 6000, 6500, 7000, 7500, 8000 and 8500 RPM.

Assign the bank to `PoseKartVehicleAudio > 16-step RPM sound bank > Engine Sound
Bank`, then leave `Engine Audio Mode` on `Automatic Fallback`. The component uses
equal-power blending between two reusable sources and falls back to the legacy
Idle/Low/High fields when the bank is absent. Use `Assets/Editor/PoseKartEngineSoundBankEditor.cs`
via the bank Inspector's `Auto-Assign RPM Clips From Folder` button to scan names such
as `3000RPM_Rally.wav`; ambiguous duplicate RPM names are skipped.
