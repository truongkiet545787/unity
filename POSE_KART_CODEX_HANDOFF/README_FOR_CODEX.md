# POSE KART — CODEX HANDOFF

This folder is the authoritative handoff package for implementing the final-project prototype in Unity.

## Read order

1. `README_FOR_CODEX.md`
2. `PROJECT_PLAN_FOR_CODEX.md`
3. `docs/VISUAL_REFERENCE_INDEX.md`
4. `docs/MAP_DESIGN.md`
5. `docs/CV_PROTOCOL.md`
6. `docs/IMPLEMENTATION_TASKS.md`
7. `docs/ACCEPTANCE_TESTS.md`
8. Inspect all files in `references/`

## Important source-of-truth rule

Generated concept images are visual references only. If a generated image conflicts with written specs, use the written specs. For checkpoint order and track logic, `references/17_exact_track_schematic.svg`, `references/18_exact_track_schematic.png`, and `docs/MAP_DESIGN.md` are authoritative.

## Project goal

Create an original 3D arcade buggy racing game in Unity with a colorful tropical/coastal style. The player drives using a webcam-based virtual steering wheel: pose/hand tracking detects both wrists, determines the wheel angle, sends normalized input to Unity, and Unity controls the car through a decoupled input-provider architecture.

## MVP

- 1 complete track, 3 laps
- 1 player buggy + at least 3 AI racers
- arcade Rigidbody car physics
- chase camera
- checkpoint/lap/ranking/finish flow
- keyboard fallback
- webcam detection and calibration
- virtual steering wheel with left/right grip zones
- two-hand steering, smoothing, dead zone and tracking-loss behavior
- optional brake/boost gestures after steering is stable
- CV debug overlay

## Non-goals for MVP

- online multiplayer
- realistic vehicle simulation
- machine-learning AI drivers
- multiple tracks
- copied Beach Buggy Racing assets, map, branding, UI, characters or sound

## First implementation action

Do not start with CV. First create a playable graybox race using keyboard input and a modular `ICarInputProvider`. Only after the keyboard race loop works, integrate Python pose tracking through UDP.
