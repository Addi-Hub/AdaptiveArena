"""Local Adaptive Arena trainer dashboard. Run with the ML-Agents Python environment."""
import csv
import hashlib
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import threading
import tkinter as tk
from tkinter import messagebox, ttk

import yaml
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "results"
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


def run_config(folder, resume, starts, target, seed, rate, interval):
    if resume:
        config = read_config(folder / "configuration.yaml")
        step = saved_step(folder)
        if not step or not (folder / "ArenaDuel" / "checkpoint.pt").is_file():
            raise ValueError("This run has no resumable trainer checkpoint.")
        if target <= step:
            raise ValueError(f"Target steps must exceed the saved step ({step:,}).")
        config["checkpoint_settings"] = {}
    else:
        if folder.exists():
            raise ValueError("That run ID already exists. Choose a new ID or resume the run.")
        preset = ROOT / "training" / f"self-play-damage-{starts}.yaml"
        config = read_config(preset)
        config["env_settings"] = {"seed": seed}
        behavior = config["behaviors"]["ArenaDuel"]
        behavior["hyperparameters"]["learning_rate"] = rate
        behavior["checkpoint_interval"] = interval
    config["behaviors"]["ArenaDuel"]["max_steps"] = target
    return config


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
        return (f"Baseline Bot: {wins} W / {losses} L / {len(rows) - wins - losses} D"
                f" ({wins / len(rows):.0%} wins over {len(rows)} recorded matches)")
    return "Baseline Bot: no report for the latest export"


class Dashboard:
    def __init__(self, window):
        self.window = window
        self.process = None
        self.lines = queue.Queue()
        self.accumulator = None
        self.selected = None
        self.baseline_files = None
        self.run = tk.StringVar()
        self.name = tk.StringVar(value="arena-damage-500k-01")
        self.starts = tk.StringVar(value="normal")
        self.target = tk.StringVar(value="500000")
        self.seed = tk.StringVar(value="4107")
        self.rate = tk.StringVar(value="0.0003")
        self.interval = tk.StringVar(value="50000")
        self.status = tk.StringVar(value="Ready. Start the trainer, then press Play in SelfPlayArena.")
        self.progress_text = tk.StringVar(value="Select a run to view its stats.")
        self.baseline = tk.StringVar()
        self.values = {tag: tk.StringVar(value="—") for tag in METRICS}
        self.build()
        self.refresh_runs()
        self.window.protocol("WM_DELETE_WINDOW", self.close)
        self.poll()
        self.refresh_stats()

    def build(self):
        self.window.title("Adaptive Arena | Training Dashboard")
        self.window.geometry("900x680")
        self.window.minsize(780, 620)
        body = ttk.Frame(self.window, padding=16)
        body.pack(fill="both", expand=True)
        ttk.Label(body, text="Training Dashboard", font=("Segoe UI", 20, "bold")).pack(anchor="w")
        ttk.Label(body, textvariable=self.status, wraplength=740).pack(anchor="w", pady=(4, 12))
        settings = ttk.LabelFrame(body, text="Self-play settings", padding=10)
        settings.pack(fill="x")
        for index, (label, value) in enumerate((
            ("New run ID", self.name), ("Starts", self.starts), ("Seed", self.seed),
            ("Learning rate", self.rate), ("Checkpoint interval", self.interval),
            ("Total target steps", self.target),
        )):
            row, column = divmod(index, 3)
            ttk.Label(settings, text=label).grid(row=row * 2, column=column, sticky="w", padx=(0, 16))
            if value is self.starts:
                field = ttk.Combobox(settings, textvariable=value, values=("normal", "mixed"), state="readonly")
            else:
                field = ttk.Entry(settings, textvariable=value)
            field.grid(row=row * 2 + 1, column=column, sticky="ew", padx=(0, 16), pady=(0, 5))
            settings.columnconfigure(column, weight=1)
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
        ttk.Label(stats, textvariable=self.baseline).grid(
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
        enabled = self.process is None and bool(self.selected)
        self.resume_button.configure(state="normal" if enabled else "disabled")

    def launch(self, resume):
        run_id = self.run.get() if resume else self.name.get().strip()
        try:
            if not RUN_ID.fullmatch(run_id):
                raise ValueError("Use a run ID of up to 64 letters, digits, hyphens or underscores.")
            target = int(self.target.get())
            seed = 0 if resume else int(self.seed.get())
            rate = 0.0 if resume else float(self.rate.get())
            interval = 0 if resume else int(self.interval.get())
            if target <= 0 or (not resume and not (0 <= seed < 2**32 and 0 < rate <= 1 and interval > 0)):
                raise ValueError("Enter positive steps, a learning rate in (0, 1], and a seed from 0 to 4294967295.")
            folder = RESULTS / run_id
            config = run_config(folder, resume, self.starts.get(), target, seed, rate, interval)
            staging = ROOT / "Library" / "ArenaTraining"
            staging.mkdir(parents=True, exist_ok=True)
            path = staging / f"{run_id}.yaml"
            path.write_text(yaml.safe_dump(config), encoding="utf-8")
            python = Path(sys.executable)
            if python.name.lower() == "pythonw.exe":
                python = python.with_name("python.exe")
            command = [str(python), "-u", "-m", "mlagents.trainers.learn", str(path), f"--run-id={run_id}"]
            if resume:
                command.append("--resume")
            self.process = subprocess.Popen(
                command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, encoding="utf-8", errors="replace",
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )
        except (OSError, ValueError, KeyError, TypeError, yaml.YAMLError) as error:
            messagebox.showerror("Could not start trainer", str(error), parent=self.window)
            return
        self.run.set(run_id)
        self.select_run()
        self.run_picker.configure(state="disabled")
        self.start_button.configure(state="disabled")
        self.status.set(f"Starting {run_id}…")
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

    def poll(self):
        while not self.lines.empty():
            line = self.lines.get_nowait()
            self.append_log(line)
            if self.process is not None:
                if "Listening on port" in line:
                    self.status.set("Trainer listening. Open SelfPlayArena and press Play in Unity.")
                elif "Connected to Unity environment" in line:
                    self.status.set("Training live. Stop Unity Play Mode to end the session.")
        if self.process is not None:
            code = self.process.poll()
            if code is not None:
                self.status.set("Trainer exited. Check the log and saved checkpoint." if code == 0
                                else f"Trainer exited with code {code}. See trainer output.")
                self.process = None
                self.run_picker.configure(state="readonly")
                self.start_button.configure(state="normal")
                self.refresh_runs()
        self.window.after(200, self.poll)

    def refresh_stats(self):
        try:
            self.update_stats()
        except (OSError, KeyError, ValueError, TypeError, yaml.YAMLError):
            self.progress_text.set("Waiting for readable trainer statistics…")
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
            value.set("—" if point is None else (f"{point.value:.1%}" if tag.endswith("rate") else f"{point.value:.3f}"))
        config = folder / "configuration.yaml"
        target = read_config(config)["behaviors"]["ArenaDuel"]["max_steps"] if config.is_file() else 0
        step = max((point.step for point in latest.values()), default=0)
        checkpoint = saved_step(folder)
        step = max(step, checkpoint)
        self.progress["value"] = min(100, 100 * step / target) if target else 0
        self.progress_text.set(f"{self.selected}   |   {step:,} / {target:,} steps   |   Checkpoint {checkpoint:,}")
        model = folder / "ArenaDuel.onnx"
        watched = [model, *(RESULTS / "baseline-evaluations").glob("baseline-*.csv")]
        stamp = tuple(sorted((str(path), path.stat().st_mtime_ns) for path in watched if path.is_file()))
        if stamp != self.baseline_files:
            self.baseline.set(baseline_result(model))
            self.baseline_files = stamp

    def close(self):
        if self.process is not None and self.process.poll() is None:
            messagebox.showinfo("Trainer running", "Stop Unity Play Mode or wait for the trainer to exit.", parent=self.window)
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
