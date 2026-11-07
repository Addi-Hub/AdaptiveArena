# Adaptive Arena

Adaptive Arena is a planned top-down 2D Unity duel where you fight an opponent that learns arena tactics through reinforcement learning. The first goal is a playable match against a scripted opponent, followed by a comparison against a trained policy under the same combat rules.

## Open the project

1. Clone the repository. Set up Git LFS with `git lfs install` before cloning if you will work with binary assets tracked through the repository's LFS rules.
2. Install Unity **6000.3.9f1** via Unity Hub, as recorded in [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt).
3. Add the repository root to Unity Hub and open it. Allow Unity to restore the packages in [the manifest](Packages/manifest.json).
4. Open `Assets/_Game/Scenes/SampleScene.unity` and enter Play Mode to check the 2D URP starter scene. The scene currently contains a camera and a global 2D light. Gameplay, ML-Agents, and training configuration are planned.

## Repository layout

| Path | Purpose |
| --- | --- |
| `Assets/_Game/Scenes/` | Game scenes, starting with the sample scene |
| `Assets/_Game/Settings/` | 2D renderer, URP asset, and scene template |
| `Assets/` | Also holds shared template assets, including input actions and URP global settings |
| `Packages/` and `ProjectSettings/` | Unity dependencies and project configuration |
| [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md) | Game rules, architecture, milestones, and experiment plan |

Add scripts, prefabs, and art under `Assets/_Game/` as they are implemented. Move Unity assets in the Project window or move each asset together with its `.meta` file to preserve references. Commit Unity metadata, settings, and package lock changes together with the corresponding project changes.

Training configs and dependency pins will live in `training/`. Evaluation scripts and compact summaries will live in `analysis/`. Local runs, environment builds, and raw analysis outputs have dedicated ignored directories.
