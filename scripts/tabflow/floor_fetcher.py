#!/usr/bin/env python3
"""
floor_fetcher.py — автоматическое получение флор-цен для всех типов табличек.

За один вызов делает 7×2 = 14 REST-запросов к GGG trade API (не live search).
Сохраняет vault/tabflow/floor_prices.json.

По умолчанию пропускает обновление если данные свежее MAX_AGE_HOURS часов.
Для принудительного обновления: --force.

Запуск:
  python floor_fetcher.py --league "Runes of Aldur" [--force]
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from datetime import datetime
from pathlib import Path

import urllib.request
import urllib.error
import urllib.parse

ROOT        = Path(__file__).resolve().parent.parent.parent
OUTPUT_PATH = ROOT / "vault" / "tabflow" / "floor_prices.json"
NINJA_PATH  = ROOT / "poe_ninja_prices.json"

TABLET_TYPES = [
    "Ritual Tablet",
    "Breach Tablet",
    "Delirium Tablet",
    "Abyss Tablet",
    "Temple Tablet",
    "Irradiated Tablet",
    "Overseer Tablet",
]

FETCH_COUNT   = 10   # сколько дешевейших позиций брать для p10
MAX_AGE_HOURS = 4    # не обновлять если данные моложе этого

BASE_URL = "https://www.pathofexile.com"


def _floor_age_hours(path: Path) -> float:
    """Возвращает возраст floor_prices.json в часах, или inf если файла нет."""
    try:
        data     = json.loads(path.read_text(encoding="utf-8"))
        fetched  = datetime.fromisoformat(data["fetched_at"])
        age_secs = (datetime.now() - fetched).total_seconds()
        return age_secs / 3600
    except Exception:
        return float("inf")


def _headers(sessid: str) -> dict[str, str]:
    return {
        "Content-Type": "application/json",
        "Cookie":       f"POESESSID={sessid}",
        "User-Agent":   "Mozilla/5.0 (GameHelper/TabFlow floor_fetcher)",
    }


def _post(url: str, body: dict, sessid: str, limiter) -> dict:
    limiter.wait_and_record()
    data = json.dumps(body).encode("utf-8")
    req  = urllib.request.Request(url, data=data, headers=_headers(sessid), method="POST")
    with urllib.request.urlopen(req, timeout=15) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _get(url: str, sessid: str, limiter) -> dict:
    limiter.wait_and_record()
    req = urllib.request.Request(url, headers=_headers(sessid), method="GET")
    with urllib.request.urlopen(req, timeout=15) as resp:
        return json.loads(resp.read().decode("utf-8"))


def fetch_floor(tablet_type: str, league: str, sessid: str, limiter) -> dict | None:
    """Возвращает {"p10_divine": float, "sample_size": int} или None при ошибке."""
    from currency import CurrencyConverter
    cc = CurrencyConverter.load(NINJA_PATH)

    # ── 1. Поиск ────────────────────────────────────────────────────────────
    search_url  = f"{BASE_URL}/api/trade2/search/{urllib.parse.quote(league)}"
    search_body = {
        "query": {
            "status": {"option": "securable"},
            "type":   tablet_type,
            "stats": [{
                "type": "and",
                "filters": [{
                    "id":       "pseudo.pseudo_number_of_uses_remaining",
                    "disabled": False,
                    "value":    {"min": 10},
                }],
                "disabled": False,
            }],
            "filters": {
                "misc_filters": {
                    "filters":  {"corrupted": {"option": "false"}},
                    "disabled": False,
                }
            },
        },
        "sort": {"price": "asc"},
    }

    try:
        search_resp = _post(search_url, search_body, sessid, limiter)
    except Exception as e:
        print(f"  [{tablet_type}] search error: {e}", file=sys.stderr)
        return None

    item_ids = (search_resp.get("result") or [])[:FETCH_COUNT]
    query_id = search_resp.get("id", "")
    if not item_ids:
        print(f"  [{tablet_type}] нет результатов")
        return None

    # ── 2. Получение цен ─────────────────────────────────────────────────────
    ids_str   = ",".join(item_ids)
    fetch_url = f"{BASE_URL}/api/trade2/fetch/{ids_str}?query={query_id}"

    try:
        fetch_resp = _get(fetch_url, sessid, limiter)
    except Exception as e:
        print(f"  [{tablet_type}] fetch error: {e}", file=sys.stderr)
        return None

    # ── 3. Конвертация → divine ──────────────────────────────────────────────
    prices_d: list[float] = []
    for item in (fetch_resp.get("result") or []):
        listing = item.get("listing") or {}
        price   = listing.get("price") or {}
        amount  = price.get("amount")
        curr    = (price.get("currency") or "divine").strip().lower()
        if amount is None:
            continue
        d = cc.to_divine(float(amount), curr)
        if d > 0:
            prices_d.append(d)

    if not prices_d:
        print(f"  [{tablet_type}] не удалось извлечь цены")
        return None

    prices_d.sort()
    p10_idx    = max(0, int(len(prices_d) * 0.10))
    p10_divine = prices_d[p10_idx]

    print(f"  [{tablet_type}] {len(prices_d)} цен, p10={p10_divine:.3f}d "
          f"(min={prices_d[0]:.3f}d, max={prices_d[-1]:.3f}d)")
    return {"p10_divine": round(p10_divine, 4), "sample_size": len(prices_d)}


def main() -> None:
    ap = argparse.ArgumentParser(description="Получить флор-цены табличек")
    ap.add_argument("--league",         default="Runes of Aldur")
    ap.add_argument("--sessid",         default="")
    ap.add_argument("--output",         default=str(OUTPUT_PATH))
    ap.add_argument("--max-age-hours",  type=float, default=MAX_AGE_HOURS,
                    help=f"Пропустить если данные свежее N часов (по умолч. {MAX_AGE_HOURS})")
    ap.add_argument("--force",          action="store_true",
                    help="Обновить даже если данные свежие")
    ap.add_argument("--dry-run",        action="store_true",
                    help="Показать запросы без выполнения")
    args = ap.parse_args()

    out_path = Path(args.output)

    # ── Проверка свежести ─────────────────────────────────────────────────────
    if not args.force and not args.dry_run:
        age_h = _floor_age_hours(out_path)
        if age_h < args.max_age_hours:
            print(f"floor_prices.json свежий ({age_h:.1f}ч < {args.max_age_hours}ч) — пропускаем.")
            sys.exit(0)

    # ── SESSID ────────────────────────────────────────────────────────────────
    sessid = args.sessid
    if not sessid:
        settings_path = ROOT / "settings.json"
        if settings_path.exists():
            try:
                s      = json.loads(settings_path.read_text(encoding="utf-8-sig"))
                sessid = s.get("TradeSessionId") or s.get("tradeSessionId") or ""
            except Exception:
                pass
    if not sessid:
        print("POESESSID не задан (--sessid или settings.json:TradeSessionId)", file=sys.stderr)
        sys.exit(1)

    if args.dry_run:
        print(f"[DRY-RUN] Запросы для лиги '{args.league}' ({len(TABLET_TYPES) * 2} запросов):")
        for t in TABLET_TYPES:
            print(f"  POST search: {t}")
            print(f"  GET  fetch:  {t}")
        return

    # ── Получение флоров с rate limiter ──────────────────────────────────────
    from api_rate_limiter import ApiRateLimiter
    limiter = ApiRateLimiter(verbose=True)

    print(f"Получаем флор-цены для лиги '{args.league}'...")
    floors: dict[str, dict] = {}

    for t in TABLET_TYPES:
        result = fetch_floor(t, args.league, sessid, limiter)
        if result:
            floors[t] = result

    output = {
        "fetched_at": datetime.now().isoformat(timespec="seconds"),
        "league":     args.league,
        "floors":     floors,
    }

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nСохранено: {out_path}  ({len(floors)}/{len(TABLET_TYPES)} типов)")


if __name__ == "__main__":
    main()
