import { describe, it, expect, beforeEach, afterEach } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  ProfileStore,
  DEFAULT_RATING,
  DEFAULT_COINS,
  ONLINE_BUY_IN,
} from "../src/profile/store.js";
import { MatchQueue, QUEUE_MAX } from "../src/net/queue.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const dataPath = path.join(__dirname, "..", "data", "profiles.json");

describe("ProfileStore", () => {
  beforeEach(() => {
    if (fs.existsSync(dataPath)) fs.unlinkSync(dataPath);
  });
  afterEach(() => {
    if (fs.existsSync(dataPath)) fs.unlinkSync(dataPath);
  });

  it("creates profile with default rating and coins", () => {
    const store = new ProfileStore();
    const p = store.ensure("player-abc", "Тестер");
    expect(p.nickname).toBe("Тестер");
    expect(p.rating).toBe(DEFAULT_RATING);
    expect(p.coins).toBe(DEFAULT_COINS);
  });

  it("charges and refunds buy-in", () => {
    const store = new ProfileStore();
    store.ensure("a", "Alice");
    const charge = store.chargeBuyIn("a", ONLINE_BUY_IN);
    expect(charge.ok).toBe(true);
    if (charge.ok) expect(charge.coins).toBe(DEFAULT_COINS - ONLINE_BUY_IN);
    const p = store.refundBuyIn("a", ONLINE_BUY_IN);
    expect(p.coins).toBe(DEFAULT_COINS);
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

  it("pays winner online pool", () => {
    const store = new ProfileStore();
    store.ensure("w", "Winner");
    for (let i = 0; i < 4; i++) store.chargeBuyIn("w", ONLINE_BUY_IN);
    const before = store.get("w")!.coins;
    store.payoutOnlineWinner("w", 4 * ONLINE_BUY_IN);
    expect(store.get("w")!.coins).toBe(before + 4 * ONLINE_BUY_IN);
  });
});

describe("MatchQueue", () => {
  it("matches exactly 4 players", () => {
    const q = new MatchQueue();
    const fakeWs = { readyState: 1 } as any;
    for (let i = 0; i < QUEUE_MAX; i++) {
      q.enqueue({
        ticketId: `t${i}`,
        playerId: `p${i}`,
        nickname: `N${i}`,
        rating: 1000,
        ws: fakeWs,
        buyInPaid: true,
      });
    }
    const tables = q.tryMatch();
    expect(tables.length).toBe(1);
    expect(tables[0].players.length).toBe(QUEUE_MAX);
    expect(q.size).toBe(0);
  });

  it("does not match with fewer than 4", () => {
    const q = new MatchQueue();
    const fakeWs = { readyState: 1 } as any;
    for (let i = 0; i < 3; i++) {
      q.enqueue({
        ticketId: `t${i}`,
        playerId: `p${i}`,
        nickname: `N${i}`,
        rating: 1000,
        ws: fakeWs,
        buyInPaid: true,
      });
    }
    expect(q.tryMatch().length).toBe(0);
    expect(q.size).toBe(3);
  });
});
