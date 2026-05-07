# Gaze Highlight Setup

## 1) Add scripts to scene
- Add `GazeHighlightController` to your XR rig root (or any persistent scene object).
- Keep `Head Transform` empty to auto-use `Camera.main`, or assign your HMD camera transform explicitly.

## 2) Create the filter contract
- Create a dedicated tag, for example: `GazeHighlight`.
- Create/use a dedicated layer for gaze-highlight candidates.
- Assign both the tag and layer to every object you want to be highlightable.

## 3) Mark targets
- Add `GazeHighlightTarget` to each highlightable object root.
- If the renderers are in children, leave `Include Children` enabled.

## 4) Controller tuning
- Set `Detection Mask` to the dedicated layer only.
- Set `Required Tag` to `GazeHighlight`.
- Start with:
  - `Max Distance`: `4`
  - `Max View Angle`: `20`
  - `Switch Cooldown Seconds`: `0.08`

## 5) Highlight style notes
- Preferred path: materials that expose `_OutlineColor` and `_OutlineWidth`.
- If outline shader properties are not present, the target falls back to `_EmissionColor` highlight.

## 6) Quick validation checklist
- Look at one tagged object -> highlight turns on.
- Look away -> highlight turns off after cooldown.
- Move gaze between two tagged objects -> only one stays highlighted.
- Put an occluder in front of target -> highlight does not trigger through occluder.
