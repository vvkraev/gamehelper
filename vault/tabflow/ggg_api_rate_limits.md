# GGG Trade API — Rate Limits

Данные получены из реальных заголовков ответа API (2026-07-17).

## Заголовки

```
X-Rate-Limit-Policy: trade-search-request-limit
X-Rate-Limit-Rules: Ip
X-Rate-Limit-Ip: 5:10:60,15:60:300,30:300:1800
```

## Расшифровка формата `hits:period_sec:ban_sec`

| Лимит | Период | Бан при превышении |
|-------|--------|--------------------|
| 5 запросов | 10 сек | 60 сек (1 мин) |
| 15 запросов | 60 сек | 300 сек (5 мин) |
| 30 запросов | 300 сек (5 мин) | 1800 сек (30 мин) |

## Применение в floor_fetcher.py

`floor_fetcher.py` делает 14 запросов (7 типов × 2: search + fetch).

Безопасный темп: **1 запрос каждые 2.5 секунды**.  
В любом 10-секундном окне — не более 4 запросов (лимит 5).  
Общее время прогона: ~50 секунд.

Реализация: `DELAY_BETWEEN = 2.5` применяется и между search→fetch внутри типа, и между типами.

## Запрос к API (правильная форма)

POST `/api/trade2/search/{league}`

```json
{
  "query": {
    "status": {"option": "securable"},
    "type": "Ritual Tablet",
    "stats": [{
      "type": "and",
      "filters": [{"id": "pseudo.pseudo_number_of_uses_remaining", "disabled": false, "value": {"min": 10}}],
      "disabled": false
    }],
    "filters": {
      "misc_filters": {
        "filters": {"corrupted": {"option": "false"}},
        "disabled": false
      }
    }
  },
  "sort": {"price": "asc"}
}
```

`status: securable` = только instabuy (покупки через Ange).  
`Uses Remaining ≥ 10` = только полные таблички (игроки торгуют только 10/10).  
`corrupted: false` = без коррапт.

## Где GGG публикует лимиты?

Нигде — официальной документации нет. Источник: заголовок `X-Rate-Limit-Ip` в ответе.
