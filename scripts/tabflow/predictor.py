"""
predictor.py — единая логика inference для TabFlow.

Используется evaluate_clipboard.py (текстовые моды из буфера)
и predict.py (rich-моды из снэпшотов).
"""
from __future__ import annotations

import json
import math
import re
from dataclasses import dataclass, field
from pathlib import Path

from mod_utils import mod_template as _mod_template

_ROLL_PAT   = re.compile(r'(\d+(?:\.\d+)?)\((\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)\)')
_RANGE_PAT  = re.compile(r'\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)')
_NUMBER_PAT = re.compile(r'\d+(?:\.\d+)?')
_EFFECT_PAT = re.compile(r'(\d+)%\s+increased Effect of Modifiers on the Tablet', re.I)


@dataclass
class PredictionResult:
    """Результат inference из текстовых модов буфера обмена."""
    price_raw: float                       # оценка до effect mult
    price: float                           # итоговая (с effect mult, без miss override)
    matched: list[tuple[str, float]]       # (mod_text, roll_norm)
    matched_templates: list[str]           # нормализованные шаблоны для miss-override
    unmatched: list[str]                   # нераспознанные (без effect_mods)
    effect_mult: float = 1.0
    effect_mods: list[str] = field(default_factory=list)
    mae: float | None = None
    n_train: int | None = None


class TabletPredictor:
    """LightGBM-модель для одного типа планшетки."""

    def __init__(self, model_dir: Path) -> None:
        self.model_dir     = model_dir
        self._loaded       = False
        self.model         = None
        self.vocab:          dict[str, int] = {}
        self.meta:           dict           = {}
        self.text_to_hash:   dict[str, str] = {}

    @property
    def mae(self) -> float | None:
        return self.meta.get('mae_divine')

    @property
    def n_train(self) -> int | None:
        return self.meta.get('n_train')

    # ── Загрузка ─────────────────────────────────────────────────────────────

    def load(self) -> bool:
        """Загружает артефакты модели. Возвращает False если файлы отсутствуют.
        Поднимает ImportError если lightgbm не установлен.
        Поддерживает версионированные папки: если есть _current.txt — загружает из неё."""
        import lightgbm as lgb

        # Версионированный формат: models/{slug}/_current.txt → имя подпапки
        current_file = self.model_dir / "_current.txt"
        resolved_dir = (
            self.model_dir / current_file.read_text(encoding='utf-8').strip()
            if current_file.exists()
            else self.model_dir
        )

        model_file = resolved_dir / "model.txt"
        vocab_file = resolved_dir / "vocab.json"
        if not model_file.exists() or not vocab_file.exists():
            return False

        self.model        = lgb.Booster(model_file=str(model_file))
        self.vocab        = json.loads(vocab_file.read_text(encoding='utf-8'))
        meta_file         = resolved_dir / "metadata.json"
        self.meta         = json.loads(meta_file.read_text(encoding='utf-8')) if meta_file.exists() else {}
        t2h_file          = resolved_dir / "text_to_hash.json"
        self.text_to_hash = json.loads(t2h_file.read_text(encoding='utf-8')) if t2h_file.exists() else {}
        self._loaded      = True
        return True

    # ── Roll normalization ────────────────────────────────────────────────────

    @staticmethod
    def normalize_roll(val: float, rmin: float, rmax: float) -> float:
        if rmax > rmin:
            return (val - rmin) / (rmax - rmin)
        if rmax > 0:
            return val / rmax
        return 1.0

    # ── Inference из текстовых модов (буфер / itemText) ──────────────────────

    def predict_from_text_mods(self, mods: list[str]) -> PredictionResult | None:
        """Возвращает None если ни один мод не распознан."""
        if not self._loaded:
            raise RuntimeError("load() не был вызван")
        import numpy as np

        vec      = np.zeros(len(self.vocab), dtype=np.float32)
        matched:   list[tuple[str, float]] = []
        templates: list[str]               = []
        unmatched: list[str]               = []

        for mod_text in mods:
            roll_norm  = self._roll_norm_from_text(mod_text)
            normalized = self._normalize_text(mod_text)
            found      = False

            # Приоритет 1: explicit.stat_ хэш через text_to_hash
            stat_hash = self.text_to_hash.get(normalized)
            if stat_hash is None:
                an_norm   = re.sub(r'\ban\b', '#', normalized).strip()
                stat_hash = self.text_to_hash.get(an_norm)
            if stat_hash and stat_hash in self.vocab:
                vec[self.vocab[stat_hash]] = roll_norm
                found = True

            # Приоритет 2: text: ключ (text-fallback снапшоты)
            if not found:
                key = f"text:{normalized}"
                if key in self.vocab:
                    vec[self.vocab[key]] = roll_norm
                    found = True

            if found:
                matched.append((mod_text, roll_norm))
                templates.append(_mod_template(mod_text))
            else:
                unmatched.append(mod_text)

        if not matched:
            return None

        price_raw = math.expm1(float(self.model.predict(vec.reshape(1, -1))[0]))

        effect_mult  = 1.0
        effect_mods: list[str] = []
        remaining:   list[str] = []
        for m in unmatched:
            hit = _EFFECT_PAT.search(m)
            if hit:
                effect_mult *= (1.0 + int(hit.group(1)) / 100.0)
                effect_mods.append(m)
            else:
                remaining.append(m)

        mae = self.mae
        if mae and effect_mult != 1.0:
            mae = mae * effect_mult

        return PredictionResult(
            price_raw=price_raw,
            price=price_raw * effect_mult,
            matched=matched,
            matched_templates=templates,
            unmatched=remaining,
            effect_mult=effect_mult,
            effect_mods=effect_mods,
            mae=mae,
            n_train=self.n_train,
        )

    # ── Inference из rich-модов (снапшоты) ───────────────────────────────────

    def predict_batch_rich(self, items_rich_mods: list[list[dict]]) -> list[float]:
        """Batch inference; одна matmul для целого типа."""
        if not self._loaded:
            raise RuntimeError("load() не был вызван")
        import numpy as np

        n = len(self.vocab)
        X = np.zeros((len(items_rich_mods), n), dtype=np.float32)
        for i, rich_mods in enumerate(items_rich_mods):
            self._fill_rich_vec(rich_mods, X[i])
        y_log = self.model.predict(X)
        return [math.expm1(float(y)) for y in y_log]

    # ── Приватные утилиты ─────────────────────────────────────────────────────

    def _roll_norm_from_text(self, mod_text: str) -> float:
        m = _ROLL_PAT.search(mod_text)
        if not m:
            return 1.0
        val  = float(m.group(1))
        a    = float(m.group(2))
        b    = float(m.group(3))
        return self.normalize_roll(val, min(a, b), max(a, b))

    @staticmethod
    def _normalize_text(mod_text: str) -> str:
        clean = _RANGE_PAT.sub('', mod_text).strip()
        return _NUMBER_PAT.sub('#', clean).strip()

    def _fill_rich_vec(self, rich_mods: list[dict], vec) -> None:
        for mod in rich_mods:
            h = mod.get("hash")
            if not h or h not in self.vocab:
                continue
            value = mod.get("value")
            rmin  = float(mod.get("roll_min") or 0)
            rmax  = float(mod.get("roll_max") or 0)
            if value is None:
                vec[self.vocab[h]] = 1.0
            elif rmax:
                vec[self.vocab[h]] = self.normalize_roll(float(value), rmin, rmax)
            else:
                vec[self.vocab[h]] = 1.0
