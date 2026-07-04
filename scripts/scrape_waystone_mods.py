"""
Скрейпит моды Waystones с poe2db.tw/us/Waystones и генерирует записи для affix_library.json.

Структура: HTML-таблица в div#WaystonesMods — уровень, тип, текст мода с HTML-разметкой.
Каждая строка = один мод (может содержать несколько stat-линий, разделённых <br>).
Тиры назначаются внутри каждой группы с одинаковым нормализованным шаблоном.

Использование:
    python scrape_waystone_mods.py [--output waystone_mods.json] [--force]
"""

import json
import re
import html as html_module
import time
import os
import urllib.request
import urllib.error
import argparse
from pathlib import Path
from collections import defaultdict

BASE_URL  = "https://poe2db.tw/us"
CACHE_DIR = Path(os.environ.get("TEMP", "/tmp")) / "poe2db_unique_cache"
CACHE_DIR.mkdir(parents=True, exist_ok=True)

HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
}

RE_NUMBER     = re.compile(r'\d[\d,.]*')
RE_RANGE      = re.compile(r'\((\d+(?:\.\d+)?)<span[^>]*>—</span>(\d+(?:\.\d+)?)\)')
RE_RANGE_TEXT = re.compile(r'\((\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)\)')


def fetch_html(url_path: str, force: bool = False) -> str:
    safe = re.sub(r'[^a-zA-Z0-9_-]', '_', url_path)
    cache_file = CACHE_DIR / f"{safe}.html"
    if not force and cache_file.exists():
        print(f"  [cache] {url_path}")
        return cache_file.read_text(encoding="utf-8")
    url = f"{BASE_URL}/{url_path}"
    print(f"  [fetch] {url}")
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=30) as resp:
        html = resp.read().decode("utf-8", errors="replace")
    cache_file.write_text(html, encoding="utf-8")
    time.sleep(0.8)
    return html


def clean_mod_html(raw: str) -> str:
    """HTML → чистый текст. Диапазоны (N—M) → (N-M), <br> → \n, теги убираются."""
    s = RE_RANGE.sub(lambda m: f"({m.group(1)}-{m.group(2)})", raw)
    s = re.sub(r'<br\s*/?>', '\n', s)
    s = re.sub(r'<[^>]+>', '', s)
    s = html_module.unescape(s)
    lines = [l.strip() for l in s.split('\n')]
    return '\n'.join(l for l in lines if l)


def normalize_to_template(text: str) -> str:
    """Заменяет числа и диапазоны (N-M) на # — шаблон для matching."""
    s = RE_RANGE_TEXT.sub('#', text)
    return RE_NUMBER.sub('#', s)


def extract_range(text: str) -> str | None:
    """Извлекает первый диапазон (N-M) из текста мода."""
    m = RE_RANGE_TEXT.search(text)
    if m:
        return f"{m.group(1)}-{m.group(2)}"
    m2 = RE_NUMBER.search(text)
    return m2.group(0) if m2 else None


def parse_waystone_table(html: str) -> list[dict]:
    """Парсит таблицу WaystonesMods, возвращает список {level, type, clean, template, range}."""
    start = html.find('id="WaystonesMods"')
    end   = html.find('id="LevelResistPenalty"', start)
    if start < 0:
        raise RuntimeError("WaystonesMods section not found")
    section = html[start:end if end > start else start + 200000]

    rows = re.findall(
        r'<tr><td>(\d+)</td><td>(Prefix|Suffix)</td><td>(.*?)</td></tr>',
        section, re.DOTALL
    )

    result = []
    for level, typ, raw in rows:
        clean = clean_mod_html(raw)
        if not clean:
            continue
        # Каждая строка — один мод с несколькими stat-линиями
        stats     = [l for l in clean.split('\n') if l]
        templates = [normalize_to_template(s) for s in stats]
        ranges    = [extract_range(s) for s in stats]
        result.append({
            "level":     int(level),
            "type":      typ,
            "stats":     stats,
            "templates": templates,
            "ranges":    ranges,
        })
    return result


def assign_tiers(rows: list[dict]) -> list[dict]:
    """
    Назначает тиры внутри каждой группы модов с одинаковым шаблоном первого стата.
    T1 = наибольший level (воспроизводит логику poe2db.tw).
    """
    # Группируем по нормализованному шаблону + типу
    families: dict[str, list[int]] = defaultdict(list)
    for row in rows:
        key = row["type"] + "|" + "|".join(row["templates"])
        families[key].append(row["level"])

    # Строим tier-map для каждой группы: level → tier
    tier_maps: dict[str, dict[int, int]] = {}
    for key, levels in families.items():
        unique_sorted = sorted(set(levels), reverse=True)
        tier_maps[key] = {lv: i + 1 for i, lv in enumerate(unique_sorted)}

    for row in rows:
        key = row["type"] + "|" + "|".join(row["templates"])
        row["tier"] = tier_maps[key][row["level"]]
        row["family_key"] = key

    return rows


def rows_to_entries(rows: list[dict]) -> list[dict]:
    """Конвертирует строки таблицы в записи affix_library формата."""
    entries = []
    seen = set()
    for row in rows:
        key = row["family_key"] + f"|T{row['tier']}"
        if key in seen:
            continue
        seen.add(key)

        affix_type = "Prefix Modifier" if row["type"] == "Prefix" else "Suffix Modifier"
        # Имя мода — первая stat-линия, нормализованная
        name_raw = row["templates"][0]

        entry = {
            "itemClasses":    ["Waystones"],
            "affixType":      affix_type,
            "affixName":      name_raw,
            "affixTier":      row["tier"],
            "affixTierLevel": row["level"],
            "affixStats":     row["templates"],
            "affixRanges":    row["ranges"],
            "familyId":       row["family_key"],
            "weight":         None,
        }
        entries.append(entry)
    return entries


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="waystone_mods.json")
    parser.add_argument("--force",  action="store_true")
    args = parser.parse_args()

    print("=== Waystone Mods Scraper ===")
    html = fetch_html("Waystones", args.force)

    rows    = parse_waystone_table(html)
    print(f"Строк в таблице: {len(rows)}")

    rows    = assign_tiers(rows)
    entries = rows_to_entries(rows)
    print(f"Записей affix_library: {len(entries)}")

    out = {"version": 1, "entries": entries}
    Path(args.output).write_text(
        json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(f"Сохранено → {args.output}")

    print("\n--- Первые 15 записей ---")
    for e in entries[:15]:
        print(f"  T{e['affixTier']} {e['affixType'][:3]}: {' / '.join(e['affixStats'])[:90]}")

    # Проверка покрытия нераспознанных из кеша
    cache_path = Path(__file__).parent.parent / "parsed_mods_cache.json"
    if cache_path.exists():
        import re as re2
        with open(cache_path) as f:
            mcache = json.load(f)
        unmatched = set()
        for mods in mcache.values():
            for m in mods:
                if m.get("unmatched"):
                    unmatched.add(m["stripped"])

        def matches(stat, tmpl):
            a = re2.sub(r'\d[\d,.]*', '', stat.strip().lower()).strip()
            b = re2.sub(r'#', '', tmpl.strip().lower()).strip()
            while '  ' in a: a = a.replace('  ', ' ')
            while '  ' in b: b = b.replace('  ', ' ')
            return a == b and len(a) >= 5

        covered = set()
        for entry in entries:
            for tmpl in entry["affixStats"]:
                for um in list(unmatched):
                    if matches(um, tmpl):
                        covered.add(um)

        print(f"\nНераспознанных: {len(unmatched)}")
        print(f"Покрывается этим скрейпом: {len(covered)}")
        print("\n=== Покрытые ===")
        for m in sorted(covered):
            print(f"  ✓ {m[:100]}")


if __name__ == "__main__":
    main()
