import { describe, it, expect, beforeAll, afterAll } from "vitest";
import WebSocket from "ws";
import { spawn, ChildProcess } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PORT = 8799;
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

describe("integration ws", () => {
  let child: ChildProcess;

  beforeAll(async () => {
    child = spawn("npx", ["tsx", "src/index.ts"], {
      cwd: path.join(__dirname, ".."),
      env: { ...process.env, PORT: String(PORT), PUBLIC_BASE: BASE },
      shell: true,
      stdio: "pipe",
    });
    // wait for listen
    await new Promise<void>((resolve, reject) => {
      const t = setTimeout(() => reject(new Error("server start timeout")), 15000);
      child.stdout?.on("data", (b) => {
        if (String(b).includes("http")) {
          clearTimeout(t);
          resolve();
        }
      });
      child.stderr?.on("data", () => {});
    });
  }, 20000);

  afterAll(async () => {
    child?.kill();
  });

  it("create join start hides opponent cards", async () => {
    const a = openWs(`ws://127.0.0.1:${PORT}`);
    await a.ready;
    expect((await a.next()).type).toBe("welcome");
    a.ws.send(JSON.stringify({ type: "create", name: "Host" }));
    const created = await a.next();
    expect(created.type).toBe("created");
    const code = created.code as string;
    expect((await a.next()).type).toBe("state");

    const b = openWs(`ws://127.0.0.1:${PORT}`);
    await b.ready;
    expect((await b.next()).type).toBe("welcome");
    b.ws.send(JSON.stringify({ type: "join", room: code, name: "Guest" }));
    expect((await b.next()).type).toBe("joined");
    expect((await b.next()).type).toBe("state");
    expect((await a.next()).type).toBe("state");

    a.ws.send(JSON.stringify({ type: "start" }));
    const playing = (await a.next()) as any;
    expect(playing.started).toBe(true);
    expect(playing.table.street).toBe("preflop");
    const host = playing.table.players.find((p: any) => p.name === "Host");
    const guest = playing.table.players.find((p: any) => p.name === "Guest");
    expect(host.hole.every((c: string) => c !== "??")).toBe(true);
    expect(guest.hole.every((c: string) => c === "??")).toBe(true);

    const guestView = (await b.next()) as any;
    const gSelf = guestView.table.players.find((p: any) => p.name === "Guest");
    const gHost = guestView.table.players.find((p: any) => p.name === "Host");
    expect(gSelf.hole.every((c: string) => c !== "??")).toBe(true);
    expect(gHost.hole.every((c: string) => c === "??")).toBe(true);

    a.ws.close();
    b.ws.close();
  }, 20000);
});
