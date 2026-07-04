"""
Скрейпит уникальные планшеты с poe2db.tw и генерирует записи для affix_library.json.

Для каждого базового типа планшета:
1. Загружает страницу (напр. /us/Ritual_Tablet)
2. Находит ссылки на уникальные предметы в разделе "Unique"
3. Для каждого уникального предмета загружает его страницу
4. Извлекает <script type="application/json"> с explicitMods
5. Выводит записи в формате affix_library.json

Использование:
    python scrape_unique_tablets.py [--output unique_tablets.json] [--force]
"""

import json
import re
import time
import os
import sys
import argparse
import urllib.request
import urllib.error
from pathlib import Path

BASE_URL = "https://poe2db.tw/us"
CACHE_DIR = Path(os.environ.get("TEMP", "/tmp")) / "poe2db_unique_cache"
CACHE_DIR.mkdir(parents=True, exist_ok=True)

TABLET_TYPES = [
    ("Abyss_Tablet",      "Abyss"),
    ("Breach_Tablet",     "Breach"),
    ("Delirium_Tablet",   "Delirium"),
    ("Expedition_Tablet", "Expedition"),
    ("Irradiated_Tablet", "Irradiated"),
    ("Overseer_Tablet",   "Overseer"),
    ("Ritual_Tablet",     "Ritual"),
    ("Temple_Tablet",     "Temple"),
]

HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
    "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
}

# [Key|Display] → Display;  [Key] → Key
RE_WITH_DISPLAY = re.compile(r'\[([^|\]]*)\|([^\]]*)\]')
RE_WITHOUT_DISPLAY = re.compile(r'\[([^\]|]+)\]')
RE_NUMBER = re.compile(r'\d[\d,.]*')

def strip_markup(text: str) -> str:
    s = RE_WITH_DISPLAY.sub(lambda m: m.group(2), text)
    return RE_WITHOUT_DISPLAY.sub(lambda m: m.group(1), s)

def normalize_to_template(text: str) -> str:
    """Заменяет все числа на # — формат шаблона affix_library."""
    return RE_NUMBER.sub('#', text)

def fetch_html(url_path: str, force: bool = False) -> str:
    safe = re.sub(r'[^a-zA-Z0-9_-]', '_', url_path)
    cache_file = CACHE_DIR / f"{safe}.html"

    if not force and cache_file.exists():
        print(f"  [cache] {url_path}")
        return cache_file.read_text(encoding="utf-8")

    url = f"{BASE_URL}/{url_path}"
    print(f"  [fetch] {url}")
    req = urllib.request.Request(url, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            html = resp.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code} for {url}")

    cache_file.write_text(html, encoding="utf-8")
    time.sleep(0.8)  # вежливая задержка
    return html

def find_unique_links(html: str) -> list[str]:
    """
    Ищет ссылки в секции Unique на базовой странице планшета.
    poe2db.tw: <a href="/us/ItemName">ItemName</a> внутри секции unique_items_list.
    """
    # Ищем все href="/us/<Name>" — уникальные предметы обычно идут рядом с "Unique /"
    # Ограничиваем контекст: берём только то, что находится после "Unique /" метки
    unique_section = re.search(r'Unique\s*/\s*\d+.*', html, re.DOTALL)
    if not unique_section:
        return []

    section_html = unique_section.group(0)
    # Ссылки вида href="/us/SomeName" (не якорные, не категорийные)
    links = re.findall(r'href="/us/([A-Za-z][A-Za-z0-9_\-\']+)"', section_html)
    # Фильтруем известные не-предметные пути
    skip = {"Item", "Modifiers", "Quest", "Economy", "Unique_item", "Items", "Patreon"}
    seen = set()
    result = []
    for link in links:
        if link not in skip and link not in seen:
            seen.add(link)
            result.append(link)
    return result

BIOMES = ["Water", "Mountain", "Grass", "Forest", "Swamp", "Desert"]
# [Biome|X] → X, но Mastered Domain выдаёт только один вариант из экземпляра.
# Возвращаем все возможные биомы чтобы matching работал для любого варианта.
RE_BIOME = re.compile(r'Map also counts as a \S+ Map')

def expand_biome_variants(mods: list[str]) -> list[str]:
    """Если мод содержит биом — добавляем все 6 вариантов."""
    result = []
    for mod in mods:
        if RE_BIOME.match(mod):
            result.extend(f"Map also counts as a {b} Map" for b in BIOMES)
        else:
            result.append(mod)
    return result

def is_tablet_page(html: str) -> bool:
    """Проверяет что страница — планшет, а не босс/NPC."""
    title_m = re.search(r'<meta property="og:title" content="([^"]+)"', html)
    if title_m and "Tablet" in title_m.group(1):
        return True
    # Проверяем typeLine в JSON
    tl = re.search(r'"typeLine"\s*:\s*"([^"]+Tablet[^"]*)"', html)
    if tl:
        return True
    return False

def extract_mods_from_json(html: str) -> tuple[str, list[str]] | None:
    """
    Метод 1: JSON-блок с frameType=3 внутри <div>.
    Возвращает (name, explicit_mods) или None.
    """
    for m in re.finditer(r'"frameType"\s*:\s*3', html):
        pos = m.start()
        start = pos
        depth = 0
        for i in range(pos, max(0, pos - 8000), -1):
            c = html[i]
            if c == '}': depth += 1
            elif c == '{':
                if depth == 0: start = i; break
                depth -= 1
        end = pos
        depth = 0
        for i in range(start, min(len(html), start + 8000)):
            c = html[i]
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: end = i + 1; break
        try:
            data = json.loads(html[start:end])
            name = data.get("name", "")
            mods = data.get("explicitMods", [])
            if name and mods:
                return name, [normalize_to_template(strip_markup(m)) for m in mods]
        except json.JSONDecodeError:
            continue
    return None

def extract_mods_from_meta(html: str) -> tuple[str, list[str]] | None:
    """
    Метод 2: og:title + og:description мета-теги.
    Возвращает (name, explicit_mods) или None.
    """
    title_m = re.search(r'<meta property="og:title" content="([^"]+)"', html)
    desc_m  = re.search(r'<meta property="og:description" content="([^"]+)"', html)
    if not title_m or not desc_m:
        return None
    # og:title = "Item Name Base Type" — берём всё до последнего слова типа планшета
    title = title_m.group(1)
    # Удаляем суффикс "... Tablet" из названия чтобы получить имя предмета
    name_m = re.match(r'^(.+?)\s+\w+ Tablet\s*$', title)
    name = name_m.group(1) if name_m else title
    mods_text = title_m.group(1)  # fallback
    desc = desc_m.group(1)
    mods = [normalize_to_template(m.strip()) for m in desc.split('\n') if m.strip()]
    if mods:
        return name, mods
    return None

def extract_item_json(html: str) -> dict | None:
    """Устаревший интерфейс — не используется напрямую."""
    return None

def parse_unique_item(url_path: str, sub_class: str, force: bool) -> list[dict]:
    """Загружает страницу уникального предмета и возвращает записи библиотеки."""
    try:
        html = fetch_html(url_path, force)
    except RuntimeError as e:
        print(f"    SKIP {url_path}: {e}")
        return []

    # Фильтр: пропускаем не-планшетные страницы (боссы, NPC)
    if not is_tablet_page(html):
        print(f"    SKIP {url_path}: not a tablet page")
        return []

    # Метод 1: JSON с frameType=3
    result = extract_mods_from_json(html)
    # Метод 2: og:description мета-тег
    if not result:
        result = extract_mods_from_meta(html)
    if not result:
        print(f"    WARN {url_path}: no mods found")
        return []

    name, mods = result
    mods = expand_biome_variants(mods)
    print(f"    OK   {name}: {len(mods)} mods")

    entry = {
        "itemClasses":    ["Tablet"],
        "affixType":      "Unique Modifier",
        "affixName":      name,
        "affixTier":      1,
        "affixTierLevel": 65,
        "affixStats":     mods,
        "affixRanges":    [None] * len(mods),
        "affixSubClass":  sub_class,
        "familyId":       url_path,
        "weight":         None,
    }
    return [entry]

def main():
    parser = argparse.ArgumentParser(description="Scrape unique tablets from poe2db.tw")
    parser.add_argument("--output", default="unique_tablets.json",
                        help="Output JSON file (default: unique_tablets.json)")
    parser.add_argument("--force", action="store_true",
                        help="Ignore cache, re-fetch all pages")
    args = parser.parse_args()

    all_entries = []

    for base_url, sub_class in TABLET_TYPES:
        print(f"\n=== {base_url} ({sub_class}) ===")
        try:
            base_html = fetch_html(base_url, args.force)
        except RuntimeError as e:
            print(f"  FAILED: {e}")
            continue

        unique_links = find_unique_links(base_html)
        if not unique_links:
            print(f"  No unique links found")
            continue
        print(f"  Unique items: {unique_links}")

        for link in unique_links:
            entries = parse_unique_item(link, sub_class, args.force)
            all_entries.extend(entries)

    output = {"version": 1, "entries": all_entries}
    out_path = Path(args.output)
    out_path.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nTotal: {len(all_entries)} unique tablet entries → {out_path}")

    # Показываем итоговые записи
    print("\n--- Записи ---")
    for e in all_entries:
        print(f"  [{e['affixSubClass']}] {e['affixName']}")
        for stat in e['affixStats']:
            print(f"    · {stat[:80]}")

if __name__ == "__main__":
    main()
