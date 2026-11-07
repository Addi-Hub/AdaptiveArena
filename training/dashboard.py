"""Local Adaptive Arena trainer dashboard. Run with the ML-Agents Python environment."""
from collections import Counter
import csv
import hashlib
import math
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import threading
import time
import tkinter as tk
from tkinter import messagebox, ttk

import yaml
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "results"
PLAY_REQUEST = ROOT / "Library" / "ArenaTraining" / "play-request"
STOP_REQUEST = PLAY_REQUEST.with_name("stop-request")
EVALUATION_REQUEST = PLAY_REQUEST.with_name("evaluation-request")
EVALUATION_STATUS = PLAY_REQUEST.with_name("evaluation-status")
REWARDS = {
    "reward_win": ("Win", 1),
    "reward_loss": ("Loss", -1),
    "reward_draw": ("Draw", 0),
    "reward_damage_dealt": ("Per HP dealt", 0.003),
    "reward_damage_taken": ("Per HP taken", -0.003),
    "reward_hp_healed": ("Per HP healed", 0),
    "reward_attack_dodged": ("Per attack dodged", 0),
    "reward_face_opponent": ("Facing bonus / decision", 0),
}
RUN_ID = re.compile(r"[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}")
CHECKPOINT = re.compile(r"ArenaDuel-(\d+)\.pt")
METRICS = {
    "Environment/Cumulative Reward": "Mean reward",
    "Self-play/ELO": "Self-play Elo",
    "Arena/Light attempts per round": "Light swings / round",
    "Arena/Heavy attempts per round": "Heavy swings / round",
    "Arena/Light hits per round": "Light hits / round",
    "Arena/Heavy hits per round": "Heavy hits / round",
    "Arena/Normal starts/Hits per round": "Normal starts / hits",
    "Arena/Close starts/Hits per round": "Close starts / hits",
    "Arena/Knockout rate": "Knockouts",
    "Arena/Sparring win rate": "Sparring win rate",
    "Arena/Training Baseline win rate": "Training Baseline win rate",
    "Arena/Facing reward per fighter per round": "Facing bonus / fighter / round",
}


def read_config(path):
    with path.open(encoding="utf-8") as stream:
        return yaml.safe_load(stream)


def saved_step(folder):
    steps = []
    for path in (folder / "ArenaDuel").glob("ArenaDuel-*.pt"):
        match = CHECKPOINT.fullmatch(path.name)
        if match:
            steps.append(int(match[1]))
    return max(steps, default=0)


def run_config(folder, resume, starts, target, seed, rate, interval, rewards=None, arenas=1, swap_sides=False, observe_reach=False, observe_aim=False, observe_attack_ready=False, sparring=False):
    if resume:
        config = read_config(folder / "configuration.yaml")
        step = saved_step(folder)
        if not step or not (folder / "ArenaDuel" / "checkpoint.pt").is_file():
            raise ValueError("This run has no resumable trainer checkpoint.")
        if target <= step:
            raise ValueError(f"Target steps must exceed the saved step ({step:,}).")
        config["checkpoint_settings"] = {}
        config["behaviors"]["ArenaDuel"].pop("init_path", None)
    else:
        if folder.exists():
            raise ValueError("That run ID already exists. Choose a new ID or resume the run.")
        preset = ROOT / "training" / f"self-play-damage-{starts}.yaml"
        config = read_config(preset)
        config["env_settings"] = {"seed": seed}
        behavior = config["behaviors"]["ArenaDuel"]
        behavior["hyperparameters"]["learning_rate"] = rate
        behavior["checkpoint_interval"] = interval
        if sparring:
            behavior.pop("self_play", None)
        if observe_attack_ready:
            config.setdefault("environment_parameters", {})["observe_attack_ready"] = 1.0
        if observe_aim:
            config.setdefault("environment_parameters", {})["observe_aim_angle"] = 1.0
        if observe_reach:
            config.setdefault("environment_parameters", {})["observe_attack_reach"] = 1.0
        if swap_sides:
            config.setdefault("environment_parameters", {})["alternate_start_sides"] = 1.0
    config.setdefault("env_settings", {})["num_areas"] = arenas
    if rewards is not None:
        config["environment_parameters"] = {**(config.get("environment_parameters") or {}), **rewards}
    config["behaviors"]["ArenaDuel"]["max_steps"] = target
    return config


def fixed_opponent_config(source, destination, target, opponent):
    if destination.exists():
        raise ValueError("Choose a new run ID for this training stage.")
    if not (source / "ArenaDuel" / "checkpoint.pt").is_file():
        raise ValueError("Select a sparring run with a saved trainer checkpoint.")
    config = read_config(source / "configuration.yaml")
    behavior = config["behaviors"]["ArenaDuel"]
    if behavior.get("self_play"):
        raise ValueError("Select a fixed-opponent sparring run.")
    config["checkpoint_settings"] = {}
    behavior.pop("init_path", None)
    behavior["max_steps"] = target
    parameters = config.get("environment_parameters") or {}
    parameters.pop("sparring_dodge", None)
    parameters.pop("sparring_baseline", None)
    parameters[opponent] = 1.0
    config["environment_parameters"] = parameters
    return config


def swing_summary(report, digest, completed_matches):
    swing_report = report.with_name(f"swings-{report.name}")
    if not swing_report.is_file():
        return "Rerun evaluation to collect swing reasons."

    counts = {"left": Counter(), "right": Counter()}
    with swing_report.open(newline="", encoding="utf-8-sig") as stream:
        for row in csv.DictReader(stream):
            side = row.get("policy_side")
            if (row.get("fighter") == "policy" and side in counts
                    and row.get("match") in completed_matches
                    and (row.get("checkpoint_sha256") or "").lower() == digest):
                counts[side][row.get("outcome")] += 1

    lines = ["Model swings by starting side"]
    for side, outcomes in counts.items():
        if not outcomes:
            lines.append(f"{side.title()}: no active swings recorded")
            continue
        lines.append(
            f"{side.title()}: {outcomes['hit']} hits · {outcomes['out_of_range']} range misses"
            f" · {outcomes['outside_cone']} aim misses · {outcomes['range_and_aim']} both"
            f" · {outcomes['dodged']} dodged · {outcomes['target_defeated']} target down"
        )
    return "\n".join(lines)


def attack_summary(rows):
    columns = ("policy_attacks_accepted", "policy_attacks_rejected",
               "policy_cancelled_dodge", "policy_cancelled_knockout", "policy_cancelled_round_end")
    if columns[0] not in rows[0]:
        return "Rerun evaluation to collect attack choices and windup cancellations."

    reasons = (("windup", "windup"), ("recovery", "recovery"), ("dodging", "dodging"),
               ("healing", "healing"), ("roundended", "round ended"),
               ("fighterdefeated", "fighter defeated"), ("invaliddirection", "invalid direction"))
    has_reasons = "policy_rejected_windup" in rows[0]
    lines = ["Model attack choices by starting side"]
    for side in ("left", "right"):
        matches = [row for row in rows if row.get("policy_side") == side and row.get("outcome")]
        if not matches:
            continue
        try:
            accepted, rejected, dodge, knockout, round_end = (
                sum(int(row[column]) for row in matches) for column in columns
            )
        except (KeyError, TypeError, ValueError):
            continue
        lines.append(f"{side.title()}: {accepted + rejected} chosen, {accepted} accepted, {rejected} rejected"
                     f" | Cancelled: {dodge} dodge, {knockout} knockout, {round_end} round end")
        if has_reasons:
            breakdown = []
            for key, label in reasons:
                count = sum(int(row.get(f"policy_rejected_{key}") or 0) for row in matches)
                if count:
                    breakdown.append(f"{count} {label}")
            lines.append("  Rejected by reason: " + (", ".join(breakdown) or "none"))
    if not has_reasons:
        lines.append("Rerun evaluation to collect attack rejection reasons.")
    return "\n".join(lines)


def baseline_result(model):
    if not model.is_file():
        return "Baseline Bot: waiting for a model export"
    digest = hashlib.sha256(model.read_bytes()).hexdigest()
    reports = sorted((RESULTS / "baseline-evaluations").glob("baseline-*.csv"), reverse=True)
    for report in reports:
        with report.open(newline="", encoding="utf-8-sig") as stream:
            rows = list(csv.DictReader(stream))
        if not rows or any(row.get("checkpoint_sha256", "").lower() != digest for row in rows):
            continue
        outcomes = [row.get("outcome") for row in rows]
        wins = outcomes.count("PlayerWin")
        losses = outcomes.count("OpponentWin")
        summary = (f"Baseline Bot: {wins} W / {losses} L / {len(rows) - wins - losses} D"
                f" ({wins / len(rows):.0%} wins over {len(rows)} recorded matches, seed {rows[0].get('seed', '?')})")
        if "policy_damage_dealt" not in rows[0]:
            return summary + "\nRerun evaluation to collect damage and remaining HP."
        columns = ("policy_damage_dealt", "baseline_damage_dealt",
                   "policy_hp_remaining", "baseline_hp_remaining")
        try:
            policy_damage, baseline_damage, policy_hp, baseline_hp = (
                sum(int(row[column]) for row in rows) / len(rows) for column in columns
            )
        except (KeyError, TypeError, ValueError):
            return summary
        damage_summary = (f"Average per match (model / baseline)   "
                f"Damage dealt {policy_damage:.1f} / {baseline_damage:.1f}   "
                f"HP remaining {policy_hp:.1f} / {baseline_hp:.1f}")
        completed = {row["match"] for row in rows if row.get("match") and row.get("outcome")}
        return "\n".join((summary, damage_summary, swing_summary(report, digest, completed), attack_summary(rows)))
    return "Baseline Bot: no report for the latest export"


class Dashboard:
    def __init__(self, window):
        self.window = window
        self.process = None
        self.evaluation_started = None
        self.evaluation_seed = tk.StringVar(value="2027")
        self.lines = queue.Queue()
        self.accumulator = None
        self.selected = None
        self.baseline_files = None
        self.run = tk.StringVar()
        self.name = tk.StringVar(value="arena-damage-500k-01")
        self.starts = tk.StringVar(value="normal")
        self.swap_sides = tk.BooleanVar()
        self.sparring = tk.BooleanVar()
        self.observe_reach = tk.BooleanVar()
        self.observe_aim = tk.BooleanVar()
        self.observe_attack_ready = tk.BooleanVar()
        self.target = tk.StringVar(value="500000")
        self.seed = tk.StringVar(value="4107")
        self.rate = tk.StringVar(value="0.0003")
        self.interval = tk.StringVar(value="50000")
        self.arenas = tk.StringVar(value="4")
        self.rewards = {key: tk.StringVar(value=str(default)) for key, (_, default) in REWARDS.items()}
        self.override_rewards = tk.BooleanVar()
        self.status = tk.StringVar(value="Ready. Keep Unity open and start or resume a run.")
        self.progress_text = tk.StringVar(value="Select a run to view its stats.")
        self.baseline = tk.StringVar()
        self.values = {tag: tk.StringVar(value="â€”") for tag in METRICS}
        self.build()
        self.refresh_runs()
        self.window.protocol("WM_DELETE_WINDOW", self.close)
        self.poll()
        self.refresh_stats()

    def build(self):
        self.window.title("Adaptive Arena | Training Dashboard")
        self.window.geometry("900x860")
        self.window.minsize(780, 700)
        body = ttk.Frame(self.window, padding=16)
        body.pack(fill="both", expand=True)
        ttk.Label(body, text="Training Dashboard", font=("Segoe UI", 20, "bold")).pack(anchor="w")
        ttk.Label(body, textvariable=self.status, wraplength=740).pack(anchor="w", pady=(4, 12))
        settings = ttk.LabelFrame(body, text="Self-play settings", padding=10)
        settings.pack(fill="x")
        for index, (label, value) in enumerate((
            ("New run ID", self.name), ("Starts", self.starts), ("Seed", self.seed),
            ("Learning rate", self.rate), ("Checkpoint interval", self.interval),
            ("Total target steps", self.target), ("Parallel arenas (1–16)", self.arenas),
        )):
            row, column = divmod(index, 3)
            ttk.Label(settings, text=label).grid(row=row * 2, column=column, sticky="w", padx=(0, 16))
            if value is self.starts:
                field = ttk.Combobox(settings, textvariable=value, values=("normal", "mixed"), state="readonly")
            else:
                field = ttk.Entry(settings, textvariable=value)
            field.grid(row=row * 2 + 1, column=column, sticky="ew", padx=(0, 16), pady=(0, 5))
            settings.columnconfigure(column, weight=1)
        ttk.Checkbutton(settings, text="Swap starting sides every two rounds (new runs)",
                        variable=self.swap_sides).grid(row=6, column=0, columnspan=3, sticky="w")
        ttk.Checkbutton(settings, text="Observe distance relative to attack reach (new runs)",
                        variable=self.observe_reach).grid(row=7, column=0, columnspan=3, sticky="w")
        ttk.Checkbutton(settings, text="Observe signed aim angle (new runs)",
                        variable=self.observe_aim).grid(row=8, column=0, columnspan=3, sticky="w")
        ttk.Checkbutton(settings, text="Observe attack readiness (new runs)",
                        variable=self.observe_attack_ready).grid(row=9, column=0, columnspan=3, sticky="w")
        ttk.Checkbutton(settings, text="Train against Sparring Bot (new runs)",
                        variable=self.sparring).grid(row=10, column=0, columnspan=3, sticky="w")
        rewards = ttk.LabelFrame(body, text="Rewards for each fighter (signed values)", padding=10)
        rewards.pack(fill="x", pady=(8, 0))
        reward_columns = 4
        for index, (key, (label, _)) in enumerate(REWARDS.items()):
            row, column = divmod(index, reward_columns)
            ttk.Label(rewards, text=label).grid(row=2 * row, column=column, sticky="w")
            ttk.Entry(rewards, textvariable=self.rewards[key], width=11).grid(
                row=2 * row + 1, column=column, sticky="ew", padx=(0, 12)
            )
            rewards.columnconfigure(column, weight=1)
        ttk.Checkbutton(rewards, text="Apply these rewards when resuming (otherwise keep saved rewards)",
                        variable=self.override_rewards).grid(row=4, column=0, columnspan=reward_columns, sticky="w")
        controls = ttk.Frame(body)
        controls.pack(fill="x", pady=10)
        self.start_button = ttk.Button(controls, text="Start new run", command=lambda: self.launch(False))
        self.start_button.pack(side="left")
        self.run_picker = ttk.Combobox(controls, textvariable=self.run, state="readonly", width=29)
        self.run_picker.pack(side="left", padx=(16, 4))
        self.run_picker.bind("<<ComboboxSelected>>", lambda _: self.select_run())
        ttk.Button(controls, text="Refresh runs", command=self.refresh_runs).pack(side="left")
        self.resume_button = ttk.Button(controls, text="Resume selected run", command=lambda: self.launch(True))
        self.resume_button.pack(side="left", padx=8)
        self.stop_button = ttk.Button(controls, text="Stop Training", command=self.stop, state="disabled")
        self.stop_button.pack(side="left")
        evaluation_controls = ttk.Frame(body)
        evaluation_controls.pack(fill="x", pady=(0, 8))
        self.evaluate_button = ttk.Button(evaluation_controls, text="Evaluate selected run vs Baseline Bot",
                                          command=self.evaluate, state="disabled")
        self.evaluate_button.pack(side="left")
        ttk.Label(evaluation_controls, text="Evaluation seed").pack(side="left", padx=(12, 4))
        ttk.Entry(evaluation_controls, textvariable=self.evaluation_seed, width=10).pack(side="left")
        stage_controls = ttk.Frame(body)
        stage_controls.pack(fill="x", pady=(0, 8))
        self.dodging_button = ttk.Button(stage_controls, text="Continue selected vs Dodging Sparring Bot",
                                         command=lambda: self.launch(False, opponent="sparring_dodge"), state="disabled")
        self.dodging_button.pack(side="left")
        self.baseline_training_button = ttk.Button(stage_controls, text="Continue selected vs Baseline Bot",
            command=lambda: self.launch(False, opponent="sparring_baseline"), state="disabled")
        self.baseline_training_button.pack(side="left", padx=8)
        stats = ttk.LabelFrame(body, text="Latest reported statistics", padding=10)
        stats.pack(fill="x")
        ttk.Label(stats, textvariable=self.progress_text).grid(row=0, column=0, columnspan=4, sticky="w")
        self.progress = ttk.Progressbar(stats, maximum=100)
        self.progress.grid(row=1, column=0, columnspan=4, sticky="ew", pady=(4, 8))
        for index, (tag, label) in enumerate(METRICS.items()):
            row, column = divmod(index, 2)
            ttk.Label(stats, text=label).grid(row=row + 2, column=column * 2, sticky="w")
            ttk.Label(stats, textvariable=self.values[tag]).grid(row=row + 2, column=column * 2 + 1, sticky="w")
        for column in (1, 3):
            stats.columnconfigure(column, weight=1)
        ttk.Label(stats, textvariable=self.baseline, justify="left", wraplength=715).grid(
            row=2 + (len(METRICS) + 1) // 2, column=0, columnspan=4, sticky="w", pady=(8, 0)
        )
        ttk.Label(body, text="Trainer output").pack(anchor="w", pady=(10, 2))
        self.log = tk.Text(body, height=8, state="disabled", wrap="word")
        self.log.pack(fill="both", expand=True)

    def refresh_runs(self):
        runs = sorted(path.parent.name for path in RESULTS.glob("*/configuration.yaml"))
        self.run_picker.configure(values=runs)
        if not self.run.get() and runs:
            self.run.set(runs[-1])
        self.select_run()

    def select_run(self):
        if self.selected != self.run.get():
            self.selected = self.run.get()
            self.accumulator = None
            self.baseline_files = None
            self.baseline.set("")
        idle = self.process is None and self.evaluation_started is None
        self.start_button.configure(state="normal" if idle else "disabled")
        self.resume_button.configure(state="normal" if idle and self.selected else "disabled")
        has_export = bool(self.selected) and (RESULTS / self.selected / "ArenaDuel.onnx").is_file()
        self.evaluate_button.configure(state="normal" if idle and has_export else "disabled")
        has_checkpoint = bool(self.selected) and (RESULTS / self.selected / "ArenaDuel" / "checkpoint.pt").is_file()
        stage_state = "normal" if idle and has_checkpoint else "disabled"
        self.dodging_button.configure(state=stage_state)
        self.baseline_training_button.configure(state=stage_state)

    def evaluate(self):
        run_id = self.run.get()
        if self.process is not None or self.evaluation_started is not None:
            return
        try:
            if not RUN_ID.fullmatch(run_id) or not (RESULTS / run_id / "ArenaDuel.onnx").is_file():
                raise ValueError("Select a run with an exported model.")
            EVALUATION_REQUEST.parent.mkdir(parents=True, exist_ok=True)
            EVALUATION_STATUS.unlink(missing_ok=True)
            temporary = EVALUATION_REQUEST.with_suffix(".tmp")
            seed = int(self.evaluation_seed.get())
            if not 0 <= seed <= 2147483647:
                raise ValueError("Evaluation seed must be between 0 and 2147483647.")
            temporary.write_text(f"{run_id}:{seed}", encoding="utf-8")
            temporary.replace(EVALUATION_REQUEST)
        except (OSError, ValueError) as error:
            messagebox.showerror("Could not request evaluation", str(error), parent=self.window)
            return
        self.evaluation_started = time.monotonic()
        self.run_picker.configure(state="disabled")
        self.select_run()
        self.status.set(f"Waiting for Unity to evaluate {run_id}…")

    def poll_evaluation(self):
        if self.evaluation_started is None:
            return
        try:
            status = EVALUATION_STATUS.read_text(encoding="utf-8") if EVALUATION_STATUS.is_file() else ""
            age = time.monotonic() - self.evaluation_started
            if status.startswith("Evaluating"):
                self.status.set(status)
                return
            if not status:
                if age < 65:
                    return
                EVALUATION_REQUEST.unlink(missing_ok=True)
                status = "Unity did not start evaluation. Keep the project open and try again."
        except OSError:
            return
        self.status.set(status)
        self.evaluation_started = None
        self.run_picker.configure(state="readonly")
        self.select_run()

    def launch(self, resume, opponent=None):
        run_id = self.run.get() if resume else self.name.get().strip()
        try:
            if not RUN_ID.fullmatch(run_id):
                raise ValueError("Use a run ID of up to 64 letters, digits, hyphens or underscores.")
            target = int(self.target.get())
            if target <= 0:
                raise ValueError("Enter a positive training step target.")
            if opponent:
                source_id = self.run.get()
                if not RUN_ID.fullmatch(source_id):
                    raise ValueError("Select a sparring run first.")
                config = fixed_opponent_config(RESULTS / source_id, RESULTS / run_id, target, opponent)
                arenas = config["env_settings"]["num_areas"]
            else:
                arenas = int(self.arenas.get())
                if not 1 <= arenas <= 16:
                    raise ValueError("Parallel arenas must be between 1 and 16.")
                seed = 0 if resume else int(self.seed.get())
                rate = 0.0 if resume else float(self.rate.get())
                interval = 0 if resume else int(self.interval.get())
                if target <= 0 or (not resume and not (0 <= seed < 2**32 and 0 < rate <= 1 and interval > 0)):
                    raise ValueError("Enter positive steps, a learning rate in (0, 1], and a seed from 0 to 4294967295.")
                folder = RESULTS / run_id
                rewards = None
                if not resume or self.override_rewards.get():
                    rewards = {key: float(value.get()) for key, value in self.rewards.items()}
                    if not 0 <= rewards["reward_face_opponent"] <= 0.01:
                        raise ValueError("Facing bonus must be between 0 and 0.01 per decision.")
                    if any(not math.isfinite(value) or abs(value) > 1e6 for value in rewards.values()):
                        raise ValueError("Rewards must be finite numbers between -1,000,000 and 1,000,000.")
                config = run_config(folder, resume, self.starts.get(), target, seed, rate, interval,
                                    rewards, arenas, self.swap_sides.get(), self.observe_reach.get(),
                                    self.observe_aim.get(), self.observe_attack_ready.get(), self.sparring.get())
            # The exported run configuration serializes constant parameters as curricula.
            observations = []
            parameters = config.get("environment_parameters") or {}
            for key, label in (("observe_attack_reach", "reach"), ("observe_aim_angle", "aim"),
                               ("observe_attack_ready", "attack-ready"), ("sparring_dodge", "sparring-dodge"),
                               ("sparring_baseline", "baseline-opponent")):
                value = parameters.get(key, 0)
                if isinstance(value, dict):
                    value = value["curriculum"][0]["value"]["sampler_parameters"]["value"]
                if value > 0.5:
                    observations.append(label)
            if not config["behaviors"]["ArenaDuel"].get("self_play"):
                observations.append("sparring")
            PLAY_REQUEST.unlink(missing_ok=True)
            STOP_REQUEST.unlink(missing_ok=True)
            staging = ROOT / "Library" / "ArenaTraining"
            staging.mkdir(parents=True, exist_ok=True)
            path = staging / f"{run_id}.yaml"
            path.write_text(yaml.safe_dump(config), encoding="utf-8")
            python = Path(sys.executable)
            if python.name.lower() == "pythonw.exe":
                python = python.with_name("python.exe")
            command = [str(python), "-u", "-m", "mlagents.trainers.learn", str(path), f"--run-id={run_id}"]
            command.append("--max-lifetime-restarts=0")
            if resume:
                command.append("--resume")
            elif opponent:
                command.append(f"--initialize-from={source_id}")
            self.process = subprocess.Popen(
                command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, encoding="utf-8", errors="replace",
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )
        except (OSError, ValueError, KeyError, TypeError, yaml.YAMLError) as error:
            messagebox.showerror("Could not start trainer", str(error), parent=self.window)
            return
        self.active_arenas = arenas
        self.play_request_value = ":".join(["SelfPlayArena", *observations])
        self.run.set(run_id)
        self.select_run()
        self.run_picker.configure(state="disabled")
        self.start_button.configure(state="disabled")
        self.status.set(f"Starting {run_id}â€¦")
        self.append_log(f"\n--- {run_id} ---\n")
        threading.Thread(target=self.read_output, args=(self.process.stdout,), daemon=True).start()

    def read_output(self, stream):
        with stream:
            for line in stream:
                self.lines.put(line)

    def append_log(self, text):
        self.log.configure(state="normal")
        self.log.insert("end", text)
        if int(self.log.index("end-1c").split(".")[0]) > 500:
            self.log.delete("1.0", "101.0")
        self.log.see("end")
        self.log.configure(state="disabled")

    def stop(self):
        try:
            STOP_REQUEST.touch()
        except OSError as error:
            messagebox.showerror("Could not stop training", str(error), parent=self.window)
            return
        self.stop_button.configure(state="disabled")
        self.status.set("Stopping training. Waiting for the trainer to save and exit…")

    def poll(self):
        while not self.lines.empty():
            line = self.lines.get_nowait()
            self.append_log(line)
            if self.process is not None:
                if "Listening on port" in line:
                    try:
                        temporary = PLAY_REQUEST.with_suffix(".tmp")
                        temporary.write_text(self.play_request_value, encoding="utf-8")
                        temporary.replace(PLAY_REQUEST)
                        self.status.set("Trainer ready. Waiting for Unity to start SelfPlayArenaâ€¦")
                    except OSError as error:
                        self.status.set("Could not signal Unity. Open SelfPlayArena and press Play.")
                        self.append_log(f"Unity launch request failed: {error}\n")
                elif "Connected to Unity environment" in line:
                    self.status.set(f"Training live · {self.active_arenas} arena(s) requested.")
                    self.stop_button.configure(state="normal")
        if self.process is not None:
            code = self.process.poll()
            if code is not None:
                self.status.set("Trainer exited. Check the log and saved checkpoint." if code == 0
                                else f"Trainer exited with code {code}. See trainer output.")
                try:
                    PLAY_REQUEST.unlink(missing_ok=True)
                except OSError as error:
                    self.append_log(f"Could not remove Unity launch request: {error}\n")
                self.process = None
                self.stop_button.configure(state="disabled")
                self.run_picker.configure(state="readonly")
                self.start_button.configure(state="normal")
                self.refresh_runs()
        self.poll_evaluation()
        self.window.after(200, self.poll)

    def refresh_stats(self):
        try:
            self.update_stats()
        except (OSError, KeyError, ValueError, TypeError, yaml.YAMLError):
            self.progress_text.set("Waiting for readable trainer statisticsâ€¦")
        self.window.after(2000, self.refresh_stats)

    def update_stats(self):
        if not self.selected:
            return
        folder = RESULTS / self.selected
        events = folder / "ArenaDuel"
        if self.accumulator is None and events.is_dir():
            self.accumulator = EventAccumulator(str(events), size_guidance={"scalars": 1})
        latest = {}
        if self.accumulator is not None:
            self.accumulator.Reload()
            for tag in self.accumulator.Tags()["scalars"]:
                if tag in METRICS or tag == "Environment/Episode Length":
                    points = self.accumulator.Scalars(tag)
                    if points:
                        latest[tag] = points[-1]
        for tag, value in self.values.items():
            point = latest.get(tag)
            value.set("â€”" if point is None else (f"{point.value:.1%}" if tag.endswith("rate") else f"{point.value:.3f}"))
        config = folder / "configuration.yaml"
        target = read_config(config)["behaviors"]["ArenaDuel"]["max_steps"] if config.is_file() else 0
        step = max((point.step for point in latest.values()), default=0)
        checkpoint = saved_step(folder)
        step = max(step, checkpoint)
        self.progress["value"] = min(100, 100 * step / target) if target else 0
        self.progress_text.set(f"{self.selected}   |   {step:,} / {target:,} steps   |   Checkpoint {checkpoint:,}")
        model = folder / "ArenaDuel.onnx"
        reports = RESULTS / "baseline-evaluations"
        watched = [model, *reports.glob("baseline-*.csv"), *reports.glob("swings-baseline-*.csv")]
        stamp = tuple(sorted((str(path), path.stat().st_mtime_ns) for path in watched if path.is_file()))
        if stamp != self.baseline_files:
            self.baseline.set(baseline_result(model))
            self.baseline_files = stamp

    def close(self):
        if self.evaluation_started is not None:
            messagebox.showinfo("Evaluation in progress", "Wait for evaluation or stop Unity Play Mode.",
                                parent=self.window)
            return
        if self.process is not None and self.process.poll() is None:
            messagebox.showinfo("Trainer running", "Use Stop Training or wait for the trainer to exit.", parent=self.window)
            return
        self.window.destroy()


def main():
    window = tk.Tk()
    if os.name != "nt":
        Dashboard(window)
        window.mainloop()
        return

    import msvcrt

    staging = ROOT / "Library" / "ArenaTraining"
    staging.mkdir(parents=True, exist_ok=True)
    with (staging / "dashboard.lock").open("a+b") as lock:
        lock.seek(0)
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError:
            window.withdraw()
            messagebox.showinfo("Training Dashboard", "The dashboard is already open for this project.", parent=window)
            window.destroy()
            return
        Dashboard(window)
        window.mainloop()


if __name__ == "__main__":
    main()
