#!/usr/bin/env python3
"""
reprice.py — генератор плана умной переоценки табличек.

Логика:
  1. Читает listings_index.json — незакрытые записи.
  2. Для каждой записи вычисляет текущую цену/валюту и возраст листинга.
  3. Определяет выдержку по скорости продаж той же комбинации модов:
       - есть продажи ≤ 6ч → ждём 24ч
       - нет данных / медленные → ждём 12ч
  4. Если возраст ≥ выдержки — вычисляет новую цену:
       - divine > 5d  → −1 divine
       - divine ≤ 5d  → перевод в chaos, −3 chaos
       - уже в chaos  → −3 chaos (минимум 3 chaos)
  5. Записывает reprice_plan.json, выводит сводку.

Запуск:
  python reprice.py [--dry-run]
"""
from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timedelta
from pathlib import Path

ROOT           = Path(__file__).resolve().parent.parent.parent
INDEX_PATH     = ROOT / "vault" / "tabflow" / "listings_index.json"
PLAN_PATH      = ROOT / "vault" / "tabflow" / "reprice_plan.json"
NINJA_PATH     = ROOT / "poe_ninja_prices.json"
OVERRIDES_PATH = ROOT / "vault" / "tabflow" / "miss_overrides.json"

PATIENCE_NO_HISTORY_H   = 6    # ч без данных о продажах
PATIENCE_WITH_HISTORY_H = 12   # ч если есть продажи ≤ 6ч
FAST_SALE_THRESHOLD_H   = 6    # порог "быстрой" продажи
DIVINE_STEP             = 1    # шаг снижения в divine
CHAOS_STEP_PCT          = 0.10 # шаг снижения в chaos — ~10% от текущей цены
CHAOS_MIN               = 3    # минимум ≈ рефордж-флор Ritual Tablet (~0.35d = 2.5c)
CHAOS_THRESHOLD_D       = 5.0  # ≤ этого divine → переходим в chaos
LAST_N_VELOCITY_DAYS    = 14   # учитываем продажи за последние N дней
LAST_SALE_MARKUP_PCT    = 0.10 # наценка над ценой последней продажи при первом листинге

from mod_utils import mod_template, mod_set as mod_key

# ── Miss overrides (floor-защита) ────────────────────────────────────────────

def load_floor_index() -> dict[tuple[str, frozenset], float]:
    """
    Возвращает {(base_type_lower, mod_templates_frozenset) → floor_price_divine}
    для всех pending overrides.
    """
    if not OVERRIDES_PATH.exists():
        return {}
    try:
        overrides = json.loads(OVERRIDES_PATH.read_text(encoding='utf-8'))
    except Exception:
        return {}
    index: dict[tuple[str, frozenset], float] = {}
    for ov in overrides:
        if ov.get("status") != "pending":
            continue
        base  = (ov.get("base_type") or "").strip().lower()
        tmpls = frozenset(ov.get("mod_templates") or [])
        floor = float(ov.get("floor_price") or 0)
        if base and floor > 0:
            index[(base, tmpls)] = floor
    return index

def floor_for_entry(entry: dict, floor_index: dict) -> float | None:
    """Возвращает floor_price (в divine) для записи или None если нет override."""
    base  = (entry.get("baseType") or "").strip().lower()
    tmpls = frozenset(mod_template(m) for m in (entry.get("mods") or []) if m.strip())
    return floor_index.get((base, tmpls))

# ── Курс chaos/divine ─────────────────────────────────────────────────────────

def chaos_per_divine() -> float:
    try:
        data   = json.loads(NINJA_PATH.read_text(encoding='utf-8-sig'))
        prices = data["entries"][-1]["prices"]
        rate   = prices.get("chaos orb", {}).get("divineValue", 0)
        return 1.0 / rate if rate > 0 else 8.0
    except Exception:
        return 8.0   # fallback

# ── Текущая цена / валюта ─────────────────────────────────────────────────────

def current_state(entry: dict) -> tuple[float, str]:
    """Возвращает (current_price, currency) — учитывает историю переоценок."""
    price    = float(entry.get("initialPrice") or 0)
    currency = entry.get("initialCurrency") or "divine"
    for rep in entry.get("repricings") or []:
        price    = float(rep.get("newPrice") or price)
        currency = rep.get("currency") or currency
    return price, currency

# ── Скорость продаж ───────────────────────────────────────────────────────────

def build_last_sale_index(entries: list[dict], cpd: float) -> dict[frozenset, float]:
    """
    Возвращает {mod_key → max_sale_price_divine} по всем нашим продажам.
    Максимум: если продавали несколько раз, доказан самый высокий прецедент.
    """
    index: dict[frozenset, float] = {}
    for e in entries:
        if not e.get("sold"):
            continue
        amount   = e.get("salePriceAmount")
        currency = (e.get("salePriceCurrency") or "").lower()
        if not amount:
            continue
        price_d = float(amount) / cpd if "chaos" in currency else float(amount)
        key = mod_key(e.get("mods") or [])
        if key not in index or price_d > index[key]:
            index[key] = price_d
    return index


def build_velocity_index(entries: list[dict]) -> dict[frozenset, list[int]]:
    """Возвращает {mod_key → [minutesToSale]} для продаж за последние N дней."""
    cutoff = datetime.now() - timedelta(days=LAST_N_VELOCITY_DAYS)
    index: dict[frozenset, list[int]] = {}
    for e in entries:
        if not e.get("sold") or not e.get("minutesToSale"):
            continue
        try:
            sold_dt = datetime.fromisoformat(
                (e.get("saleTime") or "").replace("Z", "+00:00")
            ).replace(tzinfo=None)
        except Exception:
            continue
        if sold_dt < cutoff:
            continue
        key = mod_key(e.get("mods") or [])
        index.setdefault(key, []).append(int(e["minutesToSale"]))
    return index

def patience_hours(mod_k: frozenset, velocity: dict[frozenset, list[int]]) -> int:
    """Возвращает выдержку в часах для данного набора модов."""
    times = velocity.get(mod_k)
    if times:
        fast = [t for t in times if t <= FAST_SALE_THRESHOLD_H * 60]
        if fast:
            return PATIENCE_WITH_HISTORY_H
    return PATIENCE_NO_HISTORY_H

# ── Расчёт новой цены ─────────────────────────────────────────────────────────

def chaos_step(price: float) -> int:
    """Адаптивный шаг снижения в chaos: ~10% от текущей цены, минимум 1."""
    return max(1, round(price * CHAOS_STEP_PCT))


def calc_new_price(current_price: float, currency: str,
                   cpd: float) -> tuple[float, str, str]:
    """
    Возвращает (new_price, new_currency, reason).
    cpd = chaos per divine.
    """
    if currency == "chaos":
        step = chaos_step(current_price)
        new  = max(CHAOS_MIN, current_price - step)
        return new, "chaos", f"chaos −{step}c"

    # divine
    if current_price > CHAOS_THRESHOLD_D:
        return current_price - DIVINE_STEP, "divine", f"divine −{DIVINE_STEP}d"

    # ≤ 5d → конвертируем в chaos
    in_chaos  = round(current_price * cpd)
    step      = chaos_step(in_chaos)
    new_chaos = max(CHAOS_MIN, in_chaos - step)
    return new_chaos, "chaos", f"переход divine→chaos ({in_chaos}c −{step}c)"

# ── Основная логика ───────────────────────────────────────────────────────────

def make_plan(entries: list[dict], dry_run: bool) -> list[dict]:
    unsold          = [e for e in entries if not e.get("sold")]
    cpd             = chaos_per_divine()
    velocity        = build_velocity_index(entries)
    floor_index     = load_floor_index()
    last_sale_index = build_last_sale_index(entries, cpd)
    now             = datetime.now()
    plan            = []

    print(f"Незакрытых листингов: {len(unsold)}   chaos/divine: {cpd:.1f}")
    print(f"Данные о скорости: {sum(len(v) for v in velocity.values())} продаж по "
          f"{len(velocity)} комбинациям модов")
    print(f"Прецеденты продаж: {len(last_sale_index)} комбинаций модов")
    if floor_index:
        print(f"Floor-защита: {len(floor_index)} pending override(s)")

    for e in unsold:
        try:
            listing_dt = datetime.fromisoformat(e["timestamp"].replace("Z", ""))
        except Exception:
            continue

        age_h        = (now - listing_dt).total_seconds() / 3600
        mod_k        = mod_key(e.get("mods") or [])
        patience     = patience_hours(mod_k, velocity)
        current, cur = current_state(e)

        if age_h < patience:
            print(f"  ⏳ [{e['col']},{e['row']}] {e.get('baseType')} {current}{cur[0]}  "
                  f"возраст {age_h:.1f}ч < выдержка {patience}ч — пропуск")
            continue

        new_price, new_cur, reason = calc_new_price(current, cur, cpd)

        floor         = floor_for_entry(e, floor_index)
        last_sale_d   = last_sale_index.get(mod_k)  # max доказанная цена в divine

        # ── Текущая и новая цены в divine для сравнения ───────────────────
        current_d   = current   / cpd if cur    == "chaos" else current
        new_price_d = new_price / cpd if new_cur == "chaos" else new_price

        # ── Достигли абсолютного флора → снимаем на рефордж ──────────────
        at_chaos_min  = (new_cur == "chaos" and new_price == CHAOS_MIN
                         and new_price == current and cur == "chaos")
        at_miss_floor = (floor is not None and current_d <= floor)

        if at_chaos_min or at_miss_floor:
            floor_label = f"{floor}d" if floor is not None else f"{CHAOS_MIN}c"
            print(f"  ♻ [{e['col']},{e['row']}] {e.get('baseType')} {current}{cur[0]}  "
                  f"флор {floor_label} — на рефордж")
            plan.append({
                "listingId":       e.get("id"),
                "col":             e.get("col"),
                "row":             e.get("row"),
                "baseType":        e.get("baseType"),
                "mods":            e.get("mods") or [],
                "currentPrice":    current,
                "currentCurrency": cur,
                "newPrice":        current,
                "newCurrency":     cur,
                "reason":          f"флор {floor_label}",
                "action":          "delist_for_reforge",
                "ageHours":        round(age_h, 1),
                "patienceHours":   patience,
                "floorDivine":     floor,
                "lastSaleDivine":  last_sale_d,
            })
            continue

        # ── Floor от последней продажи: не опускаемся ниже прецедента ────
        if last_sale_d is not None and new_price_d < last_sale_d:
            if current_d >= last_sale_d:
                # Текущая цена ≥ прецедента — просто пропускаем снижение
                print(f"  🏷 [{e['col']},{e['row']}] {e.get('baseType')} {current}{cur[0]}  "
                      f"последняя продажа {last_sale_d:.1f}d — держим цену")
                continue
            else:
                # Текущая цена < прецедента — поднимаем до последней продажи
                new_price_d = last_sale_d
                new_price   = last_sale_d if new_cur == "divine" else round(last_sale_d * cpd)
                new_cur     = "divine"
                reason      = f"↑ до прецедента продажи {last_sale_d:.1f}d"

        # ── Новая цена опустится ниже miss-floor → ограничиваем до floor ─
        if floor is not None and new_price_d < floor:
            if new_cur == "chaos":
                new_price = max(CHAOS_MIN, round(floor * cpd))
            else:
                new_price = floor
            new_cur = "divine" if floor >= 1 else "chaos"
            reason  = f"cap at floor {floor}d"

        plan.append({
            "listingId":       e.get("id"),
            "col":             e.get("col"),
            "row":             e.get("row"),
            "baseType":        e.get("baseType"),
            "mods":            e.get("mods") or [],
            "currentPrice":    current,
            "currentCurrency": cur,
            "newPrice":        new_price,
            "newCurrency":     new_cur,
            "reason":          reason,
            "action":          "reprice",
            "ageHours":        round(age_h, 1),
            "patienceHours":   patience,
            "floorDivine":     floor,
            "lastSaleDivine":  last_sale_d,
        })

        prefix = "[dry] " if dry_run else ""
        floor_tag = f"  [floor {floor}d]" if floor else ""
        print(f"  {prefix}📉 [{e['col']},{e['row']}] {e.get('baseType')}  "
              f"{current}{cur[0]} → {new_price}{new_cur[0]}  ({reason}, {age_h:.1f}ч){floor_tag}")

    return plan

# ── Точка входа ───────────────────────────────────────────────────────────────

def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true",
                    help="Показать план без записи файла")
    args = ap.parse_args()

    if not INDEX_PATH.exists():
        print("listings_index.json не найден")
        return

    entries = json.loads(INDEX_PATH.read_text(encoding='utf-8'))
    plan    = make_plan(entries, args.dry_run)

    if not plan:
        print("\nНет позиций для переоценки.")
        return

    if not args.dry_run:
        PLAN_PATH.parent.mkdir(parents=True, exist_ok=True)
        PLAN_PATH.write_text(
            json.dumps(plan, ensure_ascii=False, indent=2), encoding='utf-8')
        print(f"\nПлан записан: {PLAN_PATH.name}  ({len(plan)} позиций)")
    else:
        print(f"\n[dry-run] план не записан, позиций: {len(plan)}")

if __name__ == "__main__":
    main()
