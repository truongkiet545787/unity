# Acceptance Tests

## Vehicle

- [ ] keyboard steering works at low and high speed
- [ ] no visible jitter while idle
- [ ] vehicle does not flip easily on normal corners
- [ ] reset recovers an off-track vehicle

## Race

- [ ] CP01→CP12 order is enforced
- [ ] skipping a checkpoint does not increment lap
- [ ] 3 laps ends race exactly once
- [ ] ranking works with 4 racers
- [ ] AI can complete the track without transform teleporting

## CV

- [ ] calibration detects both grips reliably
- [ ] neutral pose produces steering near zero
- [ ] left/right wheel direction is correct after mirror handling
- [ ] dead zone suppresses micro-jitter
- [ ] smoothing does not add excessive lag
- [ ] tracking loss returns steering safely toward zero
- [ ] reconnecting tracking does not cause a steering spike

## UI

- [ ] calibration screen shows player/tracking state
- [ ] wheel visibly rotates with detected angle
- [ ] race HUD shows lap, position, time
- [ ] CV debug overlay can be toggled
- [ ] fallback to keyboard is clearly available

## Performance target

Choose target based on the demo machine; recommended:

- Unity: stable 60 FPS where practical, never below 30 FPS during demo
- CV inference: target >= 24 FPS
- perceived steering latency: aim below ~100 ms end-to-end

## Final demo scenario

1. Launch game
2. Start race
3. Detect player
4. Grip virtual wheel
5. Calibrate 3…2…1
6. Countdown 3…2…1…GO
7. Player completes at least one section using body control
8. Show debug overlay briefly
9. Demonstrate tracking-loss safety
10. Finish or show race state and AI competitors
