"""
currency.py — конвертер валют на основе poe_ninja_prices.json.

Все внутренние расчёты ведутся в divine (float).
Публичный API:
  cc = CurrencyConverter.load()          # грузит свежий снэпшот poe_ninja
  cc.to_divine(amount, currency)         # любую сумму → divine
  cc.to_chaos(amount_divine)             # divine → chaos (float)
  cc.to_chaos_int(amount_divine)         # divine → chaos (int, округление)
  cc.to_exalts(amount_divine)            # divine → exalted orb
  cc.chaos_per_divine                    # float — сколько chaos в 1 divine
  cc.divine_per_chaos                    # float
  cc.format_price(amount_divine)         # -> (amount, currency_str) для листинга
  cc.format_str(amount_divine)           # -> "1.5d" / "120c"
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

ROOT       = Path(__file__).resolve().parent.parent.parent
NINJA_PATH = ROOT / "poe_ninja_prices.json"

# Порог перехода divine→chaos при листинге (≤ этого значения → показываем в chaos)
CHAOS_DISPLAY_THRESHOLD_D = 1.0


@dataclass
class CurrencyConverter:
    # rates: имя_валюты_lowercase → divineValue
    _rates: dict[str, float] = field(default_factory=dict)

    # ── Загрузка ─────────────────────────────────────────────────────────────

    @classmethod
    def load(cls, ninja_path: Path = NINJA_PATH) -> "CurrencyConverter":
        """Загружает свежий снэпшот poe_ninja_prices.json."""
        rates: dict[str, float] = {"divine orb": 1.0, "divine": 1.0}
        try:
            data    = json.loads(ninja_path.read_text(encoding="utf-8-sig"))
            entries = data.get("entries") or []
            if entries:
                prices = entries[-1].get("prices") or {}
                for name, info in prices.items():
                    dv = info.get("divineValue")
                    if dv is not None:
                        rates[name.lower()] = float(dv)
        except Exception:
            pass  # работаем с дефолтами
        rates.setdefault("chaos orb", 1 / 7.0)  # fallback ~7c/d
        rates.setdefault("chaos",     rates["chaos orb"])
        rates.setdefault("exalted orb", rates.get("exalted orb", 1 / 427.0))
        rates.setdefault("exalted",     rates["exalted orb"])
        return cls(_rates=rates)

    # ── Базовые свойства ──────────────────────────────────────────────────────

    @property
    def chaos_per_divine(self) -> float:
        """Сколько chaos в 1 divine."""
        dv = self._rates.get("chaos orb") or self._rates.get("chaos") or 0
        return 1.0 / dv if dv > 0 else 7.0

    @property
    def divine_per_chaos(self) -> float:
        return 1.0 / self.chaos_per_divine

    @property
    def exalts_per_divine(self) -> float:
        dv = self._rates.get("exalted orb") or self._rates.get("exalted") or 0
        return 1.0 / dv if dv > 0 else 427.0

    # ── Конвертация ───────────────────────────────────────────────────────────

    def to_divine(self, amount: float, currency: str) -> float:
        """
        Конвертирует (amount, currency) → divine.
        currency: "divine", "chaos", "exalted", "exalted orb", или любое имя из poe_ninja.
        """
        c = currency.strip().lower()
        if c in ("divine", "divine orb", "d"):
            return float(amount)
        rate = (self._rates.get(c)
                or self._rates.get(c + " orb")
                or self._rates.get(c.removesuffix(" orb")))
        if rate is not None:
            return float(amount) * rate
        # fallback: неизвестная валюта → 0, не падаем
        return 0.0

    def to_chaos(self, amount_divine: float) -> float:
        return amount_divine * self.chaos_per_divine

    def to_chaos_int(self, amount_divine: float) -> int:
        return round(self.to_chaos(amount_divine))

    def to_exalts(self, amount_divine: float) -> float:
        return amount_divine * self.exalts_per_divine

    # ── Форматирование для листинга ───────────────────────────────────────────

    def format_price(self, amount_divine: float) -> tuple[float, str]:
        """
        Возвращает (amount, currency_str) в удобном для листинга виде.
        ≥ 1d → divine; < 1d → chaos (целое).
        """
        if amount_divine >= CHAOS_DISPLAY_THRESHOLD_D:
            return round(amount_divine, 1), "divine"
        chaos = max(1, self.to_chaos_int(amount_divine))
        return float(chaos), "chaos"

    def format_str(self, amount_divine: float) -> str:
        """Возвращает строку вида '1.5d' или '120c'."""
        amount, cur = self.format_price(amount_divine)
        if cur == "divine":
            return f"{amount:.1f}d"
        return f"{int(amount)}c"
