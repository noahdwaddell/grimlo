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
        
        raw_data = bytearray()
        for s in samples:
            val = max(-1.0, min(1.0, s))
            int_val = int(val * 32767)
            raw_data.extend(struct.pack('<h', int_val))
        wav_file.writeframes(raw_data)
    print(f"Generated: {filename} ({len(samples)} samples, {len(samples)/SAMPLE_RATE:.2f}s)")

def generate_snore_zzz(filename):
    """Comical soft gremlin snore: gentle resonant breathing drone with soft harmonic flutter."""
    duration = 1.35
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        progress = t / duration
        
        # Inhale swell (0.0 to 0.7s) then soft exhale (0.7 to 1.35s)
        if progress < 0.55:
            # Inhale snore
            p = progress / 0.55
            env = math.sin(math.pi * p) ** 1.8
            # Low buzzing pitch ~95 Hz with soft flutter
            freq = 95.0 + 8.0 * math.sin(2 * math.pi * 14.0 * t)
            phase = 2 * math.pi * freq * t
            # Rasp/buzz harmonics
            buzz = (
                0.6 * math.sin(phase) +
                0.3 * math.sin(phase * 2) +
                0.15 * math.sin(phase * 3) +
                0.08 * math.sin(phase * 4)
            )
            # Gentle breath noise
            breath = 0.05 * math.sin(2 * math.pi * 320 * t)
            samples.append((buzz + breath) * env * 0.45)
        else:
            # Exhale whistle/sigh
            p = (progress - 0.55) / 0.45
            env = math.sin(math.pi * p) ** 1.5
            freq = 180.0 - 40.0 * p
            tone = 0.4 * math.sin(2 * math.pi * freq * t)
            samples.append(tone * env * 0.25)
            
    peak = max(abs(s) for s in samples) or 1.0
    samples = [s / peak * 0.55 for s in samples]
    write_wav(filename, samples)

def generate_startle_wake(filename):
    """Sudden funny wake-up jolt: sharp intake gasp followed by a goofy micro-grunt/squeak."""
    duration = 0.28
    total_samples = int(SAMPLE_RATE * duration)
    samples = []
    
    for i in range(total_samples):
        t = i / SAMPLE_RATE
        progress = t / duration
        
        # Quick upward startle chirp (280Hz -> 650Hz -> 420Hz)
        if progress < 0.4:
            p = progress / 0.4
            freq = 280 + 370 * p
            env = math.sin(math.pi * 0.5 * p)
        else:
            p = (progress - 0.4) / 0.6
            freq = 650 - 230 * p
            env = math.cos(math.pi * 0.5 * p)
            
        phase = 2 * math.pi * freq * t
        tone = math.sin(phase) + 0.3 * math.sin(phase * 2)
        samples.append(tone * env * 0.7)
        
    peak = max(abs(s) for s in samples) or 1.0
    samples = [s / peak * 0.75 for s in samples]
    write_wav(filename, samples)

if __name__ == "__main__":
    out_dir = r"c:\Users\noahd\Documents\grimlo\grimlo\Grimlo\Assets\Audio"
    generate_snore_zzz(os.path.join(out_dir, "snore_zzz.wav"))
    generate_startle_wake(os.path.join(out_dir, "startle_wake.wav"))
    print("Sleep sound effects successfully generated!")
