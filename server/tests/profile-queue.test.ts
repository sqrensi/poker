import { describe, it, expect, beforeEach, afterEach } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { ProfileStore, DEFAULT_RATING } from "../src/profile/store.js";
import { MatchQueue, QUEUE_FILL_MS, QUEUE_MAX } from "../src/net/queue.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const dataPath = path.join(__dirname, "..", "data", "profiles.json");

describe("ProfileStore", () => {
  beforeEach(() => {
    if (fs.existsSync(dataPath)) fs.unlinkSync(dataPath);
  });
  afterEach(() => {
    if (fs.existsSync(dataPath)) fs.unlinkSync(dataPath);
  });

  it("creates profile with default rating", () => {
    const store = new ProfileStore();
    const p = store.ensure("player-abc", "Тестер");
    expect(p.nickname).toBe("Тестер");
    expect(p.rating).toBe(DEFAULT_RATING);
  });

  it("updates rating after match", () => {
    const store = new ProfileStore();
    store.ensure("a", "Alice");
    store.ensure("b", "Bob");
    store.applyMatchResult(["a", "b"], "a");
    expect(store.get("a")!.rating).toBeGreaterThan(DEFAULT_RATING);
    expect(store.get("b")!.rating).toBeLessThan(DEFAULT_RATING);
    expect(store.get("a")!.wins).toBe(1);
  });
});

describe("MatchQueue", () => {
  it("matches at max table size", () => {
    const q = new MatchQueue();
    const fakeWs = { readyState: 1 } as any;
    for (let i = 0; i < QUEUE_MAX; i++) {
      q.enqueue({
        ticketId: `t${i}`,
        playerId: `p${i}`,
        nickname: `N${i}`,
        rating: 1000,
        ws: fakeWs,
      });
    }
    const tables = q.tryMatch();
    expect(tables.length).toBe(1);
    expect(tables[0].players.length).toBe(QUEUE_MAX);
    expect(q.size).toBe(0);
  });

  it("matches after fill timeout with min players", () => {
    const q = new MatchQueue();
    const fakeWs = { readyState: 1 } as any;
    q.enqueue({ ticketId: "t0", playerId: "p0", nickname: "A", rating: 1000, ws: fakeWs });
    q.enqueue({ ticketId: "t1", playerId: "p1", nickname: "B", rating: 1000, ws: fakeWs });
    expect(q.tryMatch(Date.now()).length).toBe(0);
    const tables = q.tryMatch(Date.now() + QUEUE_FILL_MS + 1);
    expect(tables.length).toBe(1);
    expect(tables[0].players.length).toBe(2);
  });
});
