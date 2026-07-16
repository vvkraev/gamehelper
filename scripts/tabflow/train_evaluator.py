#!/usr/bin/env python3
"""
Tablet Evaluator — Training Script

Reads trade_data/*.json snapshots (those with mods_explicit_rich),
builds a normalized-roll feature vector per item,
trains a LightGBM model per tablet type,
saves models to scripts/tabflow/models/{type}/.

Usage:
  python train_evaluator.py [--data-dir ../../trade_data] [--min-samples 30]

Dependencies:
  pip install lightgbm numpy scikit-learn
"""

import argparse
import json
import math
import os
import pickle
import re
from collections import defaultdict
from pathlib import Path

import lightgbm as lgb
import numpy as np
from sklearn.model_selection import train_test_split

HERE = Path(__file__).parent
ROOT = HERE.parent.parent  # GameHelper/


# ─── Валюта ──────────────────────────────────────────────────────────────────

CURRENCY_ALIASES = {
    "divine":   "divine orb",
    "exalted":  "exalted orb",
    "chaos":    "chaos orb",
    "regal":    "regal orb",
    "chance":   "orb of chance",
    "fusing":   "orb of fusing",
    "alt":      "orb of alteration",
    "aug":      "orb of augmentation",
    "annul":    "orb of annulment",
    "mirror":   "mirror of kalandra",
}


def build_divine_rates(poe_ninja_path: Path) -> dict[str, float]:
    """Возвращает dict currency_name_lower → divine_value (1 ед. = X divine)."""
    try:
        with open(poe_ninja_path, encoding="utf-8") as f:
            data = json.load(f)
        entries = data.get("entries", [])
        if not entries:
            return {}
        prices = entries[-1]["prices"]  # берём последний снапшот
        return {name: info["divineValue"] for name, info in prices.items()}
    except Exception as e:
        print(f"  [warn] poe_ninja_prices.json: {e}")
        return {}


def to_divine(amount: float, currency: str, rates: dict[str, float]) -> float | None:
    """Конвертирует сумму в divine. None если валюта неизвестна."""
    key = currency.lower().strip()
    key = CURRENCY_ALIASES.get(key, key)
    rate = rates.get(key)
    if rate is None:
        return None
    return amount * rate


# ─── Загрузка снапшотов ───────────────────────────────────────────────────────

TABLET_KEYWORDS = [
    "Ritual Tablet", "Abyss Tablet", "Breach Tablet", "Expedition Tablet",
    "Delirium Tablet", "Irradiated Tablet", "Overseer Tablet", "Temple Tablet",
]


def is_tablet(base_type: str) -> bool:
    bt = base_type.lower()
    return "tablet" in bt


def type_slug(base_type: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", base_type.lower()).strip("_")


def load_items(data_dir: Path, rates: dict[str, float]) -> dict[str, list[dict]]:
    """Загружает все планшетки из trade_data/. Возвращает dict type_slug → [item]."""
    by_type: dict[str, list[dict]] = defaultdict(list)
    seen_ids: set[str] = set()
    skipped_no_rich = 0
    skipped_price = 0

    files = sorted(data_dir.glob("*.json"))
    print(f"Файлов в trade_data/: {len(files)}")

    for path in files:
        try:
            with open(path, encoding="utf-8") as f:
                doc = json.load(f)
        except Exception:
            continue

        for listing in doc.get("listings", []):
            base_type = listing.get("base_type", "")
            if not is_tablet(base_type):
                continue

            item_id = listing.get("id", "")
            if item_id in seen_ids:
                continue
            seen_ids.add(item_id)

            # Нужны rich-моды — только новые снапшоты
            if not listing.get("mods_explicit_rich"):
                skipped_no_rich += 1
                continue

            # Конвертируем цену в divine
            amount = listing.get("price_divine", 0)
            currency = listing.get("price_currency", "divine")
            if currency == "divine":
                price_d = float(amount)
            else:
                price_d = to_divine(float(amount), currency, rates)
                if price_d is None:
                    skipped_price += 1
                    continue

            if price_d <= 0:
                skipped_price += 1
                continue

            item = {
                "id":         item_id,
                "base_type":  base_type,
                "price_d":    price_d,
                "rich_mods":  (
                    listing.get("mods_explicit_rich",   []) +
                    listing.get("mods_desecrated_rich", []) +
                    listing.get("mods_fractured_rich",  [])
                ),
            }
            by_type[type_slug(base_type)].append(item)

    print(f"  пропущено (нет rich-модов): {skipped_no_rich}")
    print(f"  пропущено (цена): {skipped_price}")
    for slug, items in sorted(by_type.items()):
        print(f"  {slug}: {len(items)} предметов")

    return dict(by_type)


# ─── Feature extraction ───────────────────────────────────────────────────────

def build_vocab(items: list[dict]) -> dict[str, int]:
    """Собирает все уникальные hash → индекс фичи."""
    hashes: set[str] = set()
    for item in items:
        for mod in item["rich_mods"]:
            h = mod.get("hash")
            if h:
                hashes.add(h)
    return {h: i for i, h in enumerate(sorted(hashes))}


def extract_features(item: dict, vocab: dict[str, int]) -> np.ndarray:
    """Строит вектор: 0.0 если мода нет, нормализованный ролл если есть."""
    vec = np.zeros(len(vocab), dtype=np.float32)
    for mod in item["rich_mods"]:
        h = mod.get("hash")
        if not h or h not in vocab:
            continue
        idx = vocab[h]
        value   = mod.get("value")
        rmin    = mod.get("roll_min")
        rmax    = mod.get("roll_max")

        if value is None:
            vec[idx] = 1.0  # мод есть, но значение не распознано
        elif rmax is not None and rmin is not None and rmax > rmin:
            vec[idx] = (value - rmin) / (rmax - rmin)  # 0 = мин ролл, 1 = макс ролл
        elif rmax is not None and rmax > 0:
            vec[idx] = value / rmax
        else:
            vec[idx] = 1.0  # фиксированный мод
    return vec


# ─── Фильтрация выбросов цены ─────────────────────────────────────────────────

def filter_price_outliers(items: list[dict], lo_pct: float = 5, hi_pct: float = 95) -> list[dict]:
    prices = np.array([it["price_d"] for it in items])
    lo = np.percentile(prices, lo_pct)
    hi = np.percentile(prices, hi_pct)
    filtered = [it for it in items if lo <= it["price_d"] <= hi]
    print(f"  фильтр цен [{lo:.2f}d, {hi:.2f}d]: {len(items)} → {len(filtered)} предметов")
    return filtered


# ─── Обучение одной модели ────────────────────────────────────────────────────

def train_model(items: list[dict], vocab: dict[str, int], tablet_type: str) -> dict:
    """Обучает LightGBM модель для одного типа планшетки. Возвращает метрики."""
    X = np.array([extract_features(it, vocab) for it in items])
    y = np.log1p(np.array([it["price_d"] for it in items], dtype=np.float32))

    feature_names = [f"mod_{i}" for i in range(len(vocab))]

    X_train, X_val, y_train, y_val = train_test_split(
        X, y, test_size=0.2, random_state=42
    )

    train_ds = lgb.Dataset(X_train, label=y_train, feature_name=feature_names)
    val_ds   = lgb.Dataset(X_val,   label=y_val,   reference=train_ds)

    params = {
        "objective":      "regression",
        "metric":         "rmse",
        "learning_rate":  0.05,
        "num_leaves":     31,
        "min_data_in_leaf": max(5, len(X_train) // 20),
        "feature_fraction": 0.8,
        "bagging_fraction": 0.8,
        "bagging_freq":   5,
        "verbose":        -1,
    }

    callbacks = [lgb.early_stopping(30, verbose=False), lgb.log_evaluation(period=0)]

    model = lgb.train(
        params,
        train_ds,
        num_boost_round=500,
        valid_sets=[val_ds],
        callbacks=callbacks,
    )

    y_pred = model.predict(X_val)
    # RMSE в log-space → MAE в divine через обратное преобразование
    mae_d = float(np.mean(np.abs(np.expm1(y_pred) - np.expm1(y_val))))
    rmse_log = float(np.sqrt(np.mean((y_pred - y_val) ** 2)))

    print(f"  val RMSE(log): {rmse_log:.4f}  |  MAE(divine): {mae_d:.2f}d")

    return {"model": model, "mae_d": mae_d, "rmse_log": rmse_log, "n_train": len(X_train), "n_val": len(X_val)}


# ─── Сохранение ──────────────────────────────────────────────────────────────

def save_model(tablet_type: str, model, vocab: dict[str, int], metrics: dict, models_dir: Path):
    out = models_dir / tablet_type
    out.mkdir(parents=True, exist_ok=True)

    # LightGBM native format — можно загрузить из любого языка
    model.save_model(str(out / "model.txt"))

    # Словарь hash → feature_index (для inference)
    with open(out / "vocab.json", "w", encoding="utf-8") as f:
        json.dump(vocab, f, indent=2)

    # Метаданные и имена фич
    idx_to_hash = {v: k for k, v in vocab.items()}
    importance = dict(zip(
        [idx_to_hash.get(i, f"feat_{i}") for i in range(len(vocab))],
        model.feature_importance(importance_type="gain").tolist(),
    ))
    top_features = sorted(importance.items(), key=lambda x: -x[1])[:20]

    meta = {
        "tablet_type": tablet_type,
        "n_train":     metrics["n_train"],
        "n_val":       metrics["n_val"],
        "mae_divine":  round(metrics["mae_d"], 3),
        "rmse_log":    round(metrics["rmse_log"], 4),
        "n_features":  len(vocab),
        "top_features": top_features,
    }
    with open(out / "metadata.json", "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2, ensure_ascii=False)

    print(f"  → сохранено: {out}/")
    print(f"    топ-3 фичи: {top_features[:3]}")


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-dir",    default=str(ROOT / "trade_data"))
    parser.add_argument("--models-dir",  default=str(HERE / "models"))
    parser.add_argument("--min-samples", type=int, default=30,
                        help="Минимум предметов для обучения (по умолчанию 30)")
    parser.add_argument("--types", nargs="*",
                        help="Типы планшеток для обучения (по умолчанию все)")
    args = parser.parse_args()

    data_dir   = Path(args.data_dir)
    models_dir = Path(args.models_dir)
    ninja_path = ROOT / "poe_ninja_prices.json"

    print("=== Tablet Evaluator Training ===")
    print(f"trade_data:  {data_dir}")
    print(f"models_dir:  {models_dir}")

    rates = build_divine_rates(ninja_path)
    print(f"Курсы валют загружены: {len(rates)} позиций")

    print("\n[1] Загрузка планшеток из trade_data/")
    by_type = load_items(data_dir, rates)

    if not by_type:
        print("Планшетки не найдены. Снимите снапшоты через вкладку Наблюдение.")
        return

    target_types = set(args.types) if args.types else set(by_type.keys())

    for tablet_type, items in sorted(by_type.items()):
        if tablet_type not in target_types:
            continue

        print(f"\n[{tablet_type}]")

        if len(items) < args.min_samples:
            print(f"  пропуск: {len(items)} < {args.min_samples} (мин. выборка)")
            continue

        items = filter_price_outliers(items)

        if len(items) < args.min_samples:
            print(f"  пропуск после фильтрации: {len(items)} < {args.min_samples}")
            continue

        vocab = build_vocab(items)
        print(f"  фич (уникальных модов): {len(vocab)}")

        if len(vocab) == 0:
            print("  пропуск: нет модов с hash")
            continue

        result = train_model(items, vocab, tablet_type)
        save_model(tablet_type, result["model"], vocab, result, models_dir)

    print("\n=== Готово ===")


if __name__ == "__main__":
    main()
