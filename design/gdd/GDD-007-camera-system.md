# GDD-007: Camera System

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | CameraController, GameTuning |
| **Priority** | Medium |

## Overview

The camera is an orthographic 2D camera with two modes: **Auto** (follows battle actions automatically) and **Manual** (player-controlled via drag/zoom/edge-pan). The camera auto-reverts to Manual mode after player interaction, then returns to Auto after a timeout.

## Core Mechanics

### Camera Modes

| Mode | Behavior | Trigger |
|------|----------|---------|
| Auto | Follows actions, zooms to fit combat | Default, after timeout |
| Manual | Player drag/zoom/edge-pan | Player input |

### Auto Mode

- **Focus**: Camera smoothly follows the current action (attacker → target)
- **Zoom**: Adjusts to fit the combat area
- **Settle**: Waits for camera to "settle" before proceeding
- **Priority**: Actions > Player manual control

### Manual Mode

- **Entry**: Any player input (drag, zoom, edge-pan)
- **Duration**: 2 seconds timeout
- **Exit**: Auto reverts after timeout expires

### Controls

| Input | Action |
|-------|--------|
| Right-click drag | Pan camera |
| Mouse scroll | Zoom in/out |
| Edge of screen | Edge-pan (move camera toward edge) |

### Screen Shake

- **Trigger**: Critical hits, heavy impacts
- **Parameters**: Duration, magnitude (separate for crit/normal)
- **Dampening**: Linear decay over duration

### Zoom

- **Range**: 3–20 orthographic size
- **Speed**: Scroll factor × zoomSpeed
- **Reference zoom**: For normalizing edge-pan speed

### Edge Pan

- **Margin**: Pixels from screen edge to activate (default 18)
- **Speed**: Scaled by current zoom level
- **Reference zoom**: For speed normalization

## Dependencies

- `CameraController.cs` — Camera logic
- `GameTuning.cs` — Camera parameters

## Open Questions

- [ ] Should there be a "cinematic" camera for special attacks?
- [ ] Should the camera follow the initiative order visually?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| followSpeed | 9 | Camera follow smoothing |
| camInitialSize | 12 | Initial zoom |
| zoomSize | 5 | Planning phase zoom |
| zoomSpeed | 2 | Scroll zoom speed |
| zoomMin | 3 | Minimum zoom |
| zoomMax | 20 | Maximum zoom |
| edgeMargin | 18 | Edge-pan activation margin |
| edgePanSpeed | 12 | Edge-pan speed |
| dragSpeed | 1 | Right-click drag speed |
| camManualTimeout | 2s | Manual mode timeout |
| camSettleThreshold | 0.05 | Settle detection threshold |
| camSettleTimeout | 1.2s | Max wait for settle |
| camTransitionDuration | 0.3s | Smooth transition duration |
| shakeDurationCrit | 0.25s | Critical shake duration |
| shakeMagnitudeCrit | 0.2 | Critical shake intensity |
| shakeDurationNormal | 0.15s | Normal shake duration |
| shakeMagnitudeNormal | 0.1 | Normal shake intensity |
