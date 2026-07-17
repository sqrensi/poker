import { describe, it, expect } from "vitest";
import type { Card } from "./cards.js";
import { evaluateFive, evaluateBest, HandCategory } from "../src/poker/hand.js";
import { Deck } from "../src/poker/cards.js";
import { PokerTable, type Player } from "../src/poker/table.js";
import { Room } from "../src/net/rooms.js";

function C(rank: number, suit: number): Card {
  return { rank: rank as Card["rank"], suit: suit as Card["suit"] };
}

function players(n: number): Player[] {
  return Array.from({ length: n }, (_, i) => ({
    seat: i,
    id: `p${i}`,
    name: `P${i}`,
    chips: 1000,
    hole: [],
    betStreet: 0,
    totalBet: 0,
    folded: false,
    allIn: false,
    acted: false,
  }));
}

describe("HandEvaluator", () => {
  it("detects royal flush", () => {
    const hv = evaluateFive([C(14, 3), C(13, 3), C(12, 3), C(11, 3), C(10, 3)]);
    expect(hv.category).toBe(HandCategory.RoyalFlush);
  });

  it("detects pair from seven cards", () => {
    const hv = evaluateBest([C(14, 0), C(14, 1), C(2, 2), C(5, 3), C(9, 0), C(3, 1), C(7, 2)]);
    expect(hv.category).toBe(HandCategory.OnePair);
  });

  it("wheel straight", () => {
    const hv = evaluateFive([C(14, 0), C(2, 1), C(3, 2), C(4, 3), C(5, 0)]);
    expect(hv.category).toBe(HandCategory.Straight);
  });
});

describe("Deck", () => {
  it("deals 52 unique then empty", () => {
    const d = new Deck(1);
    d.shuffle();
    const set = new Set<string>();
    for (let i = 0; i < 52; i++) {
      const c = d.deal();
      set.add(`${c.rank}-${c.suit}`);
    }
    expect(set.size).toBe(52);
    expect(() => d.deal()).toThrow();
  });
});

describe("PokerTable", () => {
  it("starts hand, posts blinds, deals holes", () => {
    const t = new PokerTable(players(3), 5, 10, 42);
    t.startHand();
    expect(t.street).toBe("preflop");
    expect(t.pot).toBeGreaterThanOrEqual(15);
    expect(t.players.every((p) => p.hole.length === 2)).toBe(true);
    expect(t.acting).toBeGreaterThanOrEqual(0);
  });

  it("fold leaves one winner uncontested", () => {
    const t = new PokerTable(players(2), 5, 10, 7);
    t.startHand();
    // Heads-up: acting is SB after blinds
    let guard = 0;
    while (t.street === "preflop" && guard++ < 20) {
      const seat = t.acting;
      const legal = t.getLegal(seat);
      if (!legal) break;
      if (legal.canFold) t.apply(seat, "fold");
      else if (legal.canCheck) t.apply(seat, "check");
      else if (legal.canCall) t.apply(seat, "call");
      else break;
    }
    // If both called, force folds later — ensure we can complete a hand
    guard = 0;
    while (t.street !== "handComplete" && t.street !== "matchComplete" && guard++ < 80) {
      const seat = t.acting;
      if (seat < 0) break;
      const legal = t.getLegal(seat);
      if (!legal) break;
      if (legal.canCheck) t.apply(seat, "check");
      else if (legal.canCall) t.apply(seat, "call");
      else if (legal.canFold) t.apply(seat, "fold");
      else t.apply(seat, "allin");
    }
    expect(["handComplete", "matchComplete"]).toContain(t.street);
    expect(t.players.reduce((s, p) => s + p.chips, 0)).toBe(2000);
  });

  it("chip conservation over many actions", () => {
    const t = new PokerTable(players(4), 5, 10, 99);
    t.startHand();
    let guard = 0;
    while (t.street !== "handComplete" && t.street !== "matchComplete" && guard++ < 120) {
      const seat = t.acting;
      if (seat < 0) break;
      const legal = t.getLegal(seat)!;
      if (legal.canCheck) t.apply(seat, "check");
      else if (legal.canCall) t.apply(seat, "call");
      else if (legal.canFold && Math.random() < 0.2) t.apply(seat, "fold");
      else if (legal.canCall) t.apply(seat, "call");
      else t.apply(seat, "allin");
    }
    const sum = t.players.reduce((s, p) => s + p.chips, 0) + t.pot;
    expect(sum).toBe(4000);
  });

  it("ends match when only one player has chips", () => {
    const ps = players(2);
    ps[0].chips = 30;
    ps[1].chips = 30;
    const t = new PokerTable(ps, 5, 10, 3);
    let hands = 0;
    while (t.street !== "matchComplete" && hands++ < 40) {
      if (t.street === "waiting" || t.street === "handComplete") t.startHand();
      let guard = 0;
      while (t.street !== "handComplete" && t.street !== "matchComplete" && guard++ < 100) {
        const seat = t.acting;
        if (seat < 0) break;
        t.apply(seat, "allin");
      }
    }
    expect(t.street).toBe("matchComplete");
    expect(t.matchWinner).toBeGreaterThanOrEqual(0);
    expect(t.players.filter((p) => p.chips > 0).length).toBe(1);
    t.restartMatch(100);
    expect(t.street).toBe("preflop");
    expect(t.players.every((p) => !p.eliminated)).toBe(true);
    expect(t.players.reduce((s, p) => s + p.chips, 0) + t.pot).toBe(200);
  });
});

describe("Room", () => {
  it("creates lobby and starts with 2 players", () => {
    const room = new Room("TEST01", "host");
    const fakeWs = { readyState: 1, send() {} } as any;
    expect(room.add(fakeWs, "host", "Host").ok).toBe(true);
    expect(room.add({ readyState: 1, send() {} } as any, "guest", "Guest").ok).toBe(true);
    expect(room.start()).toBeNull();
    expect(room.started).toBe(true);
    expect(room.table?.street).toBe("preflop");
    const snap = room.snapshotFor("guest");
    const me = snap.table!.players.find((p) => p.id === "guest")!;
    const host = snap.table!.players.find((p) => p.id === "host")!;
    expect(me.hole.every((c) => c !== "??")).toBe(true);
    expect(host.hole.every((c) => c === "??")).toBe(true);
  });
});
