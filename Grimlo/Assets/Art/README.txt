Grimlo Mascot Art Folder
========================

Drop your PNG image files here!
Grimlo supports BOTH single images AND animated multi-frame sequences.

Naming Conventions:
------------------
1. IDLE (Default floating state):
   - Single image: idle.png (or grimlo_idle.png)
   - Multi-frame sequence: idle_0.png, idle_1.png, idle_2.png ...

2. TALKING (When Grimlo is speaking out loud):
   - Single image: talk.png (or talking.png)
   - Multi-frame sequence: talk_0.png, talk_1.png, talk_2.png ...

3. THINKING (While waiting for AI response):
   - Single image: think.png (or thinking.png)
   - Multi-frame sequence: think_0.png, think_1.png ...

4. SLEEPING (After 2 minutes of no user activity):
   - Single image: sleep.png (or sleeping.png)
   - Multi-frame sequence: sleep_0.png, sleep_1.png ...

Recommended Specs:
------------------
- Format: Transparent 32-bit PNG (with alpha transparency)
- Resolution: ~256x256 to 512x512 (scales crisply with high-quality GPU bilinear/bicubic sampling)
- Canvas size: Keep canvas dimensions consistent across all animation frames so Grimlo doesn't jump around.

Fallback:
---------
- If a state is missing (e.g. no sleep art yet), it falls back to Idle.
- If no art files exist in this folder, Grimlo automatically shows the built-in placeholder vector shapes.
