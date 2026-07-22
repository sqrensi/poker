# Local Texas Hold'em (Unity 3D) + Online

## Локально (Unity)

1. Откройте проект в Unity
2. Play → **меню**
3. **Играть с ботами** — локальный стол
4. **Онлайн матч** — очередь и игра на сервере (адрес в `PokerNetworkConfig`)

## Онлайн (сервер)

```bash
cd server
npm install
npm test
npm start
```

Откройте http://localhost:8787 → создайте стол → отправьте ссылку другу.

Подробнее: [server/README.md](server/README.md)

## Что делает сервер

- Авторитетная логика Hold'em (колода, ставки, шоудаун)
- Комнаты по коду / ссылке `/?room=XXXXXX`
- WebSocket API + статический веб-клиент
