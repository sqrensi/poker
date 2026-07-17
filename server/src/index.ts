import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";
import { WebSocketServer } from "ws";
import { RoomManager } from "./net/rooms.js";
import type { ClientMsg } from "./net/rooms.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PUBLIC = path.join(__dirname, "..", "public");
const PORT = Number(process.env.PORT || 8787);
const PUBLIC_BASE = process.env.PUBLIC_BASE || `http://localhost:${PORT}`;

const mime: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
};

function sendFile(res: http.ServerResponse, filePath: string) {
  const ext = path.extname(filePath);
  res.writeHead(200, { "Content-Type": mime[ext] || "application/octet-stream" });
  fs.createReadStream(filePath).pipe(res);
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url || "/", PUBLIC_BASE);
  if (url.pathname === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ ok: true }));
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
  // SPA fallback for ?room=
  const index = path.join(PUBLIC, "index.html");
  if (fs.existsSync(index)) {
    sendFile(res, index);
    return;
  }
  res.writeHead(404);
  res.end("Not found");
});

const wss = new WebSocketServer({ server });
const rooms = new RoomManager();

wss.on("connection", (ws) => {
  const playerId = randomUUID();
  ws.send(JSON.stringify({ type: "welcome", playerId, publicBase: PUBLIC_BASE }));

  ws.on("message", (raw) => {
    let msg: ClientMsg;
    try {
      msg = JSON.parse(String(raw));
    } catch {
      ws.send(JSON.stringify({ error: "Неверный JSON" }));
      return;
    }
    try {
      const result = rooms.handle(ws, playerId, msg, PUBLIC_BASE) as Record<string, unknown> | undefined;
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
      ws.send(JSON.stringify({ type: "error", error: e instanceof Error ? e.message : "Ошибка сервера" }));
    }
  });

  ws.on("close", () => rooms.disconnect(ws));
});

server.listen(PORT, () => {
  console.log(`[poker] http ${PUBLIC_BASE}`);
  console.log(`[poker] ws   ws://localhost:${PORT}`);
  console.log(`[poker] health ${PUBLIC_BASE}/health`);
});

export { rooms, server };
