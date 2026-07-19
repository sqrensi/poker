import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";
import { WebSocketServer } from "ws";
import { RoomManager } from "./net/rooms.js";
import type { ClientMsg } from "./net/rooms.js";
import { profiles } from "./profile/store.js";
import { QUEUE_MAX, QUEUE_MIN } from "./net/queue.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PUBLIC = path.join(__dirname, "..", "public");
const PORT = Number(process.env.PORT || 8787);
const PUBLIC_BASE = process.env.PUBLIC_BASE || `http://localhost:${PORT}`;

/** IPv4 адреса этой машины в локальной сети. */
function lanAddresses(): { name: string; address: string }[] {
  const out: { name: string; address: string }[] = [];
  const nets = os.networkInterfaces();
  for (const name of Object.keys(nets)) {
    for (const net of nets[name] || []) {
      const family = String(net.family);
      if ((family === "IPv4" || family === "4") && !net.internal) {
        out.push({ name, address: net.address });
      }
    }
  }
  return out;
}

function lanInfo() {
  const addresses = lanAddresses();
  return {
    port: PORT,
    addresses,
    urls: addresses.map((a) => `http://${a.address}:${PORT}`),
    hint: "Друзья в той же Wi‑Fi открывают один из urls в браузере",
  };
}

/** База для invite: origin клиента, иначе первый LAN URL, иначе PUBLIC_BASE. */
function inviteBaseFrom(msg: ClientMsg, lan: ReturnType<typeof lanInfo>): string {
  const fromClient = (msg as { publicBase?: string }).publicBase;
  if (typeof fromClient === "string" && fromClient.startsWith("http")) {
    return fromClient.replace(/\/$/, "");
  }
  if (lan.urls[0]) return lan.urls[0];
  return PUBLIC_BASE;
}

const mime: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
  ".woff2": "font/woff2",
};

function sendJson(res: http.ServerResponse, status: number, body: unknown) {
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Access-Control-Allow-Origin": "*",
  });
  res.end(JSON.stringify(body));
}

function sendFile(res: http.ServerResponse, filePath: string) {
  const ext = path.extname(filePath);
  res.writeHead(200, { "Content-Type": mime[ext] || "application/octet-stream" });
  fs.createReadStream(filePath).pipe(res);
}

const rooms = new RoomManager();

const server = http.createServer((req, res) => {
  const url = new URL(req.url || "/", PUBLIC_BASE);

  if (req.method === "OPTIONS") {
    res.writeHead(204, {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type",
    });
    res.end();
    return;
  }

  if (url.pathname === "/health") {
    sendJson(res, 200, { ok: true, online: rooms.onlineCount(), queue: rooms.queue.size, lan: lanInfo() });
    return;
  }

  if (url.pathname === "/api/lan") {
    sendJson(res, 200, lanInfo());
    return;
  }

  if (url.pathname === "/api/rooms") {
    sendJson(res, 200, { rooms: rooms.listOpenRooms() });
    return;
  }

  if (url.pathname === "/api/online") {
    sendJson(res, 200, {
      online: rooms.onlineCount(),
      queue: rooms.queue.snapshot(),
      minPlayers: QUEUE_MIN,
      maxPlayers: QUEUE_MAX,
      lan: lanInfo(),
    });
    return;
  }

  if (url.pathname === "/api/leaderboard") {
    sendJson(res, 200, { entries: profiles.leaderboard(30) });
    return;
  }

  let rel = url.pathname === "/" ? "/index.html" : url.pathname;
  const filePath = path.normalize(path.join(PUBLIC, rel));
  if (!filePath.startsWith(PUBLIC)) {
    res.writeHead(403);
    res.end("Forbidden");
    return;
  }
  if (fs.existsSync(filePath) && fs.statSync(filePath).isFile()) {
    sendFile(res, filePath);
    return;
  }
  const index = path.join(PUBLIC, "index.html");
  if (fs.existsSync(index)) {
    sendFile(res, index);
    return;
  }
  res.writeHead(404);
  res.end("Not found");
});

const wss = new WebSocketServer({ server });

wss.on("connection", (ws) => {
  const sessionId = randomUUID();
  const lan = lanInfo();
  ws.send(
    JSON.stringify({
      type: "welcome",
      sessionId,
      publicBase: PUBLIC_BASE,
      lan,
      minPlayers: QUEUE_MIN,
      maxPlayers: QUEUE_MAX,
    })
  );

  ws.on("message", (raw) => {
    let msg: ClientMsg;
    try {
      msg = JSON.parse(String(raw));
    } catch {
      ws.send(JSON.stringify({ type: "error", error: "Неверный JSON" }));
      return;
    }

    let playerId = rooms.getPlayerId(ws);
    if (msg.type !== "auth" && !playerId) {
      ws.send(JSON.stringify({ type: "error", error: "Сначала отправьте auth" }));
      return;
    }
    if (msg.type === "auth") {
      playerId = (msg.playerId || `player-${sessionId}`).trim();
    }

    try {
      const inviteBase = inviteBaseFrom(msg, lan);
      const result = rooms.handle(ws, playerId!, msg, inviteBase) as Record<string, unknown> | undefined;
      if (result && "error" in result && result.error) {
        ws.send(JSON.stringify({ type: "error", error: result.error }));
      } else if (result && "type" in result && result.type !== "ok") {
        const { _broadcast, _room, ...payload } = result as {
          _broadcast?: boolean;
          _room?: { broadcast: () => void };
          type: string;
        };
        ws.send(JSON.stringify(payload));
        if (_broadcast && _room) _room.broadcast();
      }
    } catch (e) {
      ws.send(
        JSON.stringify({ type: "error", error: e instanceof Error ? e.message : "Ошибка сервера" })
      );
    }
  });

  ws.on("close", () => rooms.disconnect(ws));
});

setInterval(() => rooms.tickQueue(), 1000);
setInterval(() => rooms.tickBots(), 650);

// 0.0.0.0 — телефоны в той же Wi‑Fi могут подключиться
server.listen(PORT, "0.0.0.0", () => {
  const lan = lanInfo();
  console.log(`[poker] local  http://localhost:${PORT}`);
  for (const u of lan.urls) console.log(`[poker] LAN    ${u}`);
  if (!lan.urls.length) console.log(`[poker] LAN    (нет внешнего IPv4 — проверьте Wi‑Fi)`);
  console.log(`[poker] queue  ${QUEUE_MIN}–${QUEUE_MAX}`);
  console.log(`[poker] lan    http://localhost:${PORT}/api/lan`);
});

export { rooms, server };
