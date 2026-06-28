# Project Overview

- **Game Title:** CognitiveVr
- **High-Level Concept:** A VR experience set in a studio apartment (Apartment Scene 1). This task adds an interactable window whose 4 panes can each be opened individually.
- **Players:** Single player (VR)
- **Inspiration / Reference Games:** N/A (apartment simulation / cognitive VR experience)
- **Tone / Art Direction:** Realistic interior (Studio Apartment 2 asset pack)
- **Target Platform:** Android (Meta Quest via Meta XR SDK + OpenXR)
- **Screen Orientation / Resolution:** VR HMD
- **Render Pipeline:** URP (PC_RPAsset active)

# Goal

Recreate the existing `Room/Room/Window (1)` object as a **new** object whose window panes are **separate, individually-openable pieces** instead of one fused mesh. The original `Window (1)` stays untouched.

## Source object analysis (already done)

`Window (1)` (InstanceID 146752) is a prefab instance of `Assets/Studio Apartment 2/Meshes/Window.fbx`, world position `(2.65, 0.85, -1.90)`, rotation `(0,0,0)`. It contains two fused child meshes:

| Child | Mesh | Verts | SubMeshes | Materials |
|-------|------|-------|-----------|-----------|
| `Window` | Window (frame/mullions) | 10025 | 2 | `Window`, `Metall` |
| `Window Glass` | Window Glass | 40 | 1 | `Windows Glass` |

Material asset paths:
- `Assets/Studio Apartment 2/Meshes/Materials/Window.mat`
- `Assets/Studio Apartment 2/Meshes/Materials/Metall.mat`
- `Assets/Studio Apartment 2/Meshes/Materials/Windows Glass.mat`

The glass mesh decomposes into **4 distinct panes** (a 2×2 grid). Confirmed pane centers (local space, X is depth/thin axis, Z is width, Y is height):

| Pane | Z (column) | Y (row) | Approx. local bounds |
|------|-----------|---------|----------------------|
| Bottom-Left (BL)  | +0.55 | 0.43 | short bottom pane |
| Bottom-Right (BR) | -0.55 | 0.43 | short bottom pane |
| Top-Left (TL)     | +0.55 | 1.42 | tall top pane |
| Top-Right (TR)    | -0.55 | 1.42 | tall top pane |

**Natural split lines:** Z = 0 (center mullion) splits left/right columns; Y ≈ 0.78 (mid horizontal mullion) splits bottom/top rows. The window is thin along local X; the outer vertical edges sit at Z ≈ ±1.05.

## Confirmed decisions (from user)
- **Split:** 2×2 grid → 4 separate panes.
- **Behavior:** No VR interaction components (separated GameObjects only).
- **Original:** Keep `Window (1)`; create a new `Window (Separated)` alongside it.

# Game Mechanics

## Core Gameplay Loop
The player explores the apartment. The window panes are now individual, separate GameObjects in the scene hierarchy. They can be moved, rotated, customized, or animated individually in the editor or via simple standard scripts without any complex physics setups.

## Controls and Input Methods
- None required. Panes are simple static scene elements with BoxColliders.

# UI
No UI changes. This is a 3D model only.

# Key Asset & Context

## New assets to be created
- **Folder:** `Assets/Studio Apartment 2/Meshes/WindowSeparated/`
- **8 split mesh assets** (`.asset`): `Window_BL`, `Window_BR`, `Window_TL`, `Window_TR` (frame, 2 submeshes each) and `Glass_BL`, `Glass_BR`, `Glass_TL`, `Glass_TR` (glass, 1 submesh each).
- **Editor tool script:** `Assets/Editor/WindowSeparator.cs` — performs the mesh split, saves assets, and builds the scene object. Runs from a menu item `Tools/Window/Create Separated Window`.

## Resulting scene hierarchy
```
Window (Separated)               (root, same transform as Window (1): pos 2.65,0.85,-1.90)
├── Pane BL                      (BoxCollider)
│   ├── Frame  (MeshFilter=Window_BL, MeshRenderer mats=[Window, Metall])
│   └── Glass  (MeshFilter=Glass_BL, MeshRenderer mats=[Windows Glass])
├── Pane BR  (same structure, Window_BR / Glass_BR)
├── Pane TL  (same structure, Window_TL / Glass_TL)
└── Pane TR  (same structure, Window_TR / Glass_TR)
```

## Mesh-splitting technique (in WindowSeparator.cs)
For each of the two source meshes, assign every triangle to one of the 4 quadrants by its **centroid** (Z>0 vs Z<0, Y>splitY vs Y<splitY, with `splitY ≈ 0.78`). For each quadrant build a new `Mesh`:
- Iterate submeshes separately so material/submesh mapping is preserved (frame → 2 submeshes kept; glass → 1 submesh).
- Remap referenced vertices into a compact new vertex array, copying `vertices`, `normals`, `tangents`, `uv`, `uv2`, `colors` when present.
- Recalculate bounds. Save mesh via `AssetDatabase.CreateAsset`.

Key code references:
```csharp
// Triangle centroid quadrant test:
Vector3 c = (v0 + v1 + v2) / 3f;
bool right = c.z >= 0f;          // +Z column
bool top   = c.y >= splitY;      // splitY ~ 0.78
```

## Interaction setup per pane (casement / vertical hinge)
No VR components (`Rigidbody`, `HingeJoint`, `XRGrabInteractable`) are added. Each pane is a regular static GameObject with standard components, so they can be animated, positioned, or modified freely in the editor or via general scripting.
- `BoxCollider`: sized/centered to the pane's split-mesh bounds (needed for basic raycasting / editor selection).


**Known visual note:** Because the center & mid mullions are shared geometry, the centroid split assigns each mullion's faces to whichever pane they fall in. When panes open independently the shared mullion edge separates with them. This is inherent to making the window "not in one piece." *Alternative if undesired:* keep the full frame static (one clone of the original `Window` mesh, non-interactive) and split only the 4 glass panes into openable pieces — cleaner seams, frame stays put. Flagged here for your call; default plan splits frame+glass together per the "separate windows, not one piece" request.

# Implementation Steps

### Step 1 — Create the editor splitter tool
- **Description:** Create `Assets/Editor/WindowSeparator.cs` with a `[MenuItem("Tools/Window/Create Separated Window")]`. It (a) loads the source meshes from `Assets/Studio Apartment 2/Meshes/Window.fbx`, (b) splits each into 4 quadrant meshes preserving submeshes/vertex streams, (c) writes the 8 `.asset` files into `Assets/Studio Apartment 2/Meshes/WindowSeparated/`.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** No

### Step 2 — Build the scene object in the tool
- **Description:** Extend `WindowSeparator.cs` to create the `Window (Separated)` root at `Window (1)`'s world transform, then create the 4 `Pane X` GameObjects each with `Frame` + `Glass` mesh children, assigning the saved split meshes and the original materials (`Window`, `Metall`, `Windows Glass`).
- **Assigned role:** developer
- **Dependencies:** Step 1
- **Parallelizable:** No

### Step 3 — Add basic components
- **Description:** In the tool, add a basic `BoxCollider` (fit to pane bounds) so each pane has individual physical bounds and can be interacted with, raycasted, or selected easily.
- **Assigned role:** developer
- **Dependencies:** Step 2
- **Parallelizable:** No

### Step 4 — Run the tool & verify in scene
- **Description:** Execute `Tools/Window/Create Separated Window`. Confirm the new object appears overlapping `Window (1)` and looks visually identical when closed; confirm 4 separate panes exist with the components configured.
- **Assigned role:** developer
- **Dependencies:** Step 3
- **Parallelizable:** No

### Step 5 — (Optional) Convert to a reusable prefab
- **Description:** Save `Window (Separated)` as a prefab in `Assets/Studio Apartment 2/Prefabs/` so it can be reused, and (optionally, on your confirmation) replace other window instances.
- **Assigned role:** developer
- **Dependencies:** Step 4
- **Parallelizable:** No

# Verification & Testing
- **Visual parity:** With all panes closed, `Window (Separated)` should look identical to `Window (1)` from the room interior (no missing faces, materials correct). Toggle the original on/off to compare in Scene view.
- **Mesh integrity:** Each split frame mesh keeps 2 submeshes (Window + Metall) with correct material order; each glass mesh keeps 1 submesh. No console errors on `RecalculateBounds`.
- **Separation:** Selecting each `Pane X` in the Hierarchy highlights only one quadrant; moving it in the editor moves only that pane.
- **No regressions:** `Window (1)` and the rest of the scene remain unchanged.
