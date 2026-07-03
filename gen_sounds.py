"""Generate retro/pixel WAV sounds for Pangea Skirmish."""
import wave, struct, math, random, os

SR = 22050  # sample rate
AMP = 12000  # master amplitude (avoid clipping)

def save(name, samples):
    path = f"Assets/Resources/Audio/{name}.wav"
    bak = path + ".bak"
    if os.path.exists(path) and os.path.getsize(path) > 100:
        os.rename(path, bak)  # keep original as backup
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(struct.pack(f"<{len(samples)}h", *samples))

def sine(freq, t): return math.sin(2 * math.pi * freq * t)
def square(freq, t): return 1.0 if math.sin(2 * math.pi * freq * t) >= 0 else -1.0
def saw(freq, t): return 2.0 * (freq * t - math.floor(freq * t + 0.5))
def tri(freq, t): return 2.0 * abs(2.0 * (freq * t - math.floor(freq * t + 0.5))) - 1.0
def noise(): return random.uniform(-1, 1)

def freq_sweep(start, end, t, dur): return start + (end - start) * (t / dur)

def envelope(t, dur, attack=0.01, release=0.1):
    if t < attack: return t / attack
    if t > dur - release: return max(0, (dur - t) / release)
    return 1.0

def arpeggio(freqs, t, dur):
    """Cycle through freqs list, each lasting cycle_s seconds."""
    cycle_s = 0.06 if dur < 1 else 0.08
    idx = int((t % (len(freqs) * cycle_s)) / cycle_s)
    return freqs[min(idx, len(freqs)-1)]

# ── Combat ──────────────────────────────────────────────────
def gen_critical():
    n = int(SR * 0.5)
    s = []
    for i in range(n):
        t = i / SR
        env = envelope(t, 0.5, 0.001, 0.08)
        f = freq_sweep(800, 2400, t, 0.5)
        w = square(f, t) * 0.7 + sine(f * 2, t) * 0.3
        s.append(int(AMP * env * w))
    save("sfx_critical", s)

def gen_miss():
    n = int(SR * 0.35)
    s = []
    for i in range(n):
        t = i / SR
        env = envelope(t, 0.35, 0.005, 0.15)
        f = freq_sweep(600, 200, t, 0.35)
        w = sine(f, t) * 0.5 + noise() * 0.5
        s.append(int(AMP * 0.5 * env * w))
    save("sfx_miss", s)

# ── Steps ───────────────────────────────────────────────────
def gen_dash():
    n = int(SR * 0.18)
    s = []
    for i in range(n):
        t = i / SR
        env = envelope(t, 0.18, 0.001, 0.05)
        f = freq_sweep(400, 1200, t, 0.18)
        w = sine(f, t) + square(f * 0.5, t) * 0.3
        s.append(int(AMP * 0.6 * env * w))
    save("sfx_dash", s)

def gen_step_grass():
    n = int(SR * 0.12)
    s = []
    for i in range(n):
        t = i / SR
        env = envelope(t, 0.12, 0.001, 0.06)
        w = noise() * 0.6 + sine(200 + noise() * 50, t) * 0.4
        s.append(int(AMP * 0.4 * env * w))
    save("sfx_step_grass", s)

# ── Notifications ───────────────────────────────────────────
def gen_victory():
    n = int(SR * 1.2)
    s = []
    notes = [523, 659, 784, 1047]  # C5 E5 G5 C6
    for i in range(n):
        t = i / SR
        env = envelope(t, 1.2, 0.01, 0.3)
        f = arpeggio(notes, t, 1.2)
        w = square(f, t) * 0.5 + tri(f * 2, t) * 0.3 + sine(f * 3, t) * 0.2
        s.append(int(AMP * env * w))
    save("sfx_victory", s)

def gen_defeat():
    n = int(SR * 1.0)
    s = []
    notes = [392, 349, 330, 262]  # G4 F4 E4 C4 (descending)
    for i in range(n):
        t = i / SR
        env = envelope(t, 1.0, 0.01, 0.4)
        f = arpeggio(notes, t, 1.0)
        w = saw(f, t) * 0.5 + sine(f * 0.5, t) * 0.5
        s.append(int(AMP * 0.6 * env * w))
    save("sfx_defeat", s)

def gen_timer_warning():
    n = int(SR * 0.6)
    s = []
    for i in range(n):
        t = i / SR
        env = envelope(t, 0.6, 0.002, 0.15)
        # two alternating tones: 440Hz pulses
        beat = 0.08
        f = 440 if int(t / beat) % 2 == 0 else 880
        w = square(f, t) * 0.7 + sine(f * 3, t) * 0.3
        s.append(int(AMP * 0.7 * env * w))
    save("sfx_timer_warning", s)

def gen_bonus_prompt():
    n = int(SR * 0.4)
    s = []
    notes = [660, 880]
    for i in range(n):
        t = i / SR
        env = envelope(t, 0.4, 0.002, 0.1)
        f = arpeggio(notes, t, 0.4)
        w = sine(f, t) * 0.6 + square(f * 2, t) * 0.4
        s.append(int(AMP * 0.5 * env * w))
    save("sfx_bonus_prompt", s)

# ── Misc ────────────────────────────────────────────────────
def gen_dodge():
    n = int(SR * 0.25)
    s = []
    for i in range(n):
        t = i / SR
        env = envelope(t, 0.25, 0.002, 0.08)
        f = freq_sweep(300, 1800, t, 0.25)
        w = sine(f, t) * 0.5 + sine(f * 1.5, t) * 0.3 + noise() * 0.2
        s.append(int(AMP * 0.5 * env * w))
    save("sfx_dodge", s)

def gen_heal():
    n = int(SR * 0.7)
    s = []
    notes = [523, 659, 784, 1047, 784, 1047]
    for i in range(n):
        t = i / SR
        env = envelope(t, 0.7, 0.01, 0.25)
        f = arpeggio(notes, t, 0.7)
        w = sine(f, t) * 0.6 + tri(f * 2, t) * 0.4
        s.append(int(AMP * 0.6 * env * w))
    save("sfx_heal", s)

# ── BGM ─────────────────────────────────────────────────────
def gen_bgm_battle():
    """16s looping battle BGM — driving bass + melody."""
    n = int(SR * 16)
    bpm = 140
    beat_s = 60 / bpm
    s = []
    # bass pattern: C2(65) G2(98) A2(110) E2(82) — loop every 4 beats
    bass_notes = [65, 98, 110, 82]
    melody_notes = [262, 294, 330, 349, 392, 349, 330, 294]  # C4 D4 E4 F4 G4 F4 E4 D4
    for i in range(n):
        t = i / SR
        env = min(1.0, t / 0.05)  # fade in 50ms
        beat = t / beat_s
        # bass on every beat
        bass_f = bass_notes[int(beat) % len(bass_notes)]
        bass_w = square(bass_f, t) * 0.4 + saw(bass_f * 0.5, t) * 0.3
        # melody on half-notes (slower)
        mel_f = melody_notes[int(beat * 0.5) % len(melody_notes)]
        mel_w = square(mel_f, t) * 0.3 + tri(mel_f * 2, t) * 0.2
        # percussion noise clicks
        click = 0
        if abs(beat - round(beat)) < 0.02:
            click = noise() * 0.3
        w = bass_w + mel_w + click
        s.append(int(AMP * 0.5 * env * w))
    save("bgm_battle", s)

def gen_bgm_menu():
    """16s looping menu BGM — calm, atmospheric."""
    n = int(SR * 16)
    s = []
    chords = [262, 330, 392, 523]  # C4 E4 G4 C5
    pad_notes = [196, 220, 262, 294]  # G3 A3 C4 D4
    for i in range(n):
        t = i / SR
        env = min(1.0, t / 0.1)
        chord_f = chords[int(t * 2) % len(chords)]
        pad_f = pad_notes[int(t * 1.5) % len(pad_notes)]
        w = sine(chord_f, t) * 0.4 + sine(chord_f * 0.5, t) * 0.3 + sine(pad_f, t) * 0.2
        # gentle arpeggio flutter
        flutter_f = 440 + sine(6, t) * 100
        w += sine(flutter_f, t) * 0.1
        s.append(int(AMP * 0.35 * env * w))
    save("bgm_menu", s)

if __name__ == "__main__":
    random.seed(42)
    for name, fn in [
        ("Critical", gen_critical),
        ("Miss", gen_miss),
        ("Dash", gen_dash),
        ("StepGrass", gen_step_grass),
        ("Victory", gen_victory),
        ("Defeat", gen_defeat),
        ("TimerWarning", gen_timer_warning),
        ("BonusPrompt", gen_bonus_prompt),
        ("Dodge", gen_dodge),
        ("Heal", gen_heal),
        ("BGMBattle", gen_bgm_battle),
        ("BGMMenu", gen_bgm_menu),
    ]:
        fn()
        print(f"  {name:20s} OK")
    print("All sounds regenerated.")
