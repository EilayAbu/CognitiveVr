# Gaze Glow Setup

A single component, `GazeGlow`, lives on every object that should glow when the player looks at it. There is no controller, no tag, no layer, and no physics raycast.

## How detection works
Each frame the component:
1. Reads the head transform (Camera.main by default, or an explicit override).
2. Computes the direction from the camera position to this object's position.
3. Computes the angle between the camera forward and that direction.
4. Turns the highlight ON when the angle drops below `Enter Angle Degrees` and OFF when it rises above `Exit Angle Degrees` (hysteresis prevents flicker at the edges).

## Setup steps
1. Add the `GazeGlow` component to any object you want to glow.
2. (Optional) If `Camera.main` is not the HMD camera, drag the correct head transform (for example `CenterEyeAnchor`) into `Head Transform Override`.
3. Tune in the Inspector:
   - `Enter Angle Degrees` (default `12`)
   - `Exit Angle Degrees` (default `18`, must be >= enter)
   - `Use Distance Limit` + `Max Distance` (default `5` meters)

## Highlight style
- If the object's material exposes `_OutlineColor` and `_OutlineWidth`, the component drives those properties.
- Otherwise it falls back to `_EmissionColor` (URP/Standard PBR materials).
- Adjust `Outline Color`, `Outline Width`, `Fallback To Emission`, and `Emission Intensity` per object as needed.

## Validation checklist
- Look directly at the object: it lights up.
- Look slightly away (still inside `Exit Angle`): it stays lit (no flicker).
- Look beyond `Exit Angle`: it turns off.
- Walk further than `Max Distance`: it turns off.
- Enable `Verbose Logs` to print state changes to the console.
