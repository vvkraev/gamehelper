#!/usr/bin/env python3
"""
Дифф последних двух снэпшотов для всех (или указанных) сессий из tracking_sessions.json.

Использование:
  python trade_diff.py                      # все сессии
  python trade_diff.py tls vvk aemecheck    # по ключевым словам в имени (регистр не важен)
"""

import json, sys, os

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SESSIONS_FILE = os.path.join(BASE, "tracking_sessions.json")

MOD_ABBR = {
    'of Unmaking':      'CDS',
    'of Annihilating':  'CHS',
    'of Potency':       'CD',
    'of Annihilation':  'CH',
    'of Enchanting':    'CAST',
    'of Mind':          'MK',
    'of Supremacy':     'Sup',
    'of Generation':    'Gen',
    'of Energy':        'MReg',
    'of Lengthening':   'Dur',
    'of Frost':         'Chill',
    'of Osmosis':       'Osm',
    'Lightless':        'Lght',
    'Acrimonious':      'Ail',
    'Jolting':          'Shk',
    'Mystic':           'SplDmg',
}


def mod_family_tier(mod: str) -> str:
    """'of Unmaking S1 — ...' → 'of Unmaking S1'"""
    return mod.split(' — ')[0].strip()


def mod_stat(mod: str) -> str:
    """'of Unmaking S1 — 10% increased...' → '10% increased...'"""
    parts = mod.split(' — ', 1)
    return parts[1].strip() if len(parts) > 1 else mod


def mod_short(family_tier: str) -> str:
    for prefix, abbr in MOD_ABBR.items():
        if family_tier.startswith(prefix):
            tier = next((w for w in family_tier.split() if len(w) > 1 and w[0] in ('S', 'P') and w[1:].isdigit()), '')
            return f"{abbr}{tier}"
    return family_tier[:16]


def group_mods(mods: list[str]) -> list[tuple[str, list[str]]]:
    """Группирует моды по FamilyName+Tier → [(family_tier, [stat1, stat2, ...]), ...]"""
    seen: dict[str, list[str]] = {}
    order: list[str] = []
    for m in mods:
        ft = mod_family_tier(m)
        if ft not in seen:
            seen[ft] = []
            order.append(ft)
        seen[ft].append(mod_stat(m))
    return [(ft, seen[ft]) for ft in order]


def mods_str(item: dict) -> str:
    parts = []
    for ft, _ in group_mods(item.get('mods_fractured', [])):
        parts.append(f"[F]{mod_short(ft)}")
    for ft, _ in group_mods(item.get('mods_desecrated', [])):
        parts.append(f"[D]{mod_short(ft)}")
    for ft, _ in group_mods(item.get('mods_explicit', [])):
        parts.append(mod_short(ft))
    for ft, _ in group_mods(item.get('mods_crafted', [])):
        parts.append(f"[C]{mod_short(ft)}")
    return ' | '.join(parts)


def load_snapshot(rel_path: str) -> dict:
    full = os.path.join(BASE, rel_path.replace('\\', '/'))
    with open(full, encoding='utf-8-sig') as f:
        data = json.load(f)
    return {item['id']: item for item in data.get('listings', [])}


def diff_session(session: dict):
    snaps = session.get('Snapshots', [])
    if len(snaps) < 2:
        print(f"\n[{session['Name']}]  только {len(snaps)} снэпшот(а), дифф невозможен")
        return

    prev_ref = snaps[-2]
    curr_ref = snaps[-1]

    prev = load_snapshot(prev_ref['FilePath'])
    curr = load_snapshot(curr_ref['FilePath'])

    gone_ids = set(prev) - set(curr)
    new_ids  = set(curr) - set(prev)
    stay_ids = set(prev) & set(curr)

    price_changed = [
        (id, prev[id], curr[id]) for id in stay_ids
        if abs(prev[id]['price_divine'] - curr[id]['price_divine']) > 0.5
    ]

    prev_ts = prev_ref['ImportedAt'][:16].replace('T', ' ')
    curr_ts = curr_ref['ImportedAt'][:16].replace('T', ' ')

    print(f"\n{'='*64}")
    print(f"[{session['Name']}]  {prev_ts} → {curr_ts}")
    print(f"  Было: {len(prev)}  Стало: {len(curr)}  Ушло: {len(gone_ids)}  Новых: {len(new_ids)}")

    if gone_ids:
        print(f"\n  УШЛО ({len(gone_ids)}):")
        for id in sorted(gone_ids, key=lambda i: prev[i]['price_divine']):
            item = prev[id]
            print(f"    {item['price_divine']:>6.0f}d  {item['name']:<24}  {mods_str(item)}")

    if new_ids:
        print(f"\n  НОВОЕ ({len(new_ids)}):")
        for id in sorted(new_ids, key=lambda i: curr[i]['price_divine']):
            item = curr[id]
            print(f"    {item['price_divine']:>6.0f}d  {item['name']:<24}  {mods_str(item)}")

    if price_changed:
        print(f"\n  ЦЕНА ИЗМЕНИЛАСЬ ({len(price_changed)}):")
        for id, p, c in sorted(price_changed, key=lambda x: x[2]['price_divine']):
            delta = c['price_divine'] - p['price_divine']
            sign = '+' if delta > 0 else ''
            print(f"    {p['price_divine']:>6.0f}→{c['price_divine']:<6.0f}d ({sign}{delta:.0f})  "
                  f"{c['name']:<24}  {mods_str(c)}")

    if not gone_ids and not new_ids and not price_changed:
        print("  (нет изменений)")


def main():
    with open(SESSIONS_FILE, encoding='utf-8-sig') as f:
        sessions = json.load(f)

    filters = [a.lower() for a in sys.argv[1:]]
    if filters:
        sessions = [s for s in sessions if any(f in s['Name'].lower() for f in filters)]

    if not sessions:
        print("Нет подходящих сессий.")
        return

    for session in sessions:
        diff_session(session)


if __name__ == '__main__':
    main()
