"""Procedural sound effects and music for Mist Isles.

Every sound is synthesised from oscillators and filtered noise, so the project has
no third-party audio licences to track. Run:
    python3 generate_audio.py <output folder>
Requires numpy and scipy.
"""
import sys
import os
import numpy as np
from scipy.signal import butter, sosfilt
from scipy.io import wavfile

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else "."
rng = np.random.default_rng(7)


def t_axis(sec):
    return np.arange(int(SR * sec)) / SR


def env(n, attack=0.005, decay=None, sec=None):
    """Linear attack, exponential decay envelope over n samples."""
    t = np.arange(n) / SR
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    d = np.exp(-t / decay) if decay else np.ones(n)
    return a * d


def lp(x, f, order=2):
    return sosfilt(butter(order, f, 'low', fs=SR, output='sos'), x)


def hp(x, f, order=2):
    return sosfilt(butter(order, f, 'high', fs=SR, output='sos'), x)


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], 'band', fs=SR, output='sos'), x)


def noise(sec):
    return rng.standard_normal(int(SR * sec))


def sine_sweep(f0, f1, sec):
    t = t_axis(sec)
    f = np.geomspace(f0, f1, len(t))
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def norm(x, peak=0.9):
    m = np.max(np.abs(x)) or 1
    return x / m * peak


def save(name, x, peak=0.9):
    x = norm(x, peak)
    fade = min(len(x) // 50, 200)
    x[-fade:] *= np.linspace(1, 0, fade)
    wavfile.write(os.path.join(OUT, name + ".wav"), SR, (x * 32767).astype(np.int16))
    print("wrote", name, round(len(x) / SR, 2), "s")


def note(freq, sec, kind="sine", decay=0.3, attack=0.005):
    t = t_axis(sec)
    if kind == "sine":
        w = np.sin(2 * np.pi * freq * t)
    elif kind == "tri":
        w = 2 * np.abs(2 * ((freq * t) % 1) - 1) - 1
    elif kind == "saw":
        w = 2 * ((freq * t) % 1) - 1
    return w * env(len(t), attack, decay)


def pluck(freq, sec, damp=0.996):
    """Karplus-Strong plucked string."""
    n = int(SR * sec)
    period = int(SR / freq)
    buf = rng.uniform(-1, 1, period)
    out = np.zeros(n)
    for i in range(n):
        out[i] = buf[i % period]
        buf[i % period] = damp * 0.5 * (buf[i % period] + buf[(i + 1) % period])
    return out


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# ------------------------------------------------------------------ combat
def sword_swing():
    sec = 0.32
    n = noise(sec)
    t = t_axis(sec)
    # band-pass sweeping from low to high gives the "whoosh"
    out = np.zeros_like(n)
    steps = 16
    for k in range(steps):
        s, e = k * len(n) // steps, (k + 1) * len(n) // steps
        f = 400 + 2600 * (k / steps)
        out[s:e] = bp(n, f * 0.6, f * 1.4)[s:e]
    shape = np.sin(np.pi * np.clip(t / sec, 0, 1)) ** 2
    return out * shape


def hit_flesh():
    sec = 0.22
    thump = sine_sweep(160, 55, sec) * env(int(SR * sec), 0.002, 0.07)
    click = hp(noise(sec), 2000) * env(int(SR * sec), 0.001, 0.015)
    return thump + 0.5 * click


def shell_clang():
    sec = 0.6
    t = t_axis(sec)
    parts = [(523, 1.0, 0.25), (1342, 0.6, 0.18), (2213, 0.4, 0.12), (3170, 0.3, 0.08), (4410, 0.2, 0.05)]
    x = sum(a * np.sin(2 * np.pi * f * t) * np.exp(-t / d) for f, a, d in parts)
    return x + 0.3 * hp(noise(sec), 3000) * env(len(t), 0.001, 0.01)


def player_hurt():
    sec = 0.3
    return sine_sweep(220, 70, sec) * env(int(SR * sec), 0.003, 0.09) + 0.4 * lp(noise(sec), 900) * env(int(SR * sec), 0.002, 0.05)


# ------------------------------------------------------------------ crabs
def chitter(count, sec, base=2400):
    x = np.zeros(int(SR * sec))
    for k in range(count):
        start = int(SR * (k * sec / (count + 0.5) + rng.uniform(0, 0.01)))
        c = bp(noise(0.025), base * rng.uniform(0.8, 1.2), base * 2.2) * env(int(SR * 0.025), 0.0005, 0.006)
        x[start:start + len(c)] += c[:len(x) - start]
    return x


def crab_click():
    return chitter(4, 0.28)


def crab_die():
    sec = 0.7
    x = chitter(7, sec, 1800)
    x *= np.linspace(1, 0.3, len(x))
    thump = sine_sweep(120, 45, 0.3) * env(int(SR * 0.3), 0.002, 0.1)
    x[int(SR * 0.35):int(SR * 0.35) + len(thump)] += thump
    return x


def crab_attack():
    sec = 0.3
    snap = hp(noise(0.04), 1500) * env(int(SR * 0.04), 0.0005, 0.008)
    x = np.zeros(int(SR * sec))
    x[int(SR * 0.12):int(SR * 0.12) + len(snap)] += snap * 1.5
    x += 0.4 * chitter(2, sec, 2000)
    return x


# ------------------------------------------------------------------ guardian
def guardian_roar():
    sec = 1.6
    t = t_axis(sec)
    f = 62 + 10 * np.sin(2 * np.pi * 3.5 * t) + 18 * np.exp(-t / 0.4)
    ph = 2 * np.pi * np.cumsum(f) / SR
    saw = sum(np.sin(k * ph) / k for k in range(1, 18))
    grit = lp(noise(sec), 700) * 0.6
    x = lp(saw + grit, 1400)
    shape = np.clip(t / 0.15, 0, 1) * np.clip((sec - t) / 0.5, 0, 1)
    return x * shape


def guardian_slam():
    sec = 1.3
    t = t_axis(sec)
    boom = sine_sweep(90, 30, sec) * np.exp(-t / 0.35)
    rumble = lp(noise(sec), 180) * np.exp(-t / 0.6) * 1.5
    crack = hp(noise(0.08), 1200) * env(int(SR * 0.08), 0.001, 0.02)
    x = boom + rumble
    x[:len(crack)] += crack
    return x


def guardian_armor_open():
    sec = 0.6
    hiss = bp(noise(sec), 2500, 7000) * env(int(SR * sec), 0.08, 0.25)
    tone = note(midi(81), sec, "sine", 0.3) * 0.3 + note(midi(88), sec, "sine", 0.25) * 0.2
    return hiss * 0.6 + tone


# ------------------------------------------------------------------ world / ui
def pickup():
    a = note(midi(88), 0.35, "sine", 0.12)
    b = note(midi(95), 0.3, "sine", 0.15)
    x = np.zeros(int(SR * 0.4))
    x[:len(a)] += a
    x[int(SR * 0.07):int(SR * 0.07) + len(b)] += b
    return x


def ui_click():
    return hp(noise(0.05), 1500) * env(int(SR * 0.05), 0.0005, 0.006) + note(1800, 0.05, "sine", 0.01) * 0.5


def quiz_correct():
    x = np.zeros(int(SR * 0.9))
    for k, m in enumerate((72, 76, 79, 84)):
        n = note(midi(m), 0.5, "tri", 0.25) * 0.6
        s = int(SR * 0.09 * k)
        x[s:s + len(n)] += n
    return x


def quiz_wrong():
    x = np.zeros(int(SR * 0.5))
    for k, m in enumerate((55, 50)):
        n = note(midi(m), 0.25, "saw", 0.12)
        s = int(SR * 0.16 * k)
        x[s:s + len(n)] += lp(n, 1200)
    return x


def mist_lift():
    sec = 3.0
    t = t_axis(sec)
    x = np.zeros(len(t))
    for k, m in enumerate((69, 72, 76, 79, 84, 88)):
        start = 0.25 * k
        n = note(midi(m), sec - start, "sine", 1.2, attack=0.2) * (0.6 + 0.4 * np.sin(2 * np.pi * 5 * t_axis(sec - start)))
        s = int(SR * start)
        x[s:s + len(n)] += n
    shimmer = bp(noise(sec), 5000, 9000) * np.exp(-t / 1.0) * 0.15
    return x + shimmer


def raft_splash():
    sec = 0.9
    t = t_axis(sec)
    return lp(noise(sec), 2500) * np.exp(-t / 0.18) + 0.5 * bp(noise(sec), 300, 900) * np.exp(-t / 0.4)


def tremor_warning():
    sec = 1.0
    t = t_axis(sec)
    return lp(noise(sec), 120) * np.sin(np.pi * t / sec) * 2 + note(45, sec, "saw", 0.6) * 0.3


# ------------------------------------------------------------------ loops
def make_loop(x, xfade_sec=1.5):
    """Crossfade the tail into the head so the clip loops without a seam."""
    n = int(SR * xfade_sec)
    head, body, tail = x[:n], x[n:-n], x[-n:]
    ramp = np.linspace(0, 1, n)
    return np.concatenate([tail * (1 - ramp) + head * ramp, body])


def ambient_ocean():
    sec = 26
    t = t_axis(sec)
    brown = np.cumsum(noise(sec))
    brown = hp(brown, 30)
    brown /= np.max(np.abs(brown))
    swell = 0.55 + 0.45 * np.sin(2 * np.pi * t / 6.5) ** 2
    waves = lp(brown, 900) * swell
    foam = bp(noise(sec), 2000, 6000) * (swell ** 4) * 0.08
    wind = bp(noise(sec), 300, 700) * (0.5 + 0.5 * np.sin(2 * np.pi * t / 11)) * 0.15
    return make_loop(waves + foam + wind)


def music_island():
    """Calm A-minor pentatonic loop: soft pad chords + plucked melody, 76 BPM, 8 bars."""
    bpm = 76
    beat = 60 / bpm
    bars = 8
    sec = bars * 4 * beat
    n = int(SR * sec)
    x = np.zeros(n + SR * 3)
    chords = [(57, 60, 64), (53, 57, 60), (48, 52, 55), (55, 59, 62)] * 2  # Am F C G
    for b, ch in enumerate(chords):
        s = int(SR * b * 4 * beat)
        for m in ch:
            p = note(midi(m), 4 * beat + 1.0, "tri", 2.5, attack=0.6) * 0.16
            x[s:s + len(p)] += lp(p, 1500)
        bass = note(midi(ch[0] - 12), 4 * beat, "sine", 1.5, attack=0.02) * 0.35
        x[s:s + len(bass)] += bass
    scale = [69, 72, 74, 76, 79, 81, 84]
    mel_rng = np.random.default_rng(11)
    for step in range(bars * 8):
        if mel_rng.random() < 0.55:
            m = scale[mel_rng.integers(0, len(scale))]
            p = pluck(midi(m), 1.2) * 0.35
            s = int(SR * step * beat / 2)
            x[s:s + len(p)] += p
    # wrap the reverb-ish tails back onto the start so the loop is seamless
    tail = x[n:]
    x = x[:n]
    x[:len(tail)] += tail
    return x


def music_guardian():
    """Tense loop for the guardian fight: low ostinato + drums, 112 BPM, 8 bars."""
    bpm = 112
    beat = 60 / bpm
    bars = 8
    sec = bars * 4 * beat
    n = int(SR * sec)
    x = np.zeros(n + SR * 2)
    kick = sine_sweep(110, 40, 0.35) * env(int(SR * 0.35), 0.001, 0.12)
    tom = sine_sweep(180, 90, 0.3) * env(int(SR * 0.3), 0.001, 0.1)
    hat = hp(noise(0.06), 7000) * env(int(SR * 0.06), 0.0005, 0.015)
    ost = [45, 45, 48, 45, 50, 45, 48, 52]
    for i in range(bars * 8):
        s = int(SR * i * beat / 2)
        if i % 4 == 0:
            x[s:s + len(kick)] += kick * 0.9
        if i % 8 == 6:
            x[s:s + len(tom)] += tom * 0.6
        x[s:s + len(hat)] += hat * 0.15
        m = ost[i % 8] + (0 if (i // 16) % 2 == 0 else -2)
        p = lp(note(midi(m), beat / 2 + 0.1, "saw", 0.15), 900) * 0.3
        x[s:s + len(p)] += p
    for b in range(bars):
        s = int(SR * b * 4 * beat)
        root = 57 if b % 2 == 0 else 55
        pad = lp(note(midi(root), 4 * beat + 0.5, "saw", 3.0, attack=0.3), 600) * 0.12
        x[s:s + len(pad)] += pad
    tail = x[n:]
    x = x[:n]
    x[:len(tail)] += tail
    return x


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for fn in [sword_swing, hit_flesh, shell_clang, player_hurt, crab_click, crab_die, crab_attack,
               guardian_roar, guardian_slam, guardian_armor_open, pickup, ui_click, quiz_correct,
               quiz_wrong, mist_lift, raft_splash, tremor_warning]:
        save(fn.__name__, fn())
    save("ambient_ocean", ambient_ocean(), 0.6)
    save("music_island", music_island(), 0.7)
    save("music_guardian", music_guardian(), 0.7)
