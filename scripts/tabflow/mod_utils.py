"""
mod_utils.py — единые функции нормализации модов PoE2.

Используется во всех скриптах TabFlow (reprice.py, evaluate_clipboard.py,
match_sales.py). Единый источник истины для алгоритма сравнения модов.
"""
from __future__ import annotations
import re

_POE_TAG = re.compile(r'\[([^\|]+\|)?([^\]]+)\]')   # [Tag|Display] → Display
_RANGE   = re.compile(r'\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)')
_NUMBER  = re.compile(r'\d+(?:\.\d+)?')


def strip_poe_tags(text: str) -> str:
    """[Curse|Hex] → Hex, [Fire Damage] → Fire Damage."""
    return _POE_TAG.sub(lambda m: m.group(2), text)


def normalize_mod(mod: str) -> str:
    """Убрать PoE-теги, привести к нижнему регистру."""
    return strip_poe_tags(mod).strip().lower()


def mod_template(mod: str) -> str:
    """
    Шаблон для сравнения: теги→текст, диапазоны убраны, числа→#, lowercase.
    Позволяет сопоставлять "5(5-7)% increased Pack Size" (листинг)
    с "5% increased Pack Size" (продажа из GGG API).
    """
    s = strip_poe_tags(mod)
    s = _RANGE.sub('', s)
    s = _NUMBER.sub('#', s)
    return s.strip().lower()


def mod_set(mods: list[str]) -> frozenset[str]:
    """Нормализованное множество шаблонов модов."""
    return frozenset(mod_template(m) for m in mods if m.strip())
