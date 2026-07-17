#!/usr/bin/env python3
"""
Оценка планшетки из текста буфера обмена (PoE2).
Вызывается из GameHelper: python evaluate_clipboard.py --file /path/to/item.txt
"""
import argparse
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).parent
ROOT = HERE.parent.parent

from mod_utils import mod_set as _mod_set
from predictor import TabletPredictor


def _market_floor(base_type: str) -> float | None:
    """p10 цена из floor-снэпшотов для данного base_type, в divine. None если нет данных."""
    trade_dir = ROOT / "trade_data"
    ninja_path = ROOT / "poe_ninja_prices.json"
    if not trade_dir.exists() or not ninja_path.exists():
        return None

    # Курсы валют из poe_ninja
    try:
        ninja = json.loads(ninja_path.read_text(encoding='utf-8-sig'))
        rates: dict[str, float] = {}
        for name, info in ninja["entries"][-1]["prices"].items():
            rates[name] = info["divineValue"]
        rates.setdefault("divine orb", 1.0)
        rates.setdefault("divine", 1.0)
    except Exception:
        return None

    bt_lower = base_type.strip().lower()
    prices: list[float] = []

    for f in sorted(trade_dir.glob("*_floor*.json")):
        try:
            data = json.loads(f.read_text(encoding='utf-8-sig'))
        except Exception:
            continue
        for item in data.get("listings", []):
            if item.get("base_type", "").strip().lower() != bt_lower:
                continue
            amount   = item.get("price_divine") or item.get("price_amount")
            currency = (item.get("price_currency") or "").strip().lower()
            if amount is None:
                continue
            rate = rates.get(currency) or rates.get(currency + " orb")
            if rate is None:
                continue
            prices.append(float(amount) * rate)

    if not prices:
        return None

    prices.sort()
    p10_idx = max(0, int(len(prices) * 0.10))
    return prices[p10_idx]


def _known_base_types(models_dir: Path) -> list[str]:
    """Список базовых типов из папок моделей (slug → Title Case)."""
    result = []
    for d in models_dir.iterdir():
        if d.is_dir():
            result.append(' '.join(w.capitalize() for w in d.name.split('_')))
    return result


def parse_item(text: str, models_dir: Path | None = None) -> tuple[str, list[str]]:
    """Возвращает (base_type, explicit_mods) из текста предмета."""
    lines = [l.strip() for l in text.replace('\r\n', '\n').split('\n')]

    sections: list[list[str]] = []
    current: list[str] = []
    for line in lines:
        if line == "--------":
            if current:
                sections.append(current)
            current = []
        elif line:
            current.append(line)
    if current:
        sections.append(current)

    if not sections:
        return "", []

    # Секция 1 — item class, rarity, [name], base
    header = sections[0]
    rarity = next((l.split(":", 1)[1].strip() for l in header if l.startswith("Rarity:")), "")
    non_meta = [l for l in header
                if not l.startswith("Item Class:") and not l.startswith("Rarity:")]

    if len(non_meta) >= 2:
        # Rare: две строки — имя, потом база
        base_type = non_meta[-1]
    elif len(non_meta) == 1:
        if rarity == "Magic":
            # Magic: одна строка — полное имя вида "Gilded Ritual Tablet of the Blight".
            # Извлекаем базовый тип по сабстрингу из известных моделей.
            magic_name = non_meta[0]
            base_type = magic_name  # fallback
            if models_dir is not None:
                known = _known_base_types(models_dir)
                # Ищем самое длинное совпадение (чтобы "Precursor Tablet" не перебил "Runic Tablet")
                for bt in sorted(known, key=len, reverse=True):
                    if bt.lower() in magic_name.lower():
                        base_type = bt
                        break
        else:
            base_type = non_meta[0]  # Normal: единственная строка = база
    else:
        return "", []

    SKIP = [
        re.compile(r'Adds .+? to (?:a|your) Map', re.I),  # implicit планшетки (to a Map / to your Map Device)
        re.compile(r'\d+ uses remaining', re.I),            # implicit uses
        re.compile(r'^\{.*\}$'),                            # { Prefix Modifier "..." } заголовки
        re.compile(r'^Item Level:', re.I),                  # Item Level: 82
        re.compile(r'Can be used in a personal Map Device', re.I),  # описание предмета
    ]
    MARKERS = {'Corrupted', 'Double Corrupted', 'Sanctified', 'Mirrored'}

    explicit_mods: list[str] = []
    for section in sections[1:]:
        for line in section:
            if line in MARKERS:
                continue
            if any(p.search(line) for p in SKIP):
                continue
            explicit_mods.append(line)

    return base_type, explicit_mods


def predict(base_type: str, mods: list[str], models_dir: Path) -> str:
    slug = re.sub(r'[^a-z0-9]+', '_', base_type.lower()).strip('_')
    model_dir = models_dir / slug

    if not model_dir.exists():
        return (
            f"Модель для '{base_type}' не найдена.\n"
            f"Обучите: .venv/bin/python3 train_evaluator.py --types {slug}"
        )

    try:
        p  = TabletPredictor(model_dir)
        ok = p.load()
    except ImportError:
        return "lightgbm не установлен: .venv/bin/pip install lightgbm numpy"

    if not ok:
        return f"Файлы модели отсутствуют в {model_dir}"

    result = p.predict_from_text_mods(mods)
    if result is None:
        lines = [f"Тип: {base_type}", "Ни один мод не распознан."]
        if mods:
            lines.append("Строки модов:")
            lines += [f"  {m}" for m in mods]
        return "\n".join(lines)

    price = result.price

    # ── Miss overrides (инста-продажи) ────────────────────────────────────────
    miss_note: str | None = None
    overrides_path = ROOT / "vault" / "tabflow" / "miss_overrides.json"
    if overrides_path.exists():
        try:
            overrides      = json.loads(overrides_path.read_text(encoding='utf-8'))
            item_templates = sorted(set(result.matched_templates))
            for ov in overrides:
                if ov.get("status") != "pending":
                    continue
                if ov.get("base_type", "").lower() != base_type.lower():
                    continue
                if sorted(ov.get("mod_templates", [])) != item_templates:
                    continue
                floor  = ov.get("floor_price", 0)
                miss_p = ov.get("miss_price", 0)
                mins   = ov.get("minutes_to_sale", "?")
                if floor > price:
                    price     = float(floor)
                    miss_note = (f"⚡ ИНСТА-КОРР: поднято до {floor}d "
                                 f"(модель: {result.price:.1f}d; 2× от продажи {miss_p}d за {mins} мин)")
                else:
                    miss_note = (f"✓ ИНСТА-КОРР неактивна: модель {result.price:.1f}d ≥ floor {floor}d"
                                 f" — можно снять override")
                break
        except Exception:
            pass

    market_floor = _market_floor(base_type)

    out = [f"Тип: {base_type}"]
    out.append(f"Оценка: ~{price:.1f}d" + (f"  (±{result.mae:.2f}d)" if result.mae else ""))
    if market_floor is not None:
        reforge_cost = market_floor * 3
        out.append(f"Рыночный флор: ~{market_floor:.2f}d/шт  (рефордж 3×={reforge_cost:.2f}d)")
    if miss_note:
        out.append(miss_note)
    if result.n_train:
        out.append(f"Модель обучена на {result.n_train} предметах")
    out.append(f"Распознано модов: {len(result.matched)}/{len(mods)}")
    for m, rn in result.matched:
        roll_tag = f"  [{rn*100:.0f}%]" if rn < 0.999 else ""
        out.append(f"  ✓ {m}{roll_tag}")
    for m in result.effect_mods:
        out.append(f"  × {m}  [мультипликатор ×{result.effect_mult:.2f}]")
    if result.unmatched:
        out.append(f"Нераспознано ({len(result.unmatched)}):")
        out += [f"  ? {m}" for m in result.unmatched]
    return "\n".join(out)


def _load_resolved_overrides() -> set[tuple[str, tuple[str, ...]]]:
    """Возвращает множество (base_type, mod_templates) для resolved overrides."""
    overrides_path = HERE.parent.parent / "vault" / "tabflow" / "miss_overrides.json"
    if not overrides_path.exists():
        return set()
    try:
        overrides = json.loads(overrides_path.read_text(encoding='utf-8'))
        return {
            (ov["base_type"], tuple(sorted(ov.get("mod_templates", []))))
            for ov in overrides
            if ov.get("status") == "resolved"
        }
    except Exception:
        return set()


def _entry_mod_templates(mods: list[str]) -> tuple[str, ...]:
    """Нормализует моды записи до шаблонов."""
    return tuple(sorted(_mod_set(mods)))


def check_misses(models_dir: Path) -> None:
    """Прогоняет активные miss-записи из listings_index.json через актуальную модель.
    Пропускает записи где override уже resolved (статус снят вручную).
    """
    index_path = HERE.parent.parent / "vault" / "tabflow" / "listings_index.json"
    if not index_path.exists():
        print("listings_index.json не найден")
        return

    entries  = json.loads(index_path.read_text(encoding='utf-8'))
    resolved = _load_resolved_overrides()
    misses   = [e for e in entries if e.get('miss') and e.get('itemText')]

    active = []
    for e in misses:
        base = (e.get('baseType') or '').lower()
        tmpl = _entry_mod_templates(e.get('mods') or [])
        if (base, tmpl) not in resolved:
            active.append(e)

    if not active:
        print("Нет активных промазов (все resolved).")
        return

    for e in active:
        sold_price = e.get('salePriceAmount', '?')
        initial    = e.get('initialPrice', '?')
        ts         = (e.get('timestamp') or '')[:16]
        name       = e.get('baseType', '')

        print(f"\n{'='*60}")
        print(f"  {ts}  {name}  (листинг {initial}d → продан за {sold_price}d)")
        print(f"{'='*60}")

        base_type, mods = parse_item(e['itemText'], models_dir)
        if not base_type:
            print("  [!] не удалось распарсить текст предмета")
            continue

        print(predict(base_type, mods, models_dir))


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--file', help='Файл с текстом предмета')
    ap.add_argument('--check-misses', action='store_true',
                    help='Прогнать все miss-записи из listings_index.json через модель')
    ap.add_argument('--models-dir', default=str(HERE / 'models'))
    args = ap.parse_args()

    models_dir = Path(args.models_dir)

    if args.check_misses:
        check_misses(models_dir)
        return

    if args.file:
        text = Path(args.file).read_text(encoding='utf-8-sig')
    else:
        text = sys.stdin.read()

    if not text.strip():
        print("Пустой ввод")
        sys.exit(1)

    base_type, mods = parse_item(text, models_dir)

    if not base_type:
        print("Не удалось определить тип предмета")
        sys.exit(1)

    if 'tablet' not in base_type.lower():
        print(f"Это не планшетка: {base_type}")
        sys.exit(1)

    print(predict(base_type, mods, models_dir))


if __name__ == '__main__':
    main()
