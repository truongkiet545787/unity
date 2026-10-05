# Map Design — Coastal Island Track

## Race direction

Clockwise. 12 ordered checkpoints. 3 laps. The checkpoint order is the authoritative anti-cheat/lap-validation sequence.

## Zone sequence

1. Harbor / Start-Finish
2. Coastal Village
3. Rocky Canyon
4. Cave
5. Cliff Road
6. Beach / Dirt
7. Return to Harbor

The exact schematic is in `references/17_exact_track_schematic.svg` and `references/18_exact_track_schematic.png`.

## Checkpoints

| CP | Zone | Gameplay purpose |
|---|---|---|
| CP01 | Harbor exit | confirms correct start direction |
| CP02 | Harbor→Village | first mild corner |
| CP03 | Village south | narrow road entry |
| CP04 | Village north | S-turn validation |
| CP05 | Canyon entry | transition to rock biome |
| CP06 | Canyon apex | fast bend / boost approach |
| CP07 | Cave entry | lighting transition |
| CP08 | Cave exit | prevent cave shortcut cheating |
| CP09 | Cliff entry | fast elevation section |
| CP10 | Cliff exit | setup for beach descent |
| CP11 | Beach mid | wide drift section |
| CP12 | Harbor return | validates final approach before finish |

## Track elements

- Boost A: canyon approach
- Boost B: cliff section
- Boost C: beach/dirt section
- Small jump ramp: beach/cliff transition; low enough to avoid physics instability
- Optional shortcut: village only, post-MVP
- Start grid: 4 vehicles minimum

## Graybox dimensions — starting estimates

- Normal road width: 9–11 m
- Village road width: 7–9 m
- Beach road width: 12–15 m
- Track barrier height: 0.8–1.2 m
- Start straight: 80–120 m
- Minimum turn radius for normal corners: ~18 m
- Hairpin radius: ~10–14 m

These are tuning defaults, not hard constraints.

## AI path

Use a waypoint path or spline with look-ahead targeting. Place path nodes at corner entry, apex and exit. AI should expose the same logical input values as the player (`steering`, `throttle`, `brake`) through `AIInputProvider` rather than manipulating transforms directly.

## Environment dressing order

1. Graybox road and collision
2. Checkpoints and race flow
3. Barriers and reset volumes
4. Terrain/ocean
5. Large landmarks
6. Buildings/rocks/trees
7. Props
8. Lighting/VFX/audio

Do not art-polish before the full lap works.
