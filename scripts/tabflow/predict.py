#!/usr/bin/env python3
"""
Tablet Evaluator — Inference Script

Читает снапшоты из trade_data/, предсказывает цену каждой планшетки,
выводит таблицу отсортированную по марже (предсказанная − листинг).

Usage:
  python predict.py                          # последний снапшот с планшетками
  python predict.py --snapshot rit_s19.json  # конкретный файл
  python predict.py --latest 3               # последние 3 снапшота
  python predict.py --top 30                 # показать топ-30 вместо 20
  python predict.py --min-margin 0.3         # только маржа >= 0.3d
"""

import argparse
import json
import math
import re
from pathlib import Path

import lightgbm as lgb
import numpy as np

HERE   = Path(__file__).parent
ROOT   = HERE.parent.parent
MODELS = HERE / "models"

CURRENCY_ALIASES = {
    "divine":  "divine orb",
    "exalted": "exalted orb",
    "chaos":   "chaos orb",
    "regal":   "regal orb",
}


# ─── Утилиты ─────────────────────────────────────────────────────────────────

def load_divine_rates(ninja_path: Path) -> dict[str, float]:
    try:
        data = json.loads(ninja_path.read_text(encoding="utf-8"))
        entries = data.get("entries", [])
        if not entries:
            return {}
        return {k: v["divineValue"] for k, v in entries[-1]["prices"].items()}
    except Exception:
        return {}


def to_divine(amount: float, currency: str, rates: dict[str, float]) -> float | None:
    key = CURRENCY_ALIASES.get(currency.lower().strip(), currency.lower().strip())
    rate = rates.get(key)
    return amount * rate if rate else None


def is_tablet(base_type: str) -> bool:
    return "tablet" in base_type.lower()


def type_slug(base_type: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", base_type.lower()).strip("_")


# ─── Загрузка модели ─────────────────────────────────────────────────────────

def load_model(slug: str) -> tuple | None:
    model_dir = MODELS / slug
    model_file = model_dir / "model.txt"
    vocab_file = model_dir / "vocab.json"
    meta_file  = model_dir / "metadata.json"

    if not model_file.exists():
        return None

    model  = lgb.Booster(model_file=str(model_file))
    vocab  = json.loads(vocab_file.read_text(encoding="utf-8"))
    meta   = json.loads(meta_file.read_text(encoding="utf-8")) if meta_file.exists() else {}
    return model, vocab, meta


# ─── Feature extraction ───────────────────────────────────────────────────────

def extract_features(rich_mods: list[dict], vocab: dict[str, int]) -> np.ndarray:
    vec = np.zeros(len(vocab), dtype=np.float32)
    for mod in rich_mods:
        h = mod.get("hash")
        if not h or h not in vocab:
            continue
        idx   = vocab[h]
        value = mod.get("value")
        rmin  = mod.get("roll_min")
        rmax  = mod.get("roll_max")

        if value is None:
            vec[idx] = 1.0
        elif rmax is not None and rmin is not None and rmax > rmin:
            vec[idx] = (value - rmin) / (rmax - rmin)
        elif rmax is not None and rmax > 0:
            vec[idx] = value / rmax
        else:
            vec[idx] = 1.0
    return vec


# ─── Отображение модов ───────────────────────────────────────────────────────

def roll_indicator(mod: dict) -> str:
    value = mod.get("value")
    rmin  = mod.get("roll_min")
    rmax  = mod.get("roll_max")
    if value is None or rmax is None or rmin is None or rmax <= rmin:
        return ""
    norm = (value - rmin) / (rmax - rmin)
    if norm >= 0.75:
        return "▲"
    if norm >= 0.25:
        return "·"
    return "▽"


def short_mod_name(text: str) -> str:
    """'Tribute S1 — Monsters Sacrificed...50%...' → 'Tribute▲'"""
    # Текст до " — " содержит "Name Tier"
    if " — " in text:
        label_part = text.split(" — ")[0].strip()      # "Tribute S1"
        # Убираем последнее слово (тир: S1, P2, …)
        words = label_part.split()
        if len(words) >= 2 and re.match(r"^[SP]\d+$", words[-1]):
            return " ".join(words[:-1])
        return label_part
    return text[:20]


def format_mods(rich_mods: list[dict], vocab: dict[str, int], meta: dict) -> str:
    """Форматирует список модов в одну строку, самые важные первыми."""
    top_hashes = {h for h, _ in meta.get("top_features", [])[:8]}

    parts: list[tuple[int, str]] = []  # (priority, text)
    for mod in rich_mods:
        h = mod.get("hash", "")
        if not h or h not in vocab:
            continue
        name = short_mod_name(mod.get("text", ""))
        ind  = roll_indicator(mod)
        priority = 0 if h in top_hashes else 1
        parts.append((priority, f"{name}{ind}"))

    parts.sort(key=lambda x: x[0])
    labels = [p[1] for p in parts]
    if len(labels) > 4:
        labels = labels[:4] + [f"+{len(labels)-4}"]
    return "  ".join(labels)


# ─── Загрузка снапшотов ───────────────────────────────────────────────────────

def load_snapshot(path: Path, rates: dict[str, float]) -> list[dict]:
    items = []
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except Exception as e:
        print(f"  [!] {path.name}: {e}")
        return []

    for listing in doc.get("listings", []):
        base_type = listing.get("base_type", "")
        if not is_tablet(base_type):
            continue
        if not listing.get("mods_explicit_rich"):
            continue

        amount   = listing.get("price_divine", 0)
        currency = listing.get("price_currency", "divine")
        price_d  = amount if currency == "divine" else to_divine(float(amount), currency, rates)
        if not price_d or price_d <= 0:
            continue

        rich_mods = (
            listing.get("mods_explicit_rich",   []) +
            listing.get("mods_desecrated_rich", []) +
            listing.get("mods_fractured_rich",  [])
        )

        items.append({
            "id":        listing.get("id", ""),
            "name":      listing.get("name", ""),
            "base_type": base_type,
            "price_d":   price_d,
            "currency":  currency,
            "rich_mods": rich_mods,
            "seller":    listing.get("seller_account", ""),
        })

    return items


def find_snapshots(data_dir: Path, latest: int, specific: str | None) -> list[Path]:
    if specific:
        candidates = [
            data_dir / specific,
            data_dir / (specific if specific.endswith(".json") else specific + ".json"),
        ]
        for c in candidates:
            if c.exists():
                return [c]
        # Поиск по частичному имени
        matches = sorted(data_dir.glob(f"*{specific}*.json"))
        if matches:
            return matches[:latest]
        print(f"Файл не найден: {specific}")
        return []

    # Ищем последние файлы с планшетками
    all_files = sorted(data_dir.glob("*.json"), key=lambda p: p.stat().st_mtime, reverse=True)
    result = []
    for f in all_files:
        try:
            doc = json.loads(f.read_text(encoding="utf-8"))
            has_tablets = any(
                is_tablet(x.get("base_type", "")) and x.get("mods_explicit_rich")
                for x in doc.get("listings", [])
            )
            if has_tablets:
                result.append(f)
                if len(result) >= latest:
                    break
        except Exception:
            continue
    return result


# ─── Вывод ───────────────────────────────────────────────────────────────────

def print_results(
    file_name: str,
    base_type: str,
    rows: list[dict],
    meta: dict,
    top: int,
    min_margin: float,
):
    mae = meta.get("mae_divine", "?")
    n_train = meta.get("n_train", "?")
    slug = type_slug(base_type)

    print(f"\n{'═'*72}")
    print(f"  {base_type}  —  {file_name}  ({len(rows)} предметов)")
    print(f"  Модель: MAE {mae}d  |  обучена на {n_train} предметах")
    print(f"{'═'*72}")

    filtered = [r for r in rows if r["margin"] >= min_margin]
    filtered.sort(key=lambda r: -r["margin"])

    if not filtered:
        print(f"  Нет предметов с маржой ≥ {min_margin}d")
        return

    shown = filtered[:top]
    print(f"  {'#':>3}  {'листинг':>8}  {'предск':>8}  {'маржа':>8}  моды")
    print(f"  {'─'*3}  {'─'*8}  {'─'*8}  {'─'*8}  {'─'*30}")

    for i, row in enumerate(shown, 1):
        margin_str = f"+{row['margin']:.2f}d" if row["margin"] >= 0 else f"{row['margin']:.2f}d"
        print(f"  {i:>3}  {row['price_d']:>7.2f}d  {row['predicted_d']:>7.2f}d  "
              f"{margin_str:>8}  {row['mods_label']}")

    if len(filtered) > top:
        print(f"\n  ... ещё {len(filtered) - top} предметов (используй --top N)")

    # Статистика
    margins = [r["margin"] for r in filtered]
    print(f"\n  Итого подходящих: {len(filtered)} из {len(rows)}")
    print(f"  Медианная маржа:  {sorted(margins)[len(margins)//2]:.2f}d")
    print(f"  Лучшая маржа:     {max(margins):.2f}d")


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--snapshot",   help="Имя файла в trade_data/ (полное или частичное)")
    parser.add_argument("--latest",     type=int, default=1, help="Сколько последних снапшотов (по умолч. 1)")
    parser.add_argument("--top",        type=int, default=20, help="Показать топ-N предметов (по умолч. 20)")
    parser.add_argument("--min-margin", type=float, default=0.0, help="Минимальная маржа в divine (по умолч. 0)")
    parser.add_argument("--data-dir",   default=str(ROOT / "trade_data"))
    args = parser.parse_args()

    data_dir = Path(args.data_dir)
    rates    = load_divine_rates(ROOT / "poe_ninja_prices.json")

    snapshots = find_snapshots(data_dir, args.latest, args.snapshot)
    if not snapshots:
        print("Снапшоты с планшетками не найдены.")
        print(f"  Ищу в: {data_dir}")
        print("  Убедитесь что сделали снапшот через вкладку Наблюдение.")
        return

    # Кэш загруженных моделей
    models_cache: dict[str, tuple | None] = {}

    for snap_path in snapshots:
        items = load_snapshot(snap_path, rates)
        if not items:
            print(f"{snap_path.name}: нет планшеток с rich-модами")
            continue

        # Группируем по типу
        by_type: dict[str, list] = {}
        for item in items:
            slug = type_slug(item["base_type"])
            by_type.setdefault(slug, []).append(item)

        for slug, type_items in by_type.items():
            if slug not in models_cache:
                models_cache[slug] = load_model(slug)

            loaded = models_cache[slug]
            if loaded is None:
                print(f"\n[!] Модель для '{slug}' не найдена.")
                print(f"    Запустите: python3 train_evaluator.py --types {slug}")
                continue

            model, vocab, meta = loaded

            # Предсказание
            X = np.array([extract_features(it["rich_mods"], vocab) for it in type_items])
            y_log = model.predict(X)
            predicted = np.expm1(y_log)

            rows = []
            for it, pred_d in zip(type_items, predicted):
                rows.append({
                    **it,
                    "predicted_d": float(pred_d),
                    "margin":      float(pred_d) - it["price_d"],
                    "mods_label":  format_mods(it["rich_mods"], vocab, meta),
                })

            print_results(
                file_name  = snap_path.name,
                base_type  = type_items[0]["base_type"],
                rows       = rows,
                meta       = meta,
                top        = args.top,
                min_margin = args.min_margin,
            )


if __name__ == "__main__":
    main()
