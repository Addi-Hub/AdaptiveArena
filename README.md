# Adaptive Arena

Adaptive Arena is a planned top-down 2D Unity duel where you fight an opponent that learns arena tactics through reinforcement learning. The first goal is a playable match against a scripted opponent, followed by a comparison against a trained policy under the same combat rules.

## Open the project

1. Clone the repository. Set up Git LFS with `git lfs install` before cloning if you will work with binary assets tracked through the repository's LFS rules.
2. Install Unity **6000.3.9f1** via Unity Hub, as recorded in [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt).
3. Add the repository root to Unity Hub and open it. Allow Unity to restore the packages in [the manifest](Packages/manifest.json).
4. Open `Assets/_Game/Scenes/Arena.unity`, enter Play Mode, and focus the Game view. Move with WASD and aim with the mouse. Use left click for a light swing, right click for a heavy swing, and R to reset the practice scene. The target starts outside melee reach and darkens as it takes damage.

## Repository layout

| Path | Purpose |
| --- | --- |
| `Assets/_Game/Scenes/` | Arena prototype and original sample scene |
| `Assets/_Game/Settings/` | 2D renderer, URP asset, and scene template |
| `Assets/` | Also holds shared template assets, including input actions and URP global settings |
| `Packages/` and `ProjectSettings/` | Unity dependencies and project configuration |
| [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md) | Game rules, architecture, milestones, and experiment plan |

Add scripts, prefabs, and art under `Assets/_Game/` as they are implemented. Move Unity assets in the Project window or move each asset together with its `.meta` file to preserve references. Commit Unity metadata, settings, and package lock changes together with the corresponding project changes.

Training configs and dependency pins will live in `training/`. Evaluation scripts and compact summaries will live in `analysis/`. Local runs, environment builds, and raw analysis outputs have dedicated ignored directories.
