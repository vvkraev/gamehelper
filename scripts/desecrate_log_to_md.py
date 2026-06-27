#!/usr/bin/env python3
"""
Конвертирует JSONL-лог десекрейт-сессии в читаемые Markdown-файлы.
Сохраняет в хранилище Obsidian с правильной структурой папок.

Использование:
    python3 scripts/desecrate_log_to_md.py \\
        --log trade_data/desecrate_reveal_log.jsonl \\
        --item time_lost_sapphire \\
        --batch batch_2026-06-26 \\
        --base-names "pandemonium_splinter,hypnotic_shine,base_3,base_4"

    python3 scripts/desecrate_log_to_md.py \\
        --log trade_data/2026-06-27_ring_desecrate_log.jsonl \\
        --item ring \\
        --session desecrate_2026-06-27

Структура вывода:
    Батч:   vault/crafts/{item}/{batch}/{base_name}/desecrate_reveal.md
    Одиночный: vault/crafts/{item}/{session}/desecrate_reveal.md
"""

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

VAULT_PATH = Path(__file__).parent.parent / "vault"


def load_log(path: str) -> list[dict]:
    entries = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                entries.append(json.loads(line))
    return entries


def group_by_base_cycle(entries: list[dict]) -> dict:
    grouped = defaultdict(lambda: defaultdict(list))
    for e in entries:
        base = e.get("base", 1)
        cycle = e.get("cycle", 1)
        grouped[base][cycle].append(e)
    return grouped


def shorten_mod(mod: str, max_len: int = 90) -> str:
    return mod if len(mod) <= max_len else mod[:max_len - 1] + "…"


def render_base_md(base_num: int, cycles: dict, item: str, date: str) -> str:
    lines = []
    lines.append(f"# Десекрейт-ревил: {item} — База {base_num} ({date})")
    lines.append("")

    total_ool = len(cycles)
    total_cranium = len(cycles)
    total_oae = 0
    found = False

    for cycle_num in sorted(cycles):
        attempts = cycles[cycle_num]
        lines.append(f"## Цикл {cycle_num}")

        for attempt in attempts:
            att_num = attempt.get("attempt", 1)
            mods = attempt.get("mods", [])
            found_target = attempt.get("found_target", False)
            chosen = attempt.get("chosen_mod", "")
            no_echo = attempt.get("no_abyssal_echo", False)
            note = attempt.get("note", "")

            if att_num == 1:
                prefix = "**Попытка 1** (OoLight + Cranium):"
            else:
                prefix = "**Попытка 2** (OoAE):"
                if not no_echo:
                    total_oae += 1

            status = "✅" if found_target else "❌"
            lines.append(f"{prefix} {status}")
            for m in mods:
                if found_target and m == chosen:
                    lines.append(f"- **→ {shorten_mod(m)}** ← *выбрано*")
                else:
                    lines.append(f"- {shorten_mod(m)}")

            if no_echo:
                lines.append("> ⚠️ OoAE не использован: закончились")
            if note:
                lines.append(f"> 📝 {note}")
            lines.append("")

            if found_target:
                found = True

    lines.append("---")
    lines.append("")
    lines.append("## Расходники")
    lines.append("")
    lines.append("| Материал | Шт |")
    lines.append("|---|:---:|")
    lines.append(f"| Omen of Light | {total_ool} |")
    lines.append(f"| Preserved Cranium | {total_cranium} |")
    lines.append(f"| Omen of Abyssal Echo | {total_oae} |")
    lines.append(f"| **Итог циклов** | **{len(cycles)}** |")
    lines.append(f"| **Результат** | {'✅ найдено' if found else '⏳ в процессе'} |")

    return "\n".join(lines)


def render_summary_md(entries: list[dict], grouped: dict) -> str:
    first = entries[0]
    item = first.get("item", "Unknown")
    date = first.get("date", "")

    total_cycles = sum(len(c) for c in grouped.values())
    total_ool = total_cycles
    total_cranium = total_cycles
    total_oae = 0
    found_count = 0

    for base_cycles in grouped.values():
        base_found = False
        for cycle_attempts in base_cycles.values():
            for a in cycle_attempts:
                if a.get("found_target"):
                    base_found = True
                att_num = a.get("attempt", 1)
                no_echo = a.get("no_abyssal_echo", False)
                if att_num == 2 and not no_echo:
                    total_oae += 1
        if base_found:
            found_count += 1

    lines = []
    lines.append(f"# Десекрейт-сессия: {item} ({date})")
    lines.append("")
    lines.append("| Параметр | Значение |")
    lines.append("|---|---|")
    lines.append(f"| Базы | {len(grouped)} |")
    lines.append(f"| Успешных | {found_count} / {len(grouped)} |")
    lines.append(f"| Всего циклов | {total_cycles} |")
    lines.append(f"| Omen of Light | {total_ool} |")
    lines.append(f"| Preserved Cranium | {total_cranium} |")
    lines.append(f"| Omen of Abyssal Echo | {total_oae} |")
    lines.append(f"| Всего ревилов | {len(entries)} |")
    lines.append("")
    lines.append("## Базы")
    lines.append("")
    for base_num in sorted(grouped):
        cycles = grouped[base_num]
        n_cycles = len(cycles)
        base_found = any(
            a.get("found_target")
            for c in cycles.values()
            for a in c
        )
        chosen = next(
            (a.get("chosen_mod", "") for c in cycles.values() for a in c if a.get("found_target")),
            ""
        )
        status = f"✅ {shorten_mod(chosen, 60)}" if base_found else "⏳ в процессе"
        lines.append(f"- **База {base_num}** ({n_cycles} цикл.) — {status}")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Десекрейт-лог → Markdown в Obsidian vault")
    parser.add_argument("--log", required=True, help="Путь к .jsonl файлу")
    parser.add_argument("--item", required=True, help="Слаг предмета (напр. time_lost_sapphire)")
    parser.add_argument("--batch", default=None, help="Имя батча (напр. batch_2026-06-26)")
    parser.add_argument("--session", default=None, help="Имя сессии одиночного предмета (напр. desecrate_2026-06-27)")
    parser.add_argument("--base-names", default=None, help="Имена баз через запятую (напр. pandemonium_splinter,hypnotic_shine,base_3,base_4)")
    parser.add_argument("--vault", default=str(VAULT_PATH), help="Путь к vault (по умолчанию: vault/)")
    args = parser.parse_args()

    entries = load_log(args.log)
    grouped = group_by_base_cycle(entries)

    first = entries[0]
    item_name = first.get("item", args.item)
    date = first.get("date", "")

    vault = Path(args.vault)
    crafts_root = vault / "crafts" / args.item

    base_names: dict[int, str] = {}
    if args.base_names:
        for i, name in enumerate(args.base_names.split(","), start=1):
            base_names[i] = name.strip()

    if args.batch:
        batch_root = crafts_root / args.batch

        # Per-base files
        for base_num in sorted(grouped):
            base_slug = base_names.get(base_num, f"base_{base_num}")
            base_dir = batch_root / base_slug
            base_dir.mkdir(parents=True, exist_ok=True)

            md = render_base_md(base_num, grouped[base_num], item_name, date)
            out = base_dir / "desecrate_reveal.md"
            out.write_text(md, encoding="utf-8")
            print(f"  → {out}", file=sys.stderr)

        # Batch summary
        summary_md = render_summary_md(entries, grouped)
        summary_out = batch_root / "_desecrate_summary.md"
        summary_out.write_text(summary_md, encoding="utf-8")
        print(f"  → {summary_out}", file=sys.stderr)

    elif args.session:
        session_dir = crafts_root / args.session
        session_dir.mkdir(parents=True, exist_ok=True)

        if len(grouped) == 1:
            # Один предмет — один файл
            base_num = list(grouped.keys())[0]
            md = render_base_md(base_num, grouped[base_num], item_name, date)
        else:
            md = render_summary_md(entries, grouped)

        out = session_dir / "desecrate_reveal.md"
        out.write_text(md, encoding="utf-8")
        print(f"  → {out}", file=sys.stderr)

    else:
        print("Укажи --batch или --session", file=sys.stderr)
        sys.exit(1)

    print("Готово.", file=sys.stderr)


if __name__ == "__main__":
    main()
