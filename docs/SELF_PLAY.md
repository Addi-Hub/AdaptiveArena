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
