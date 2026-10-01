"""Launching and reaping headless Unity game processes.

Each Unity runs in its own process group (start_new_session=True), so parent exit alone does NOT
kill it; stop() and an atexit sweep signal the whole group. Every group this interpreter spawns
is recorded in a per-parent registry file, so a parent that dies uncleanly (Ray SIGKILL, sbatch
preemption) leaves a list an operator or the next launch can reap.

Unity's stdout/stderr go to DEVNULL and its log to -logFile: an unread subprocess.PIPE fills at
~64 KB and freezes Unity's main thread mid-episode (it looks like a day-transition hang).
"""
from __future__ import annotations

import atexit
import os
import signal
import socket
import subprocess
import sys
import time
from pathlib import Path
from typing import Optional

_EXE_NAME = "Collaborative Operations And Resource Management with Agentic AI"   # Unity productName
_BUILDS = {   # (headless server build, graphics-capable render build) per platform, from the repo root
    "darwin": (f"Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/{_EXE_NAME}",
               f"Build/HeadlessRender/macOS/ARC_HeadlessRender.app/Contents/MacOS/{_EXE_NAME}"),
    "linux": ("Build/Headless/Linux/ARC_Headless.x86_64", "Build/HeadlessRender/Linux/ARC_HeadlessRender.x86_64"),
    "win": ("Build/Headless/Windows/ARC_Headless.exe", "Build/HeadlessRender/Windows/ARC_HeadlessRender.exe"),
}


def default_exe(render: bool = False) -> str:
    """The headless build to launch: ARC_HEADLESS_EXE / ARC_RENDER_EXE if set, else this platform's
    build under Build/ (HeadlessBuildScript.BuildMacOS / BuildLinux / BuildMacOSRender). The render
    build keeps graphics, for camera frames; on Linux it needs a virtual display (xvfb-run)."""
    env = os.environ.get("ARC_RENDER_EXE" if render else "ARC_HEADLESS_EXE")
    if env:
        return env
    plat = "win" if sys.platform.startswith("win") else "linux" if sys.platform.startswith("linux") else "darwin"
    return str(Path(__file__).resolve().parents[2] / _BUILDS[plat][render])


_REGISTRY = Path(os.environ.get("ARC_GAME_UNITY_REGISTRY")
                 or f"/tmp/arc_game_unity_pgids_{os.getpid()}.txt")
BOOT_SECONDS = 8            # time for MainScene to load and the gym server to bind


def _registry_append(pgid: int, port: int) -> None:
    try:
        with open(_REGISTRY, "a") as f:
            f.write(f"{pgid}\t{port}\t{time.time():.0f}\n")
    except OSError:
        pass                # non-fatal: the Popen handle still allows a graceful stop


def _registry_remove(pgid: int) -> None:
    try:
        if not _REGISTRY.exists():
            return
        with open(_REGISTRY) as f:
            keep = [ln for ln in f if ln.strip() and not ln.startswith(f"{pgid}\t")]
        with open(_REGISTRY, "w") as f:
            f.writelines(keep)
    except OSError:
        pass


def kill_process_group(pgid: int, timeout: float = 5.0) -> None:
    """SIGTERM the group, then SIGKILL after `timeout`. Silent if it is already gone."""
    try:
        os.killpg(pgid, signal.SIGTERM)
    except (ProcessLookupError, PermissionError):
        return
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            os.killpg(pgid, 0)
        except (ProcessLookupError, PermissionError):
            # Gone, or no longer ours to probe after SIGTERM (leader reaped/reparented):
            # either way done, rather than letting the probe crash close() and mask the
            # episode's real error.
            return
        time.sleep(0.1)
    try:
        os.killpg(pgid, signal.SIGKILL)
    except (ProcessLookupError, PermissionError):
        pass


def _sweep_registry() -> None:
    try:
        if not _REGISTRY.exists():
            return
        with open(_REGISTRY) as f:
            for line in f:
                try:
                    kill_process_group(int(line.split("\t", 1)[0]), timeout=2.0)
                except (ValueError, IndexError):
                    continue
    finally:
        try:
            _REGISTRY.unlink()
        except OSError:
            pass


atexit.register(_sweep_registry)


def port_in_use(port: int) -> bool:
    probe = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    probe.settimeout(0.25)
    try:
        probe.connect(("127.0.0.1", port))
        return True
    except OSError:
        return False
    finally:
        probe.close()


def launch(exe_path: str, port: int, *, seed: Optional[int] = None, log_path: Optional[str] = None,
           graphics: bool = False, param_config: Optional[str] = None,
           map_config: Optional[str] = None) -> Optional[subprocess.Popen]:
    """Start a headless Unity serving the gym on `port`; None if something already listens there.

    A second spawn on an occupied port wastes a process and, with the single-client gym server,
    hangs; the caller connects to the existing listener instead.

    seed          passed as -seed: the flood layout and opening tasks are rolled in MainScene's
                  Awake/Start, before the gym server accepts a request, so it must be a launch arg
    graphics      keep the GPU device (omit -nographics): needed for camera frame capture, and
                  for a Player build serving as the gym (a Player build under -nographics does
                  not bind the gym server). ARC_KEEP_GRAPHICS=1 forces it.
    param_config  parameter CSV, exported as ARC_PARAM_CONFIG
    map_config    map JSON path, or "none" for the scene's built-in layout (ARC_MAP_CONFIG)
    """
    exe = Path(exe_path)
    if not exe.exists():
        raise FileNotFoundError(f"Unity executable not found: {exe}")
    if port_in_use(port):
        print(f"ℹ️  Unity already listening on port {port}; skipping duplicate spawn")
        return None

    graphics = graphics or os.environ.get("ARC_KEEP_GRAPHICS", "").strip().lower() in ("1", "true", "yes")
    cmd = [str(exe), "-batchmode"] + ([] if graphics else ["-nographics"])
    cmd += ["-gym-server", "-gym-port", str(port), "-logFile", log_path or "-"]
    if seed is not None:
        cmd += ["-seed", str(int(seed))]
    env = os.environ.copy()
    if param_config:
        env["ARC_PARAM_CONFIG"] = os.path.abspath(param_config)
    if map_config:
        env["ARC_MAP_CONFIG"] = map_config if map_config.lower() == "none" else os.path.abspath(map_config)

    print(f"🎮 Starting Unity gym server: {exe} (port {port})")
    proc = subprocess.Popen(cmd, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                            stdin=subprocess.DEVNULL, start_new_session=True)
    _registry_append(proc.pid, port)
    time.sleep(BOOT_SECONDS)
    return proc


def stop(proc: Optional[subprocess.Popen]) -> None:
    """Kill a launched Unity's whole process group (crash handlers and helpers included)."""
    if proc is None:
        return
    try:
        pgid = os.getpgid(proc.pid)
    except (ProcessLookupError, PermissionError):
        pgid = proc.pid
    kill_process_group(pgid, timeout=5.0)
    try:
        proc.wait(timeout=1.0)
    except (subprocess.TimeoutExpired, OSError):
        pass
    _registry_remove(pgid)
