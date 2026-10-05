# Implementation Tasks — Recommended Order

## Milestone 0 — Repository and project audit

- inspect Unity version/render pipeline/packages
- create branch/commit strategy
- confirm build target
- do not upgrade packages without need

Deliverable: project opens and builds before feature work.

## Milestone 1 — Keyboard vehicle sandbox

- `ICarInputProvider`
- `KeyboardInputProvider`
- arcade `CarController`
- stable Rigidbody setup
- chase camera
- reset-car command

Deliverable: drive one buggy around a flat test scene.

## Milestone 2 — Race loop

- checkpoint sequence
- lap tracker
- countdown
- timer
- finish state
- HUD lap/time

Deliverable: complete 3 laps with keyboard.

## Milestone 3 — Graybox coastal track

- build route from exact schematic
- CP01–CP12
- barriers/reset volumes
- start grid
- boost pads placeholders

Deliverable: full lap without art assets.

## Milestone 4 — AI racers

- AI path / spline
- `AIInputProvider`
- speed adaptation per corner
- simple obstacle avoidance if needed
- ranking calculation

Deliverable: 1 player + 3 AI complete race.

## Milestone 5 — Environment and visual pass

- harbor, village, beach, canyon, cave, cliff
- ocean and landmarks
- lighting and simple VFX
- legally usable/original models only

Deliverable: coherent stylized track at target frame rate.

## Milestone 6 — Python CV prototype

- webcam capture
- pose/hand tracking
- landmark normalization
- on-screen skeleton debug
- virtual wheel math offline

Deliverable: Python app prints stable `steering` from -1 to +1.

## Milestone 7 — UDP integration

- Python UDP sender
- Unity receiver
- packet timeout
- `PoseInputProvider`
- input-provider switcher

Deliverable: car can be driven from Python steering values.

## Milestone 8 — Calibration + wheel UI

- detect player
- place hands in grip zones
- calibration countdown
- wheel rotation UI
- tracking lost UI

Deliverable: full no-keyboard steering demo.

## Milestone 9 — Gestures and polish

- brake gesture
- boost gesture and cooldown
- debug overlay
- audio/VFX feedback
- pause/fallback behavior

Deliverable: presentation-ready interaction.

## Milestone 10 — QA, build and report

- acceptance-test pass
- FPS/latency measurement
- user test with several people
- final build
- demo video/screenshots
- report charts

Deliverable: submitted final project.
