import { describe, it, expect, beforeAll, afterAll } from "vitest";
import WebSocket from "ws";
import { spawn, ChildProcess } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PORT = 8800 + Math.floor(Math.random() * 200);
const BASE = `http://127.0.0.1:${PORT}`;

function openWs(url: string) {
  const ws = new WebSocket(url);
  const q: unknown[] = [];
  let wake: (() => void) | null = null;
  ws.on("message", (d) => {
    q.push(JSON.parse(String(d)));
    wake?.();
  });
  const next = async () => {
    while (!q.length) await new Promise<void>((r) => (wake = r));
    return q.shift() as Record<string, unknown>;
  };
  const ready = new Promise<void>((res, rej) => {
    ws.on("open", () => res());
    ws.on("error", rej);
  });
  return { ws, next, ready };
}

async function auth(client: ReturnType<typeof openWs>, playerId: string, nickname: string) {
  expect((await client.next()).type).toBe("welcome");
  client.ws.send(JSON.stringify({ type: "auth", playerId, nickname }));
  const profile = await client.next();
  expect(profile.type).toBe("profile");
  return profile;
}

async function waitForServer(port: number, ms = 20000) {
  const start = Date.now();
  while (Date.now() - start < ms) {
    try {
      const r = await fetch(`http://127.0.0.1:${port}/api/online`);
      if (r.ok) return;
    } catch {
      /* retry */
    }
    await new Promise((r) => setTimeout(r, 200));
  }
  throw new Error("server start timeout");
}

describe("integration ws", () => {
  let child: ChildProcess;

  beforeAll(async () => {
    child = spawn("npx", ["tsx", "src/index.ts"], {
      cwd: path.join(__dirname, ".."),
      env: { ...process.env, PORT: String(PORT), PUBLIC_BASE: BASE },
      shell: true,
      stdio: "pipe",
    });
    await waitForServer(PORT);
  }, 25000);

  afterAll(async () => {
    child?.kill();
  });

  it("create join start hides opponent cards", async () => {
    const a = openWs(`ws://127.0.0.1:${PORT}`);
    await a.ready;
    await auth(a, "player-host-int-01", "HostPlayer");

    a.ws.send(JSON.stringify({ type: "create", name: "HostPlayer" }));
    const created = await a.next();
    expect(created.type).toBe("created");
    const code = created.code as string;
    expect((await a.next()).type).toBe("state");

    const b = openWs(`ws://127.0.0.1:${PORT}`);
    await b.ready;
    await auth(b, "player-guest-int-01", "GuestPlayer");
    b.ws.send(JSON.stringify({ type: "join", room: code, name: "GuestPlayer" }));
    expect((await b.next()).type).toBe("joined");
    expect((await b.next()).type).toBe("state");
    expect((await a.next()).type).toBe("state");

    a.ws.send(JSON.stringify({ type: "start" }));
    const playing = (await a.next()) as any;
    expect(playing.started).toBe(true);
    expect(playing.table.street).toBe("preflop");
    const host = playing.table.players.find((p: any) => p.name === "HostPlayer");
    const guest = playing.table.players.find((p: any) => p.name === "GuestPlayer");
    expect(host.hole.every((c: string) => c !== "??")).toBe(true);
    expect(guest.hole.every((c: string) => c === "??")).toBe(true);

    const guestView = (await b.next()) as any;
    const gSelf = guestView.table.players.find((p: any) => p.name === "GuestPlayer");
    const gHost = guestView.table.players.find((p: any) => p.name === "HostPlayer");
    expect(gSelf.hole.every((c: string) => c !== "??")).toBe(true);
    expect(gHost.hole.every((c: string) => c === "??")).toBe(true);

    a.ws.close();
    b.ws.close();
  }, 20000);

  it("host can add bot and start alone", async () => {
    const a = openWs(`ws://127.0.0.1:${PORT}`);
    await a.ready;
    await auth(a, "player-host-bot-01", "SoloHost");

    a.ws.send(JSON.stringify({ type: "create", name: "SoloHost" }));
    expect((await a.next()).type).toBe("created");
    expect((await a.next()).type).toBe("state");

    a.ws.send(JSON.stringify({ type: "list_rooms" }));
    const rooms = await a.next();
    expect(rooms.type).toBe("rooms");
    expect((rooms.rooms as any[]).length).toBeGreaterThan(0);

    a.ws.send(JSON.stringify({ type: "add_bot" }));
    let lobby: any = null;
    for (let i = 0; i < 4; i++) {
      const m = (await a.next()) as any;
      if (m.type === "state") lobby = m;
      if (m.type === "bots_added" || (lobby && lobby.lobby?.some((p: any) => p.isBot))) break;
    }
    expect(lobby?.lobby?.some((p: any) => p.isBot)).toBe(true);

    a.ws.send(JSON.stringify({ type: "start" }));
    let playing: any = null;
    for (let i = 0; i < 4; i++) {
      const m = (await a.next()) as any;
      if (m.type === "state" && m.started) {
        playing = m;
        break;
      }
    }
    expect(playing?.started).toBe(true);
    expect(playing.table.players.length).toBe(2);

    a.ws.close();
  }, 20000);
});
