#!/usr/bin/env python3
"""
Оценка планшетки из текста буфера обмена (PoE2).
Вызывается из GameHelper: python evaluate_clipboard.py --file /path/to/item.txt
"""
import argparse
import json
import math
import re
import sys
from pathlib import Path

HERE = Path(__file__).parent


def parse_item(text: str) -> tuple[str, list[str]]:
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
    non_meta = [l for l in header
                if not l.startswith("Item Class:") and not l.startswith("Rarity:")]
    if len(non_meta) >= 2:
        base_type = non_meta[-1]   # Rarity Magic/Rare: предпоследняя = имя, последняя = база
    elif len(non_meta) == 1:
        base_type = non_meta[0]    # Normal: единственная строка = база
    else:
        return "", []

    SKIP = [
        re.compile(r'Adds .+? to a Map', re.I),      # implicit планшетки
        re.compile(r'\d+ uses remaining', re.I),       # implicit uses
        re.compile(r'^\{.*\}$'),                       # { Prefix Modifier "..." } заголовки
        re.compile(r'^Item Level:', re.I),             # Item Level: 82
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

    vocab_path        = model_dir / "vocab.json"
    model_path        = model_dir / "model.txt"
    meta_path         = model_dir / "metadata.json"
    text_to_hash_path = model_dir / "text_to_hash.json"

    if not vocab_path.exists() or not model_path.exists():
        return f"Файлы модели отсутствуют в {model_dir}"

    try:
        import lightgbm as lgb
        import numpy as np
    except ImportError:
        return "lightgbm не установлен: .venv/bin/pip install lightgbm numpy"

    vocab        = json.loads(vocab_path.read_text(encoding='utf-8'))
    text_to_hash = json.loads(text_to_hash_path.read_text(encoding='utf-8')) \
                   if text_to_hash_path.exists() else {}
    model = lgb.Booster(model_file=str(model_path))

    n = len(vocab)
    vec = np.zeros(n, dtype=np.float32)

    matched: list[str] = []
    unmatched: list[str] = []
    for mod_text in mods:
        # Убираем диапазоны вида (25-35) которые PoE2 добавляет в буфер рядом со значением
        clean = re.sub(r'\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)', '', mod_text).strip()
        normalized = re.sub(r'\d+(?:\.\d+)?', '#', clean).strip()

        found = False

        # Приоритет 1: explicit.stat_ хэш через маппинг текст→хэш (более информативная фича)
        stat_hash = text_to_hash.get(normalized)
        if stat_hash is None:
            # Вариант с "an" → "#" для модов вида "an additional time"
            an_norm = re.sub(r'\ban\b', '#', normalized).strip()
            stat_hash = text_to_hash.get(an_norm)
        if stat_hash and stat_hash in vocab:
            vec[vocab[stat_hash]] = 1.0
            found = True

        # Приоритет 2: text: ключ (из text-fallback снапшотов — если stat_hash нет)
        if not found:
            key = f"text:{normalized}"
            if key in vocab:
                vec[vocab[key]] = 1.0
                found = True

        if found:
            matched.append(mod_text)
        else:
            unmatched.append(mod_text)

    if not matched:
        lines = [f"Тип: {base_type}", "Ни один мод не распознан."]
        if mods:
            lines.append("Строки модов:")
            for m in mods:
                lines.append(f"  {m}")
        return "\n".join(lines)

    pred_log = float(model.predict(vec.reshape(1, -1))[0])
    price = math.expm1(pred_log)

    mae = None
    n_train = None
    if meta_path.exists():
        meta = json.loads(meta_path.read_text(encoding='utf-8'))
        mae = meta.get('mae_divine')
        n_train = meta.get('n_train')

    out = [f"Тип: {base_type}"]
    out.append(f"Оценка: ~{price:.1f}d" + (f"  (±{mae:.2f}d)" if mae else ""))
    if n_train:
        out.append(f"Модель обучена на {n_train} предметах")
    out.append(f"Распознано модов: {len(matched)}/{len(mods)}")
    for m in matched:
        out.append(f"  ✓ {m}")
    if unmatched:
        out.append(f"Нераспознано ({len(unmatched)}):")
        for m in unmatched:
            out.append(f"  ? {m}")
    return "\n".join(out)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--file', help='Файл с текстом предмета')
    ap.add_argument('--models-dir', default=str(HERE / 'models'))
    args = ap.parse_args()

    if args.file:
        text = Path(args.file).read_text(encoding='utf-8-sig')
    else:
        text = sys.stdin.read()

    if not text.strip():
        print("Пустой ввод")
        sys.exit(1)

    base_type, mods = parse_item(text)

    if not base_type:
        print("Не удалось определить тип предмета")
        sys.exit(1)

    if 'tablet' not in base_type.lower():
        print(f"Это не планшетка: {base_type}")
        sys.exit(1)

    print(predict(base_type, mods, Path(args.models_dir)))


if __name__ == '__main__':
    main()
