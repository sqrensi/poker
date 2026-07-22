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
import { MatchQueue, QUEUE_MAX, QUEUE_MIN, QUEUE_FILL_MS } from "../src/net/queue.js";
import { Room } from "../src/net/rooms.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const dataPath = path.join(__dirname, "..", "data", "profiles.json");

const fakeWs = { readyState: 1 } as any;

function enqueuePlayer(q: MatchQueue, i: number, at: number, fillBots = false) {
  q.enqueue({
    ticketId: `t${i}`,
    playerId: `p${i}`,
    nickname: `N${i}`,
    rating: 1000,
    ws: fakeWs,
    buyInPaid: true,
    fillBots,
  });
  const entry = q.getTicket(`p${i}`);
  if (entry) entry.enqueuedAt = at;
}

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

  it("applyLocalBalanceHint only decreases server balance", () => {
    const store = new ProfileStore();
    store.ensure("a", "Alice");
    store.chargeBuyIn("a", ONLINE_BUY_IN);
    expect(store.get("a")!.coins).toBe(DEFAULT_COINS - ONLINE_BUY_IN);
    store.applyLocalBalanceHint("a", DEFAULT_COINS);
    expect(store.get("a")!.coins).toBe(DEFAULT_COINS - ONLINE_BUY_IN);
    store.applyLocalBalanceHint("a", DEFAULT_COINS - ONLINE_BUY_IN * 2);
    expect(store.get("a")!.coins).toBe(DEFAULT_COINS - ONLINE_BUY_IN * 2);
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

  it("cashes out chip stack to wallet", () => {
    const store = new ProfileStore();
    store.ensure("w", "Winner");
    store.chargeBuyIn("w", ONLINE_BUY_IN);
    const before = store.get("w")!.coins;
    store.cashOutChips("w", 7500);
    expect(store.get("w")!.coins).toBe(before + 7500);
    store.cashOutChips("w", 0);
    expect(store.get("w")!.coins).toBe(before + 7500);
  });
});

describe("MatchQueue", () => {
  it("matches exactly 4 players immediately", () => {
    const q = new MatchQueue();
    const now = Date.now();
    for (let i = 0; i < QUEUE_MAX; i++) enqueuePlayer(q, i, now);
    const tables = q.tryMatch(now);
    expect(tables.length).toBe(1);
    expect(tables[0].players.length).toBe(QUEUE_MAX);
    expect(q.size).toBe(0);
  });

  it("does not match 2 players before timeout", () => {
    const q = new MatchQueue();
    const now = 1_000_000;
    enqueuePlayer(q, 0, now);
    enqueuePlayer(q, 1, now);
    expect(q.tryMatch(now + QUEUE_FILL_MS - 1).length).toBe(0);
    expect(q.size).toBe(2);
  });

  it("matches 2 players after 5 second timeout", () => {
    const q = new MatchQueue();
    const now = 1_000_000;
    enqueuePlayer(q, 0, now);
    enqueuePlayer(q, 1, now);
    const tables = q.tryMatch(now + QUEUE_FILL_MS);
    expect(tables.length).toBe(1);
    expect(tables[0].players.length).toBe(2);
    expect(q.size).toBe(0);
  });

  it("matches 3 players after third waited 5 seconds", () => {
    const q = new MatchQueue();
    const now = 1_000_000;
    enqueuePlayer(q, 0, now);
    enqueuePlayer(q, 1, now);
    enqueuePlayer(q, 2, now + 1000);
    expect(q.tryMatch(now + 5999).length).toBe(0);
    const tables = q.tryMatch(now + 6000);
    expect(tables.length).toBe(1);
    expect(tables[0].players.length).toBe(3);
  });

  it("does not match 1 player", () => {
    const q = new MatchQueue();
    enqueuePlayer(q, 0, Date.now());
    expect(q.tryMatch(Date.now() + QUEUE_FILL_MS * 2).length).toBe(0);
    expect(q.size).toBe(1);
  });

  it("reports min/max in status", () => {
    const q = new MatchQueue();
    enqueuePlayer(q, 0, Date.now());
    const st = q.statusFor("p0");
    expect(st?.minPlayers).toBe(QUEUE_MIN);
    expect(st?.maxPlayers).toBe(QUEUE_MAX);
  });

  it("stores fillBots on queue entry", () => {
    const q = new MatchQueue();
    enqueuePlayer(q, 0, Date.now(), true);
    expect(q.getTicket("p0")?.fillBots).toBe(true);
  });
});

describe("queue bot fill", () => {
  it("fills empty seats with bots up to 4", () => {
    const room = new Room("BOT01", "p0", { fromQueue: true, chips: ONLINE_BUY_IN });
    const fakeWs = { readyState: 1, send() {} } as any;
    expect(room.add(fakeWs, "p0", "Human0").ok).toBe(true);
    expect(room.add({ readyState: 1, send() {} } as any, "p1", "Human1").ok).toBe(true);
    while (room.seats.length < QUEUE_MAX) {
      const r = room.addBot();
      expect(r.ok).toBe(true);
    }
    expect(room.seats.length).toBe(QUEUE_MAX);
    expect(room.seats.filter((s) => s.isBot).length).toBe(2);
    expect(room.start()).toBeNull();
    expect(room.table?.players.length).toBe(QUEUE_MAX);
  });
});
