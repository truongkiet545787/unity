# Paste this into Codex

Read `README_FOR_CODEX.md` and `PROJECT_PLAN_FOR_CODEX.md` first. Then read every document in `docs/` and inspect all images/video in `references/` before editing code.

Treat the written specifications and `references/17_exact_track_schematic.svg` / `18_exact_track_schematic.png` as authoritative. Generated concept art is visual inspiration only and may contain approximate labels.

Your first task is NOT to implement the full game. Audit the existing Unity repository and report:

1. Unity version and render pipeline
2. current scenes, packages and folder structure
3. whether a vehicle controller already exists
4. the smallest safe Milestone 1 implementation plan
5. exact files you propose to create or modify

Then implement only Milestone 1: modular keyboard-driven arcade vehicle sandbox with `ICarInputProvider`, `KeyboardInputProvider`, `CarController`, a stable Rigidbody setup and a chase camera. Keep the architecture compatible with later `PoseInputProvider` and `AIInputProvider`.

Do not copy Beach Buggy Racing assets, map geometry, UI, branding, characters or audio. Do not introduce unnecessary packages. Preserve buildability after each change and include a short test checklist with every milestone.
