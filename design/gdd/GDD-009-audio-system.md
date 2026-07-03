# GDD-009: Audio System

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | AudioManager |
| **Priority** | Low |

## Overview

The audio system uses a singleton `AudioManager` with two audio sources: one for SFX (one-shot) and one for BGM (looping). Audio clips are loaded from `Resources/Audio/` at startup. Volume is controlled via GameTuning.

## Core Mechanics

### Audio Sources

| Source | Usage | Loop |
|--------|-------|------|
| _sfxSource | Sound effects (one-shot) | No |
| _musicSource | Background music | Yes |

### Sound Effects

| Category | Clips | Trigger |
|----------|-------|---------|
| Movement | sfxStep, sfxStepGrass, sfxDash | Unit movement |
| Combat | sfxAttack, sfxHit, sfxCritical, sfxMiss, sfxDeath | Attack resolution |
| Round | sfxDice, sfxRound | Initiative roll, round start |
| Notifications | sfxVictory, sfxDefeat, sfxTimerWarning | Game events |
| UI | sfxUIClick, sfxUIHover, sfxUIConfirm | Menu interaction |

### Background Music

| Clip | Scene |
|------|-------|
| bgmBattle | Battle scene |
| bgmMenu | Main menu |

### Volume Control

- **SFX volume**: `Tuning.Get().sfxVolume` (0–1)
- **Music volume**: `Tuning.Get().musicVolume` (0–1)
- Applied on each Play/PlayMusic call

## Dependencies

- `AudioManager.cs` — Audio management singleton
- `GameTuning.cs` — Volume parameters

## Open Questions

- [ ] Should there be spatial audio (3D positioning)?
- [ ] Should music crossfade between scenes?
- [ ] Should there be dynamic music (combat intensity)?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| sfxVolume | 1.0 | SFX volume |
| musicVolume | 1.0 | Music volume |
