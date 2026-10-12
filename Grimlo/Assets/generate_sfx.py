import math
import struct
import wave
import os

SAMPLE_RATE = 44100

def write_wav(filename, samples):
    os.makedirs(os.path.dirname(filename), exist_ok=True)
    with wave.open(filename, 'w') as wav_file:
        wav_file.setnchannels(1)  # mono
        wav_file.setsampwidth(2)  # 16-bit
        wav_file.setframerate(SAMPLE_RATE)
        
        # Clip and pack
        raw_data = bytearray()
        for s in samples:
            val = max(-1.0, min(1.0, s))
            int_val = int(val * 32767)
            raw_data.extend(struct.pack('<h', int_val))
        wav_file.writeframes(raw_data)
    print(f"Generated: {filename} ({len(samples)} samples, {len(samples)/SAMPLE_RATE:.2f}s)")

def generate_glass_tap(filename):
    """Resonant double-tap on thick glass: sharp transient + harmonic ringing."""
    duration = 0.35
    total_samples = int(SAMPLE_RATE * duration)
    samples = [0.0] * total_samples
    
    taps = [0.0, 0.08] # two quick knuckle taps
    tap_gains = [0.85, 0.95]
    
    for tap_idx, tap_time in enumerate(taps):
        start_samp = int(tap_time * SAMPLE_RATE)
        gain = tap_gains[tap_idx]
        
        for i in range(start_samp, total_samples):
            t = (i - start_samp) / SAMPLE_RATE
            # Sharp decay envelope
            env = math.exp(-18.0 * t)
            # Glass ring frequencies: 1760 Hz (A6), 2640 Hz (E7), 3520 Hz (A7), 5280 Hz
            ring = (
                0.45 * math.sin(2 * math.pi * 1760 * t) +
                0.30 * math.sin(2 * math.pi * 2640 * t) +
                0.15 * math.sin(2 * math.pi * 3520 * t) +
                0.10 * math.sin(2 * math.pi * 5280 * t)
            )
            # Impact thud transient (low resonant thud of glass frame)
            thud = 0.5 * math.sin(2 * math.pi * 220 * t) * math.exp(-45.0 * t)
            
            samples[i] += gain * (ring + thud) * env
            
    # Normalize peak
    peak = max(abs(s) for s in samples) or 1.0
    samples = [s / peak * 0.85 for s in samples]
    write_wav(filename, samples)

def generate_bubble_pop(filename):
    """Playful cartoon bubble pop / bloop: fast upward pitch chirp + soft pop click."""
    duration = 0.12
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        progress = t / duration
        
        # Frequency rises rapidly from 380Hz to 1100Hz
        freq = 380 + 720 * (progress ** 1.8)
        phase = 2 * math.pi * (380 * t + 720 * (progress ** 2.8) * duration / 2.8)
        
        # Envelope: punchy rise then fast decay
        env = math.sin(math.pi * (progress ** 0.5)) * math.exp(-12.0 * progress)
        
        # Add slight 2nd harmonic warmth
        tone = math.sin(phase) + 0.25 * math.sin(phase * 2)
        samples.append(tone * env * 0.8)
        
    peak = max(abs(s) for s in samples) or 1.0
    samples = [s / peak * 0.8 for s in samples]
    write_wav(filename, samples)

def generate_shake_dizzy(filename):
    """Cartoon dizzy wobble: fast vibrato pitch bend descending with springy warble."""
    duration = 0.45
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    
    phase = 0.0
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        progress = t / duration
        
        # Base pitch sweeps down slightly 580 -> 320 Hz
        base_freq = 580 - 260 * progress
        # Wobble speed accelerates 16Hz -> 28Hz
        wobble_rate = 16.0 + 12.0 * progress
        # Vibrato depth
        wobble_depth = 110.0 * (1.0 - progress * 0.4)
        
        inst_freq = base_freq + wobble_depth * math.sin(2 * math.pi * wobble_rate * t)
        phase += 2 * math.pi * inst_freq / SAMPLE_RATE
        
        env = (1.0 - progress) * (1.0 - math.exp(-25.0 * t))
        tone = 0.7 * math.sin(phase) + 0.2 * math.sin(phase * 2) + 0.1 * math.sin(phase * 3)
        samples.append(tone * env * 0.75)
        
    peak = max(abs(s) for s in samples) or 1.0
    samples = [s / peak * 0.8 for s in samples]
    write_wav(filename, samples)

def generate_wake_chitter(filename):
    """Playful gremlin waking chirps: 3 staccato musical clicks/trills rising in tone."""
    duration = 0.42
    total_samples = int(SAMPLE_RATE * duration)
    samples = [0.0] * total_samples
    
    chirps = [
        (0.00, 520, 0.08, 0.6),
        (0.10, 740, 0.09, 0.75),
        (0.22, 1040, 0.14, 0.85)
    ]
    
    for start_t, freq, ch_dur, ch_gain in chirps:
        start_samp = int(start_t * SAMPLE_RATE)
        end_samp = min(total_samples, start_samp + int(ch_dur * SAMPLE_RATE))
        phase = 0.0
        
        for i in range(start_samp, end_samp):
            local_t = (i - start_samp) / SAMPLE_RATE
            p = local_t / ch_dur
            # Pitch chirp upward
            f = freq + 300 * p
            phase += 2 * math.pi * f / SAMPLE_RATE
            env = math.sin(math.pi * p) * math.exp(-4.0 * p)
            samples[i] += ch_gain * (math.sin(phase) + 0.2 * math.sin(phase * 2)) * env
            
    peak = max(abs(s) for s in samples) or 1.0
    samples = [s / peak * 0.8 for s in samples]
    write_wav(filename, samples)

def generate_click_blip(filename):
    """Snappy subtle UI blip / tick."""
    duration = 0.04
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        progress = t / duration
        # Fast descending chirp 1400 -> 600 Hz
        freq = 1400 - 800 * progress
        env = math.exp(-35.0 * progress)
        tone = math.sin(2 * math.pi * freq * t)
        samples.append(tone * env * 0.6)
        
    peak = max(abs(s) for s in samples) or 1.0
    samples = [s / peak * 0.75 for s in samples]
    write_wav(filename, samples)

if __name__ == "__main__":
    out_dir = r"c:\Users\noahd\Documents\grimlo\grimlo\Grimlo\Assets\Audio"
    generate_glass_tap(os.path.join(out_dir, "tap_glass.wav"))
    generate_bubble_pop(os.path.join(out_dir, "bubble_pop.wav"))
    generate_shake_dizzy(os.path.join(out_dir, "shake_dizzy.wav"))
    generate_wake_chitter(os.path.join(out_dir, "wake_chitter.wav"))
    generate_click_blip(os.path.join(out_dir, "click_blip.wav"))
    print("All audio files successfully generated!")
