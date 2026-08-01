# Third-Person Parkour & Ladder Movement System

A Unity third-person character controller featuring physics-based movement, a modular obstacle-detection parkour system with animation target matching, and a ladder climbing system — all built around a shared "temporarily hand off control" architecture.

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Core Movement](#core-movement)
   - [PlayerController](#playercontroller)
   - [CameraController](#cameracontroller)
4. [Parkour System](#parkour-system)
   - [EnvironmentScanner](#environmentscanner)
   - [ParkourAction](#parkouraction)
   - [ParkourSystem](#parkoursystem)
   - [Target Matching Explained](#target-matching-explained)
5. [Ladder System](#ladder-system)
   - [Ladder](#ladder)
   - [LadderSystem](#laddersystem)
6. [Setup Guide](#setup-guide)
7. [Known Limitations & Possible Extensions](#known-limitations--possible-extensions)

---

## Overview

The project implements a third-person character that can walk, run, jump, vault/climb over obstacles contextually (via raycast detection), and climb ladders — with animations that are procedurally corrected at runtime so the character's hands and feet line up precisely with the geometry it's interacting with, rather than relying on generic, often-misaligned animation clips.

The design philosophy across every system here is the same:

1. A **detector** figures out what's around the player (raycasts, triggers).
2. A **decision layer** decides what action is possible/appropriate.
3. A **controller/coroutine** takes control away from normal movement, plays an animation, and — where needed — nudges the character's actual position/rotation to match the animation using Unity's `Animator.MatchTarget`.
4. Control is handed back to normal movement once the action finishes.

---

## Architecture

```
Player (GameObject)
 ├── CharacterController
 ├── Animator
 ├── PlayerController.cs      → basic locomotion, gravity, ground check
 ├── EnvironmentScanner.cs     → raycast-based obstacle detection
 ├── ParkourSystem.cs          → orchestrates parkour actions
 ├── LadderSystem.cs           → orchestrates ladder climbing
 └── (ParkourAction assets referenced by ParkourSystem)

Main Camera
 └── CameraController.cs       → orbit camera, feeds planar rotation to PlayerController

Ladder (GameObject, per ladder in the scene)
 ├── BoxCollider (Is Trigger)
 ├── Ladder.cs
 ├── BottomPoint (empty child)
 └── TopPoint (empty child)

ParkourAction (ScriptableObject asset, one per vaultable/climbable action)
```

Every "action" system (`ParkourSystem`, `LadderSystem`) follows the same control handshake with `PlayerController`:

```csharp
playerController.SetControl(false); // disables CharacterController, hands off to a coroutine
// ... coroutine drives transform/animator directly ...
playerController.SetControl(true);  // re-enables CharacterController, normal movement resumes
```

This is the key reason the systems can coexist without fighting each other over who owns the character's transform each frame.

---

## Core Movement

### PlayerController

Handles day-to-day locomotion:

- Reads `Horizontal`/`Vertical` input, converts it into a move direction relative to the camera's **planar rotation** (yaw only, so moving forward always means "forward relative to where the camera is looking," ignoring camera pitch).
- Uses `CharacterController.Move()` for horizontal movement and manually integrates a `ySpeed` value against `Physics.gravity.y` for falling, with a small constant downward speed (`-0.5f`) applied while grounded to keep the controller pressed against slopes.
- `GroundCheck()` uses `Physics.CheckSphere` against a configurable `groundCheckOffset`/`groundCheckRadius`/`groundLayer`.
- Rotates the character toward the movement direction with `Quaternion.RotateTowards`, at `rotationSpeed` degrees/sec.
- Feeds a `moveAmount` float into the Animator (damped via the built-in `Time.deltaTime` smoothing overload of `SetFloat`) to drive a locomotion blend tree.
- **`SetControl(bool)`** is the public hook every other system uses to take over: it disables/enables the `CharacterController` component itself and, when losing control, zeroes the animator's `moveAmount` and freezes `targetRotation` so there's no drift once control returns.
- Exposes `RotationSpeed` (used by `ParkourSystem` to rotate the player toward an obstacle) and `IsGrounded`.

### CameraController

A standard mouse-look orbit camera:

- Accumulates pitch (`rotationX`, clamped between `minVerticalAngle`/`maxVerticalAngle`) and yaw (`rotationY`, unclamped) from mouse input, with optional per-axis inversion.
- Positions itself at a fixed `distance` behind a `followTarget`, offset by `framingOffset` (useful for over-the-shoulder framing).
- Exposes **`PlanarRotation`** — a yaw-only quaternion — which is what `PlayerController` uses to translate raw input into world-space movement direction. This decoupling is what lets the player run "forward" consistently regardless of how far the camera is tilted up or down.

---

## Parkour System

### EnvironmentScanner

Pure detection, no decision-making. Casts two rays per check:

1. **Forward ray** — from a point offset above the player's feet (`forwardRayOffset`), forward along `transform.forward`, for `forwardRayLength`. This finds a wall/ledge/obstacle directly in front of the player.
2. **Height ray** — only cast if the forward ray hit something. Starts high above the forward hit point (`heightRayLength` above it) and casts straight down. This finds the *top surface* of whatever the forward ray detected, which tells the rest of the system how tall the obstacle is.

Both hits (and whether they were found) are packed into an `ObstacleHitData` struct and returned. `Debug.DrawRay` visualizes both rays in the Scene view (red = hit, white = miss) for tuning ray length/offsets.

### ParkourAction

A `ScriptableObject` — meaning each parkour move (vault over a low wall, climb onto a ledge, hop a gap, etc.) is authored as a reusable **data asset** in the Project window (`Create → Scriptable Objects → ParkourAction`), not hardcoded logic. This lets designers add/tune new moves without touching code.

Each asset defines:

| Field | Purpose |
|---|---|
| `animName` | Name of the Animator state to crossfade into |
| `obstacleTag` | If set, the obstacle's collider must have this tag for the action to be valid (lets you restrict, e.g., "Vault" to only tagged low walls) |
| `minHeight` / `maxHeight` | The obstacle's height (relative to the player's feet) must fall in this range |
| `rotateToObstacle` | Whether the player should rotate to directly face the obstacle before/during the animation |
| `enableTargetMatching` | Whether to run `Animator.MatchTarget` correction during the animation |
| `matchBodyPart` | Which `AvatarTarget` (e.g. hand, foot) gets corrected |
| `matchStartTime` / `matchTargetTime` | Normalized animation time window over which the correction is applied |
| `matchPosWeight` | Per-axis blend weight for how strongly position is corrected |
| `postActionDelay` | Extra wait time after the animation before control returns |

**`checkIfPossible(hitData, player)`** is the decision function: it rejects the action if the tag doesn't match, or if the detected obstacle height is outside `[minHeight, maxHeight]`. If it passes, it also *computes and caches* `TargetRotation` (facing away from the obstacle's surface normal) and `MatchPos` (the exact point the animation should align to) — these are read back by `ParkourSystem` during playback.

### ParkourSystem

The orchestrator:

- On `Jump` input (when not already `inAction`), calls `EnvironmentScanner.ObstacleCheck()`.
- If an obstacle is found, iterates the assigned list of `ParkourAction` assets in order and runs the **first** one whose `checkIfPossible` returns true — meaning **asset order in the Inspector list matters** (put more specific/restrictive actions first).
- If no obstacle is found, falls back to `DoNormalJump()` — a simple crossfade into a `Jump` state that waits until the Animator has left that state before returning control.
- If a matching action is found, runs `DoParkourAction(action)`:
  1. Disables normal control, crossfades into the action's animation.
  2. Each frame for the duration of the clip: optionally rotates the player toward `action.TargetRotation`, and (if enabled) calls `MatchTarget` for procedural correction.
  3. Breaks early if the Animator has moved into a transition past the halfway point (`timer > 0.5f`), to avoid the coroutine holding control after the clip has effectively finished.
  4. Waits `postActionDelay`, then restores control.

#### Target Matching Explained

Unity's `Animator.MatchTarget` lets you correct an animation clip's playback at runtime so a specific body part lands exactly at a specific world-space point, instead of wherever the authored clip happens to place it. This matters because a single "climb ledge" animation is authored for one specific ledge height/position, but in a real level the player can approach a ledge at slightly different heights, distances, and angles every time.

How it's used here:

```csharp
animator.MatchTarget(
    action.MatchPos,                                  // target world position
    transform.rotation,                                // target rotation (kept as current)
    action.MatchBodyPart,                              // which body part (e.g. AvatarTarget.RightHand)
    new MatchTargetWeightMask(action.MatchPosWeight, 0), // per-axis position weight, no rotation weight
    action.MatchStartTime,                              // normalized time to start correcting (0–1)
    action.MatchTargetTime                              // normalized time to finish correcting (0–1)
);
```

- **`MatchPos`** is set inside `ParkourAction.checkIfPossible()` to `hitData.heightHit.point` — the exact point the environment scanner found on top of the obstacle. This is why detection and target matching are linked: the same raycast hit that determined "is this obstacle climbable?" also determines "exactly where should the hand/foot land?"
- **`matchPosWeight`** (a `Vector3`, e.g. `(0, 1, 0)`) controls which axes get corrected. A weight of `(0,1,0)` corrects only vertical (Y) position — useful when you trust the animation's horizontal reach but want the vertical alignment to be pixel/frame-perfect against varying obstacle heights. Increase X/Z weight if the player also needs horizontal correction (e.g. approaching a ledge at an angle).
- **`matchStartTime`/`matchTargetTime`** define the window, in normalized clip time (0 = start, 1 = end), over which the correction is blended in. E.g. `0.1`–`0.4` means: do nothing for the first 10% of the clip, smoothly correct position between 10%–40%, then let the rest of the clip play out uncorrected. These values are tuned per-animation by scrubbing the clip and noting where the hand/foot actually contacts the surface.
- **`animator.isMatchingTarget`** is checked before calling `MatchTarget` again each frame — Unity's target matching is stateful and re-calling it while already active will throw/behave unpredictably, so `ParkourSystem.MatchTarget()` guards against that.
- **`rotateToObstacle`** is a separate, simpler correction — it doesn't use `MatchTarget` at all, just directly rotates the transform toward `Quaternion.LookRotation(-hitData.forwardHit.normal)` (i.e., facing squarely into the obstacle's surface) over the course of the action, using `RotationSpeed` from `PlayerController`.

In short: **detection finds the point → the action asset caches it as `MatchPos` → the system feeds it into `MatchTarget` at the configured time window and weight → the animation's hand/foot placement is corrected to exactly match the real obstacle**, regardless of the player's exact approach angle/distance/height within the action's valid range.

---

## Ladder System

### Ladder

A marker component placed on the ladder's trigger `GameObject`, defining:

- `bottomPoint` / `topPoint` — Transforms marking where the player's feet should be at the base and near the top of the climb.
- `climbOffset` — how far in front of the ladder (along its forward vector) the player is locked to while climbing (the "rail" position).
- `RailPosition`, `TopY`, `BottomY` — computed helpers read by `LadderSystem`.

Gizmos draw a line between the two points and the rail offset for easy tuning in the Scene view.

### LadderSystem

Lives on the player, alongside `PlayerController`:

- Detects ladder overlap via `OnTriggerEnter`/`OnTriggerExit` (the player's `CharacterController` acts as the detecting collider — no extra collider needed on the player).
- On `F` press while inside the trigger, starts the `ClimbLadder` coroutine:
  1. Disables normal control, snaps the player onto the ladder's rail position and facing rotation.
  2. Crossfades into a `LadderClimb` blend tree driven by a `climbSpeed` float parameter, fed directly from `Input.GetAxis("Vertical")` each frame (negative = climbing down, positive = up).
  3. Moves `transform.position.y` directly based on that same input, while locking X/Z to the rail so the player can't drift off the ladder.
  4. Exits when: the player climbs above `TopY` (plays a `LadderExitTop` animation first), climbs below `BottomY` (exits immediately), or presses `F` again mid-climb.
  5. On any exit path, crossfades back into a named locomotion state and restores control via `SetControl(true)` — necessary because the ladder blend tree has no authored exit transitions of its own; the script fully owns entry and exit.

---

## Setup Guide

**Player:**
1. `CharacterController`, `Animator`, `PlayerController`, `EnvironmentScanner`, `ParkourSystem`, `LadderSystem` all on the same GameObject.
2. Assign a list of `ParkourAction` assets to `ParkourSystem` in the desired priority order.
3. Animator needs: a locomotion blend tree driven by `moveAmount`, a `Jump` state, one state per `ParkourAction.animName`, a `LadderClimb` 1D blend tree driven by `climbSpeed` (thresholds at -1/0/1 for down/idle/up), and a `LadderExitTop` state.

**Camera:**
1. `CameraController` on the Main Camera, with `followTarget` set to the player.

**Parkour Actions:**
1. `Create → Scriptable Objects → ParkourAction` for each move (vault, climb, etc.).
2. Set `minHeight`/`maxHeight` to the range of obstacle heights the animation looks good for, `obstacleTag` if you want to restrict it to specific obstacles, and the target matching fields as described above.
3. Tag climbable/vaultable geometry to match `obstacleTag`, and ensure it's on the layer assigned to `EnvironmentScanner`'s `obstacleLayer`.

**Ladders:**
1. Create a `Ladder` GameObject with a trigger `BoxCollider` sized to the climbable zone (including enough height at the top for the player to stand and press `F` to climb down).
2. Add `BottomPoint`/`TopPoint` empty children, positioned at the base and near-top of the ladder, and assign them in the `Ladder` component's Inspector.
3. Keep the ladder's *solid* collider (for physical blocking) separate from the trigger collider — don't mark the same collider as both, or the player will walk through the mesh.

---

## Known Limitations & Possible Extensions

- `ParkourSystem` only checks the **first** matching action per attempt (`break` on first success) — actions later in the list that could also apply are never considered, so ordering must be deliberate.
- `EnvironmentScanner` only casts straight forward from a fixed offset — it won't detect obstacles at an angle or ones that require the player to be closer/further than the fixed ray length.
- `LadderSystem`'s bottom-exit and manual-exit paths don't currently play a distinct "step off" animation the way top-exit does — only a crossfade back to locomotion. This is a straightforward addition if a dedicated clip exists.
- Ladder detection relies on the trigger collider fully covering both the climbing zone and the top standing platform; if the collider's top edge stops at the ladder's physical height, pressing `F` while standing on the platform above it won't register.
- `Animator.MatchTarget`'s correction weights and time windows are currently tuned per-asset via manual scrubbing — no automated validation exists to catch a poorly configured `matchStartTime`/`matchTargetTime` producing a visible pop or foot-slide.
