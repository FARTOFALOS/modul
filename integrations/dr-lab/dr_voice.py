"""Local female speech for NEW, closed-M5 DR confirmations; never sends an order."""
import json
import queue
import sqlite3
import subprocess
import threading
import time
from contextlib import closing
from pathlib import Path

ROOT = Path(__file__).resolve().parent
RUNTIME = ROOT / '.runtime' / 'live'
_started = None
_queue = queue.Queue()
_thread = None


def confirmations(inst, raw):
    """Reuse the chart's first-confirmation rule; gaps cannot establish a first event."""
    import scene21
    import pandas as pd
    stamp = pd.Timestamp(raw['fetched_at']).tz_convert('America/New_York')
    day = (stamp + pd.Timedelta(hours=6)).normalize()
    now = (stamp-day).total_seconds()/60
    bars = [[(pd.Timestamp(x[0], unit='s', tz='UTC').tz_convert('America/New_York')-day).total_seconds()/60, *x[1:5]] for x in raw['bars']]
    events = []
    for session, (start, formed, end) in scene21.SESS.items():
        if not formed <= now <= end: continue
        state = scene21._state(bars, session, int(now), now, True)
        close = state.get('conf')
        if close is None: continue
        present = {b[0] for b in bars if b[0]+5 <= now}
        if not all(t in present for t in range(start, int(close), 5)): continue
        closed_at = (day + pd.Timedelta(minutes=close)).timestamp()
        events.append(dict(id=f'{inst}:{day.date()}:{session}:{close}', instrument=inst,
                           session=session, closed_at=closed_at))
    return events


def observe(inst, raw):
    if _started is None: return
    for event in confirmations(inst, raw):
        # Ignore past events at launch/reconnect; never narrate replay or stale cached candles.
        if event['closed_at'] <= _started or not 0 <= time.time()-event['closed_at'] <= 180: continue
        RUNTIME.mkdir(parents=True, exist_ok=True)
        with closing(sqlite3.connect(RUNTIME/'dr_voice_seen.sqlite3', timeout=5)) as db:
            with db:
                db.execute('CREATE TABLE IF NOT EXISTS spoken (id TEXT PRIMARY KEY)')
                inserted = db.execute('INSERT OR IGNORE INTO spoken VALUES (?)', (event['id'],)).rowcount
        if inserted: _queue.put(event)


def _speak():
    while True:
        event = _queue.get()
        try:
            result = subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
                                     '-File', str(ROOT/'dr_speak.ps1'), '-Instrument', event['instrument']],
                                    capture_output=True, timeout=30,
                                    creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
            if result.returncode: raise RuntimeError(result.stderr.decode('utf-8', errors='replace')[-500:])
            status = dict(ok=True, event=event)
        except Exception as exc:
            status = dict(ok=False, event=event, error=str(exc))
        RUNTIME.mkdir(parents=True, exist_ok=True)
        (RUNTIME/'dr_voice_status.json').write_text(json.dumps(status), encoding='utf-8')
        _queue.task_done()


def start():
    global _started, _thread
    if _thread and _thread.is_alive(): return
    _started = time.time()
    _thread = threading.Thread(target=_speak, name='female-dr-voice', daemon=True)
    _thread.start()
