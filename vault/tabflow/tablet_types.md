# Типы Ritual Tablet в PoE2

Используются в `floor_fetcher.py` и Fragment Stash пикерах (GameHelper).

## 7 обычных типов

| UI-метка  | Полное название в API   | Индекс |
|-----------|------------------------|--------|
| Ritual    | Ritual Tablet          | 0      |
| Breach    | Breach Tablet          | 1      |
| Delirium  | Delirium Tablet        | 2      |
| Abyss     | Abyss Tablet           | 3      |
| Temple    | Temple Tablet          | 4      |
| Irradiated| Irradiated Tablet      | 5      |
| Overseer  | Overseer Tablet        | 6      |

## 8-й тип

| UI-метка  | Описание                        | Индекс |
|-----------|---------------------------------|--------|
| Unique    | Уникальные таблетки (TimeLost и др.) | 7 |

## Источники

- `scripts/tabflow/floor_fetcher.py` — `TABLET_TYPES` список
- `AppSettings.cs` — `FragmentTabletTypeSetting.Defaults()`
- Fragment Stash → Tablets sub-tab → 7 иконок типов + иконка Unique

## Рыночные данные

Флор-цены хранятся в `vault/tabflow/floor_prices.json`:
```json
{
  "fetched_at": "...",
  "floors": {
    "Ritual Tablet": {"p10_divine": 0.351, "sample_size": 10},
    ...
  }
}
```
