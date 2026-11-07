# Adaptive Arena game design

## First playable duel

The prototype is a single top-down arena with walls, two spawn points, a human player, and one opponent. Move with WASD, aim with the mouse, fire with the left mouse button, and heal with Q. Both fighters use the same movement, attack, and health rules. Projectiles deal damage on valid hits. A knockout ends the round, while a double knockout or time limit is a draw. The HUD shows health, time, heal availability, and round outcome. A restart restores the initial state.

Learning happens offline between builds. A match runs a fixed policy so its behavior can be reproduced and compared with a scripted opponent.

### Heal

For the first prototype, give each fighter 100 maximum HP and one heal charge per round. A valid heal immediately restores up to 30 HP, capped at maximum HP, and spends the charge. A full-health fighter, a knocked-out fighter, or a fighter with no charge cannot heal. Failed attempts spend nothing. Apply damage before resolving heal requests on the same simulation tick so a lethal hit cannot be reversed. Reset HP and the charge at round start. Expose the charge in the HUD and to the AI for its own fighter. Tune the HP and heal amount after playtesting.

## Architecture

Keep game code under `Assets/_Game/Scripts/` as it is added.

| Area | Responsibility |
| --- | --- |
| `Core/` | Arena bounds, round lifecycle, spawn selection, timer, and reset |
| `Combat/` | Fighter movement, weapon and projectile rules, health, damage, and healing |
| `Player/` | Translate Unity Input System events into movement, aim, fire, and heal requests |
| `AI/` | Produce the same requests from a scripted policy or an ML-Agents adapter |
| `UI/` | Display round and fighter state |

Controllers submit requests through the same fighter interface. Combat owns validation and emits resolved events for the HUD and trainer. Apply requests on a fixed simulation step. The arena reset clears projectiles, velocities, pending inputs, timers, and combat state. The ML-Agents adapter reads observations and reports rewards from resolved events, keeping the simulation usable without a trainer. Put reusable fighter and projectile prefabs in `Assets/_Game/Prefabs/` when they exist.

## Development plan

| Milestone | Exit check |
| --- | --- |
| 1. Arena and movement | The player moves within bounds and aims at the cursor in Play Mode. |
| 2. Combat and heal | Two fighters can complete and restart a round. Verify HP capping, invalid heals, lethal-hit ordering, timeout, and reset. |
| 3. Scripted opponent | A scripted fighter can move, aim, fire, and heal through the shared interface. Record baseline results and playtest the duel. |
| 4. Training integration | Pin compatible Unity, Python, and ML-Agents versions. Run a short PPO smoke test and confirm episode resets, terminal rewards, and checkpoint export. |
| 5. Evaluation and playtest | Replay fixed checkpoints against held-out scenarios, compare with scripted baselines, and test the selected checkpoint in a human-playable build. |

## Training

- Begin with PPO against one fixed scripted opponent. Vary spawn positions and opponent behavior during training after the basic duel produces reliable episodes.
- Observe normalized relative position and velocity, own and opponent HP, own heal availability and weapon readiness, nearby wall distances, and visible projectile threats. Restrict observations to information available under the game rules.
- Predict continuous movement and aim inputs and discrete fire and heal requests. Route every action through the same validation as player inputs.
- Start with terminal rewards of +1 for a win, −1 for a loss, and 0 for a draw. If adding damage-based shaping, measure its impact separately. Avoid rewarding heal requests or restored HP directly.
- Record the commit, config, versions, seeds, scenario settings, checkpoint, and environment build for each run. After training is wired up, launch from `training/` with a unique run ID. Keep raw runs under `training/results/` and local environment builds under `training/envs/`. Track configs, dependency pins, and experiment summaries. Use the toolkit's release and installation guidance when choosing compatible versions. [Unity ML-Agents Toolkit](https://github.com/Unity-Technologies/ml-agents)

## Evaluation

Freeze the checkpoint and use held-out seeds, spawns, and opponent settings. Compare with the scripted baseline using the same evaluation opponents, episode limits, and matched seeds with sides swapped. Keep a separate validation set for checkpoint selection. Record match count, win/loss/draw rates, round duration, damage dealt and taken, heal use and effective HP restored, and the frequency of unused heals at knockout.

Report results per scenario and across independent training seeds. Inspect replays for stalling, wall trapping, and repetitive behavior. In human playtests, check whether healing feels fair, attacks are readable, and the opponent responds in ways a player can learn to counter.
