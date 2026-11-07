# Self-play and baseline evaluation

Train both fighters through PPO self-play. Run multiple independent duels in parallel, with the learning policy facing recent frozen versions of itself. Keep the scripted Baseline Bot out of training and use it as the fixed opponent for evaluation.

At each evaluation checkpoint, freeze the learner and run 100 matches against the same version of Baseline Bot. Use held-out starting conditions, swap sides evenly, and record wins, losses, and draws. Divide wins by all matches, including draws. As an initial completion rule, require at least 70 wins in each of three consecutive checkpoint evaluations. Record the passing checkpoint and keep its policy for Play vs Bot. Treat the match count and threshold as configurable defaults.

The Training dashboard should show two sets of metrics.

| Self-play | Baseline evaluation |
| --- | --- |
| Active arenas, total steps, steps per second | Checkpoint and baseline version |
| Completed episodes and mean episode reward | Wins, losses, draws, and win rate |
| Latest checkpoint and elapsed training time | Recent checkpoint win rates and progress toward the three-pass rule |

Keep learning and evaluation separate so an evaluation match cannot update the policy. Save run configuration, checkpoint identifiers, and evaluation seeds with the results.

## Run the single-arena smoke test

Use Unity 6000.3.9f1 and Python 3.10.12. The Unity project requests ML-Agents 4.0.0 and the Python requirements pin the corresponding trainer. [Unity ML-Agents Release 23](https://github.com/Unity-Technologies/ml-agents/tree/release_23_tag)

1. Open the Unity project and let Package Manager resolve the new dependency. Open `Assets/_Game/Scenes/SelfPlayArena.unity` and leave Play Mode off.
2. With Python 3.10.12 active, run these commands from the project root in PowerShell.

   ```powershell
   python -m venv .venv
   .\.venv\Scripts\python.exe -m pip install -r training\requirements.txt
   .\.venv\Scripts\mlagents-learn.exe training\self-play-smoke.yaml --run-id=arena-smoke-01
   ```

3. When the trainer asks you to start the environment, press **Play** in Unity. Both fighters use the same behavior on different teams. Each knockout or 30-second timeout ends both episodes and resets the arena.
4. Check the console for trainer progress and the run folder `results/arena-smoke-01/` for exported `.onnx` files. Use a new run ID for a fresh smoke test.

The 10,000-step smoke run checks wiring and checkpoint creation. Use the separate baseline evaluation to judge policy strength.

## Previous draw-penalty experiment

Start a fresh run using the same Conda environment. The hit-reward configuration runs for 50,000 steps with checkpoints every 10,000 steps.

```powershell
$conda = "$env:USERPROFILE\anaconda3\Scripts\conda.exe"
& $conda run --no-capture-output -n adaptive-arena mlagents-learn training\self-play-hit-rewards.yaml --run-id=arena-draw-penalty-01
```

Use `SelfPlayArena.unity` and press **Play** when the trainer prompts you. A landed light or heavy hit gives the attacker +1 and the defender âˆ’1. A knockout adds +200 to the winner and âˆ’200 to the loser. A draw adds âˆ’100 to each fighter. Terminal rewards are added to the hit rewards accumulated during the round.

TensorBoard reports light and heavy attempts, hits per round, and knockout rate under `Arena/`. A swing counts as an attempt when its windup starts, even if the fighter cancels it. ML-Agents infers the Elo outcome from the sign of the final reward, so a penalized draw can register as a loss in Elo. Use the knockout rate and baseline evaluation results to compare this run.

## Compare damage rewards with normal and mixed starts

Run both configurations from scratch with the same seed. The normal configuration uses the scene's starting positions. The mixed configuration starts every other completed round with the fighters 1.4 units apart. Both run for 50,000 steps and use the same reward rules.

```powershell
$conda = "$env:USERPROFILE\anaconda3\Scripts\conda.exe"
& $conda run --no-capture-output -n adaptive-arena mlagents-learn training\self-play-damage-normal.yaml --run-id=arena-damage-normal-01 --seed=4107
# After the normal run finishes, stop Play Mode and start the mixed run.
& $conda run --no-capture-output -n adaptive-arena mlagents-learn training\self-play-damage-mixed.yaml --run-id=arena-damage-mixed-01 --seed=4107
```

Press **Play** in `SelfPlayArena.unity` when each trainer prompts you. Knockouts award +1 to the winner and âˆ’1 to the loser. Draws add zero. Every HP removed gives the attacker +0.003 and the defender âˆ’0.003. Compare hits per round and knockout rate under `Arena/Normal starts/` and `Arena/Close starts/`, then evaluate each final checkpoint against the same 100 Baseline Bot starts.

## Evaluate a frozen checkpoint

Stop the trainer and leave Unity Play Mode. From the project root, copy the smoke checkpoint into the local import folder.

```powershell
New-Item -ItemType Directory -Force Assets\_Game\LocalEvaluation | Out-Null
Copy-Item results\arena-smoke-01\ArenaDuel.onnx Assets\_Game\LocalEvaluation\ArenaDuel.onnx
```

Let Unity import the model, then open `Assets/_Game/Scenes/BaselineEvaluation.unity` and press **Play**. The evaluation uses deterministic inference and runs 100 matches at 10Ã— simulation speed. Each pair repeats the same starting heights with left and right sides swapped. Unity prints the win rate and writes one row per match to `results/baseline-evaluations/`, including the checkpoint hash, seed, and outcome. The win rate counts every match, including draws.

## Training dashboard

Click **Training** in the game's Main Menu or choose **Adaptive Arena â†’ Training Dashboard** in the Windows Unity Editor. Both open a separate dashboard window. Once the trainer is listening, Unity automatically enters Play Mode in SelfPlayArena. If you're playing a regular duel or running an evaluation, stop that session to let Unity switch before the trainer times out.

The launcher defaults to the Python executable in the `adaptive-arena` Anaconda environment and lets you select a different interpreter if it cannot find that executable. You can also open the dashboard directly from the project root with the same environment used for training.

```powershell
$conda = "$env:USERPROFILE\anaconda3\Scripts\conda.exe"
& $conda run --no-capture-output -n adaptive-arena python training\dashboard.py
```

- **Start new run** defaults to the existing damage rewards. Edit the signed win, loss, draw, damage dealt per HP, and damage taken per HP values in the dashboard. Each draw applies the draw reward to both fighters. Choose a new run ID, normal or mixed starts, seed, learning rate, checkpoint interval, and total steps. The defaults start a 500,000-step normal-start experiment.
- **Resume selected run** loads the trainer checkpoint and its saved configuration. Set **Total target steps** to the desired cumulative step, such as 500,000 for a run saved at 50,048. Enable **Apply these rewards when resuming** to replace the saved rewards. Other training fields apply only to new runs. Extending a run changes the horizon of its linear schedules, so a resumed run and a fresh long run are separate experiments.
- Leave Unity open while the dashboard starts the run. To end early, click **Stop Training** once Unity connects. Wait for the trainer to finish saving before starting another run. Dashboard runs disable worker restarts so Unity stays stopped after disconnecting. The dashboard controls one Editor-connected trainer at a time.
- Select a run to see progress, the latest reported reward, Elo, hits per round, knockout rate, and the most recent Baseline Bot report matching its exported model. Run the BaselineEvaluation scene separately as described above and use that same seed and match count for checkpoint comparisons.
