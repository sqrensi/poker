# Poker Online — Hold'em Club

Браузерный Texas Hold'em: профиль, рейтинг, очередь подбора и приватные комнаты.

## Возможности

- **ID + ник** — стабильный `playerId` в `localStorage` (из Unity передаётся `?pid=`)
- **Рейтинг (MMR)** — стартовый 1000, Elo-подобное обновление после конца матча
- **Очередь** — «Быстрая игра», стол **2–10** игроков (добор ~12 сек, при 10 — сразу)
- **Приватная комната** — ссылка `/?room=XXXXXX`, хост стартует вручную
- Без FPS-воркеров: только JSON WebSocket + лёгкий HTTP API

## Запуск

```bash
cd server
npm install
npm start
```

Открой http://localhost:8787

API: `/health`, `/api/online`, `/api/leaderboard`

Данные профилей: `server/data/profiles.json`

## Unity

Меню «Онлайн» открывает браузер с вашим ID. Боты — локально в Unity.
