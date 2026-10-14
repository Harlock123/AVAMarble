#!/usr/bin/env python3
"""Synthesises AVAMarble's sound effects and tunes into ../src/AVAMarble/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAMarble", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The marble.
    write("Bump.wav", mix(tone(0.08, 200, 110, "square", release=1.0), tone(0.06, 900, 300, "noise", release=1.0, gain=0.4)), 0.35)
    write("Land.wav", mix(tone(0.14, 150, 70, "sine", release=0.8), tone(0.1, 500, 150, "noise", release=1.0, gain=0.4)), 0.4)
    write("Break.wav", mix(tone(0.7, 5200, 2000, "noise", release=0.7), *[tone(0.6, f, kind="tri", release=1.4, gain=0.3) for f in [2600, 3300, 4100, 4900, 5600]]), 0.5)
    write("Fall.wav", tone(1.3, 1500, 180, "tri", release=0.3, vibrato=0.02), 0.35)
    write("Dissolve.wav", cat(*[mix(tone(0.07, 900 - k * 40, 400, "noise", release=0.8), tone(0.06, 300 + (k % 3) * 120, kind="sine", release=0.9, gain=0.5)) for k in range(12)]), 0.4)
    write("Gulp.wav", cat(tone(0.18, 300, 120, "square", release=0.5), tone(0.25, 120, 60, "sine", release=0.6)), 0.45)
    write("Clack.wav", mix(tone(0.07, 2200, 1500, "tri", release=1.2), tone(0.05, 1400, 600, "noise", release=1.2, gain=0.5)), 0.4)
    # The clock.
    write("Count.wav", tone(0.12, hz("A5"), kind="pulse", release=0.6), 0.35)
    write("Go.wav", tone(0.45, hz("A6"), kind="pulse", release=0.6), 0.4)
    write("Tick.wav", tone(0.05, 1800, kind="square", release=1.0), 0.25)
    write("TimeUp.wav", cat(*[tone(0.18, hz(n), kind="square", release=0.3) for n in ["E5", "C5", "A4"]], tone(0.6, hz("E4"), kind="square", release=0.5)), 0.4)
    write("Goal.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6"]], tone(0.5, hz("C7"), kind="pulse", release=0.6)), 0.4)

    # Rolling: a floating, mysterious waltz in D minor over arpeggios (new). Loops.
    arps = []
    for chord in [["D3", "A3", "F4"], ["D3", "A3", "F4"], ["Bb2", "F3", "D4"], ["Bb2", "F3", "D4"], ["G2", "D3", "Bb3"], ["A2", "E3", "C#4"], ["D3", "A3", "F4"], ["A2", "E3", "C#4"]] * 2:
        arps += [(chord[0], 1), (chord[1], 1), (chord[2], 1)]
    lead = [("A4", 2), ("D5", 1), ("F5", 2), ("E5", 1), ("D5", 2), ("C#5", 1), ("D5", 3),
            ("F5", 2), ("G5", 1), ("A5", 2), ("F5", 1), ("D5", 2), ("E5", 1), ("C#5", 3),
            ("A4", 2), ("D5", 1), ("F5", 2), ("A5", 1), ("Bb5", 2), ("G5", 1), ("A5", 3),
            ("G5", 2), ("F5", 1), ("E5", 2), ("C#5", 1), ("D5", 3), ("-", 3)]
    bass = seq(176, arps, kind="tri", gain=0.42, release=0.5)
    top = seq(176, lead, kind="sine", gain=0.32, release=0.4)
    write("Music.wav", mix(bass, top), 0.42)

    write("TitleTune.wav", tune(120,
                                [("D5", 1), ("F5", 0.5), ("A5", 1.5), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("C#5", 1), ("D5", 2),
                                 ("A4", 1), ("D5", 0.5), ("F5", 1.5), ("E5", 0.5), ("D5", 0.5), ("C#5", 1), ("A4", 1), ("D5", 2)],
                                [("D3", 2), ("Bb2", 2), ("G2", 2), ("A2", 2), ("D3", 2), ("G2", 2), ("A2", 2), ("D3", 2)], kind="tri"), 0.45)
    write("StartTune.wav", tune(150, [("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 1.5), ("F5", 0.5), ("A5", 2)], [("D3", 2), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("A5", 1), ("G5", 1), ("F5", 1), ("E5", 1), ("D5", 3)], [("D4", 2), ("Bb3", 2), ("A3", 1), ("D3", 3)], kind="tri"), 0.45)
    write("Victory.wav", tune(150, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1), ("A5", 0.5), ("D6", 0.5), ("F#6", 0.5), ("A6", 2), ("F#6", 0.5), ("D6", 2)],
                              [("D3", 1), ("A2", 1), ("D3", 1), ("F#3", 1), ("A3", 2), ("D3", 2)], kind="pulse"), 0.45)


if __name__ == "__main__":
    main()
