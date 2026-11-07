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

## Train with hit rewards

Start a fresh run using the same Conda environment. The hit-reward configuration runs for 50,000 steps with checkpoints every 10,000 steps.

```powershell
$conda = "$env:USERPROFILE\anaconda3\Scripts\conda.exe"
& $conda run --no-capture-output -n adaptive-arena mlagents-learn training\self-play-hit-rewards.yaml --run-id=arena-hits-01
```

Use `SelfPlayArena.unity` and press **Play** when the trainer prompts you. A landed light or heavy hit gives the attacker +1 and the defender −1. Knockouts add +100 to the winner and −100 to the loser. Draws add zero and keep any hit rewards. The TensorBoard `Arena/` metrics report light and heavy attempts, hits per round, and knockout rate. A swing counts as an attempt when its windup starts, even if the fighter cancels it.

## Evaluate a frozen checkpoint

Stop the trainer and leave Unity Play Mode. From the project root, copy the smoke checkpoint into the local import folder.

```powershell
New-Item -ItemType Directory -Force Assets\_Game\LocalEvaluation | Out-Null
Copy-Item results\arena-smoke-01\ArenaDuel.onnx Assets\_Game\LocalEvaluation\ArenaDuel.onnx
```

Let Unity import the model, then open `Assets/_Game/Scenes/BaselineEvaluation.unity` and press **Play**. The evaluation uses deterministic inference and runs 100 matches at 10× simulation speed. Each pair repeats the same starting heights with left and right sides swapped. Unity prints the win rate and writes one row per match to `results/baseline-evaluations/`, including the checkpoint hash, seed, and outcome. The win rate counts every match, including draws.
