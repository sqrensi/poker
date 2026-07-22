# Деплой Poker Online на Ubuntu VPS

Инструкция для развёртывания сервера Hold'em Club на чистом VPS (Ubuntu 22.04 / 24.04).

## Что понадобится

- VPS с Ubuntu, минимум **1 GB RAM**
- Домен (опционально, но рекомендуется для HTTPS)
- SSH-доступ к серверу

Сервер слушает порт **8787** (HTTP + WebSocket). Для продакшена ставим **Nginx** как reverse proxy с TLS.
NAUv56tLTm
---

## 1. Подключение к VPS

```bash
ssh root@ВАШ_IP
```

Создайте пользователя (если ещё нет):

```bash
adduser poker
usermod -aG sudo poker
su - poker
```

---

## 2. Установка Node.js 20

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt-get install -y nodejs git
node -v
npm -v
```

---

## 3. Загрузка проекта

### Вариант A — через git

```bash
cd ~
git clone https://github.com/ВАШ_РЕПО/poker.git
cd poker/server
npm install
```

### Вариант B — через scp с локальной машины

На **локальном** компьютере (из папки проекта):

```bash
scp -r server poker@ВАШ_IP:~/poker-server
```

На VPS:

```bash
cd ~/poker-server
npm install
```

---

## 4. Переменные окружения

```bash
nano ~/poker-server/.env
```

Содержимое:

```env
PORT=8787
PUBLIC_BASE=https://poker.ваш-домен.ru
```

`PUBLIC_BASE` — публичный URL, который видят клиенты (для invite-ссылок).

---

## 5. Проверка локально на VPS

```bash
cd ~/poker-server
npm start
```

В другом терминале:

```bash
curl http://127.0.0.1:8787/health
```

Должен вернуться JSON с `"ok": true`.

Остановите: `Ctrl+C`.

---

## 6. systemd — автозапуск

```bash
sudo nano /etc/systemd/system/poker.service
```

```ini
[Unit]
Description=Poker Hold'em Online Server
After=network.target

[Service]
Type=simple
User=poker
WorkingDirectory=/home/poker/poker-server
EnvironmentFile=/home/poker/poker-server/.env
ExecStart=/usr/bin/npm start
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

> Путь `WorkingDirectory` поправьте под вашу папку (`~/poker/server` или `~/poker-server`).

```bash
sudo systemctl daemon-reload
sudo systemctl enable poker
sudo systemctl start poker
sudo systemctl status poker
```

Логи:

```bash
journalctl -u poker -f
```

---

## 7. Nginx + HTTPS (Let's Encrypt)

```bash
sudo apt install -y nginx certbot python3-certbot-nginx
sudo nano /etc/nginx/sites-available/poker
```

```nginx
server {
    listen 80;
    server_name poker.ваш-домен.ru;

    location / {
        proxy_pass http://127.0.0.1:8787;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/poker /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
sudo certbot --nginx -d poker.ваш-домен.ru
```

Обновите `.env`:

```env
PUBLIC_BASE=https://poker.ваш-домен.ru
```

```bash
sudo systemctl restart poker
```

---

## 8. Firewall

```bash
sudo ufw allow OpenSSH
sudo ufw allow 'Nginx Full'
sudo ufw enable
sudo ufw status
```

Порт 8787 наружу открывать **не нужно** — трафик идёт через Nginx (443).

---

## 9. Данные и бэкап

Профили и монеты хранятся в файле:

```
/home/poker/poker-server/data/profiles.json
```

Резервная копия:

```bash
cp ~/poker-server/data/profiles.json ~/profiles-backup-$(date +%F).json
```

---

## 10. Unity-клиент

В Unity укажите адрес VPS вместо LAN:

- PlayerPrefs ключ `poker_lan_host` → `https://poker.ваш-домен.ru`
- Кнопка «Играть онлайн» открывает браузер с `?pid=...&queue=1` — сразу начинается поиск матча

---

## 11. Как работает онлайн-матч

| Параметр | Значение |
|----------|----------|
| Игроков в матче | **4** |
| Стартовый баланс | **50 000** монет |
| Взнос в очередь | **1 000** монет (возврат при отмене поиска) |
| Выплата победителю | **4 000** монет |
| Стек за столом | 1 000 фишек |

---

## 12. Полезные команды

```bash
sudo systemctl status poker
cd ~/poker-server && git pull && npm install && sudo systemctl restart poker
curl https://poker.ваш-домен.ru/api/online
curl https://poker.ваш-домен.ru/health
```
