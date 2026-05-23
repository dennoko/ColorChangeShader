# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A VRChat-compatible color manipulation shader system for the VRCLV PhotoStudio project. It provides interactive 3D sliders to dynamically control HSV (Hue, Saturation, Value) color properties and emission intensity in real-time, with network synchronization for multiplayer VRChat environments.

## Architecture

### Core Components

**Shader** — [Shader/ColorChangeShader.shader](Shader/ColorChangeShader.shader)
- Converts HSV input to RGB using a branchless algorithm
- Exposes `_Hue`, `_Saturation`, `_Value`, `_Emission` properties (all 0–1 except emission up to 2 for HDR)
- Uses GPU instancing via `UNITY_INSTANCING_BUFFER` — supports `MaterialPropertyBlock` for per-object variation
- Renders as Background queue with `ZWrite Off, Cull Off` (skybox use case)

**MeshSlider** — [slider/MeshSlider.cs](slider/MeshSlider.cs)
- 3D physical slider that players grab in VRChat via `VRC_Pickup`
- All movement is calculated in parent local space; caches track vectors for performance
- Owner path: immediate local update → network sync via `UdonSynced`
- Remote path: receives `OnDeserialization()` → smooth Lerp interpolation (15f speed)
- Maps slider position (0–1) to a configurable min/max property range, then calls `Material.SetFloat()` or `MaterialPropertyBlock` depending on whether a `Renderer` or `Material` is targeted

**slider** — [slider/slider.cs](slider/slider.cs)
- Companion script for Unity UI `Slider` components (non-3D alternative)
- Same dual-target pattern (Renderer vs Material) and same network sync logic as MeshSlider
- Value formula: `Mathf.Lerp(minValue, maxValue, normalizedSliderValue)`

### Data Flow

```
Player grabs 3D handle (VRC_Pickup)
  → MeshSlider computes position along track in local space
  → Maps to property value, calls UpdateShader()
  → Material.SetFloat() or MaterialPropertyBlock updates ColorChangeShader
  → UdonSynced broadcasts value
  → Remote players receive OnDeserialization() → smooth Lerp to new position
```

### Prefab

[Prefab/slider_board.prefab](Prefab/slider_board.prefab) — a complete drop-in control panel with three `MeshSlider` buttons (Hue, Saturation, Emission) all targeting the `skybox` material.

Each button requires:
- `handleTransform` — visible knob mesh
- `pickupTransform` — interactive pickup object (can be same as handle)
- `trackStart` / `trackEnd` — empty GameObjects defining the rail

## Development Environment

- **Unity** with the VRChat Creator Companion SDK
- **UdonSharp** — C# scripts compile to `.asset` files; always recompile after editing `.cs`
- **Target**: VRChat world upload via VRChatCreatorCompanion

## Key Constraints

- `MaterialPropertyBlock` is preferred over `Material.SetFloat()` when targeting a `Renderer` — avoids creating material instances and reduces draw call overhead.
- Network sync uses manual mode (`Sync.Manual`); call `RequestSerialization()` explicitly after changing synced variables.
- Ownership must be transferred before writing synced variables — both scripts call `Networking.SetOwner(Networking.LocalPlayer, gameObject)` on pickup/interaction.
- All slider position math must stay in parent local space to work correctly with scaled or rotated parent transforms.
