"""
api_rate_limiter.py — учёт запросов к GGG Trade API с персистентным логом.

Лимиты из реальных заголовков X-Rate-Limit-Ip (2026-07-17):
  5  req / 10s  → бан 60s
  15 req / 60s  → бан 5 min
  30 req / 300s → бан 30 min

Файл api_rate_log.json хранит временные метки всех запросов за последние 5 минут.
Переживает перезапуски — не даёт превысить лимит даже если сервис перезапустился.
"""
from __future__ import annotations

import json
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT     = Path(__file__).resolve().parent.parent.parent
LOG_PATH = ROOT / "vault" / "tabflow" / "api_rate_log.json"

# (max_requests, window_seconds) — из заголовка X-Rate-Limit-Ip
LIMITS = [
    (5,  10),   # 5 req / 10s
    (15, 60),   # 15 req / 60s
    (30, 300),  # 30 req / 300s
]
KEEP_WINDOW_S = 360  # хранить метки за последние 6 мин (чуть больше max окна)
SAFETY_GAP_S  = 0.1  # небольшой запас чтобы не попасть на границу


def _now_ts() -> float:
    return time.time()


def _load(path: Path) -> list[float]:
    """Загружает список unix-timestamps из файла."""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return [float(t) for t in (data.get("requests") or [])]
    except Exception:
        return []


def _save(path: Path, timestamps: list[float]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps({"requests": [round(t, 3) for t in timestamps]}, indent=2),
        encoding="utf-8",
    )


def _trim(timestamps: list[float], now: float) -> list[float]:
    """Оставляет только метки в пределах KEEP_WINDOW_S."""
    cutoff = now - KEEP_WINDOW_S
    return [t for t in timestamps if t > cutoff]


def _wait_needed(timestamps: list[float], now: float) -> float:
    """
    Возвращает количество секунд, которые нужно подождать перед следующим запросом,
    чтобы не нарушить ни один из лимитов.
    0.0 — запрос можно делать сейчас.
    """
    max_wait = 0.0
    for max_req, window_s in LIMITS:
        window_start = now - window_s
        in_window = [t for t in timestamps if t > window_start]
        if len(in_window) >= max_req:
            # нужно подождать пока самый старый запрос в окне выйдет за его пределы
            oldest = min(in_window)
            wait = (oldest + window_s) - now + SAFETY_GAP_S
            if wait > max_wait:
                max_wait = wait
    return max_wait


class ApiRateLimiter:
    """
    Поточно-безопасный (файловый) ограничитель запросов к GGG Trade API.

    Использование:
        limiter = ApiRateLimiter()
        limiter.wait_and_record()   # вызвать перед каждым HTTP-запросом
        # делаем запрос...
    """

    def __init__(self, log_path: Path = LOG_PATH, verbose: bool = False) -> None:
        self.log_path = log_path
        self.verbose  = verbose

    def wait_and_record(self) -> None:
        """
        Ждёт столько, сколько нужно чтобы не нарушить лимит,
        затем записывает метку текущего запроса в файл.
        """
        timestamps = _load(self.log_path)
        now        = _now_ts()
        timestamps = _trim(timestamps, now)

        # Повторяем проверку после каждого sleep (может быть несколько ограничений)
        while True:
            wait = _wait_needed(timestamps, now)
            if wait <= 0:
                break
            if self.verbose:
                print(f"  [rate-limit] жду {wait:.1f}с…")
            time.sleep(wait)
            now = _now_ts()

        # Записываем метку нового запроса
        timestamps.append(now)
        _save(self.log_path, timestamps)


# ── CLI: показать текущее состояние лога ─────────────────────────────────────

if __name__ == "__main__":
    ts = _load(LOG_PATH)
    now = _now_ts()
    ts  = _trim(ts, now)
    print(f"Запросов в логе (за последние {KEEP_WINDOW_S}с): {len(ts)}")
    for max_req, window_s in LIMITS:
        window_start = now - window_s
        in_w = [t for t in ts if t > window_start]
        print(f"  {len(in_w)}/{max_req} за {window_s}с")
    wait = _wait_needed(ts, now)
    if wait > 0:
        print(f"До следующего безопасного запроса: {wait:.1f}с")
    else:
        print("Запрос можно делать прямо сейчас.")
