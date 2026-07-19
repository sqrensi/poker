import type { Card } from "../poker/cards.js";
import { evaluateBest } from "../poker/hand.js";
import type { ActionType, LegalActions, PokerTable, Street } from "../poker/table.js";

/** Монстр-бот: Monte Carlo equity, Chen, позиция, дро, push/fold. */

const MC_PRE = 140;
const MC_POST = 160;

const deckScratch: Card[] = [];
const boardScratch: Card[] = new Array(5);
let evalBuf: Card[] = [];

export function decideBot(table: PokerTable, seat: number): { action: ActionType; amount?: number } {
  const legal = table.getLegal(seat);
  if (!legal) return { action: "check" };

  const me = table.players[seat];
  if (!me || me.hole.length < 2) return fallback(legal);

  let villains = countVillains(table, seat);
  if (villains < 1) villains = 1;

  const board = table.board;
  let equity = estimateEquity(me.hole, board, villains, board.length >= 3 ? MC_POST : MC_PRE);
  const chen = chenScore(me.hole[0], me.hole[1]);
  const chenNorm = clamp01(chen / 20);
  if (board.length < 3) equity = equity * 0.55 + chenNorm * 0.45;

  const outs = estimateOuts(me.hole, board);
  const drawEq =
    table.street === "flop" ? outs * 0.021 : table.street === "turn" ? outs * 0.011 : 0;
  let effective = clamp01(equity + drawEq);
  effective = clamp(effective + Math.random() * 0.03 - 0.015, 0.02, 0.98);

  const potOdds =
    legal.callAmount > 0 ? legal.callAmount / Math.max(1, legal.pot + legal.callAmount) : 0;
  const pos = positionScore(table, seat);
  const stackBb = me.chips / Math.max(1, table.bb);
  const spr = legal.pot > 0 ? me.chips / legal.pot : stackBb;
  const multiPenalty = Math.max(0, villains - 1) * 0.035;
  const posBonus = (pos - 0.5) * 0.06;

  if (table.street === "preflop" && stackBb <= 14) {
    return decideShortStack(legal, table, chen, chenNorm, potOdds, pos, stackBb, villains);
  }

  if (legal.canCheck) {
    return decideChecked(legal, table, effective, outs, pos, spr, stackBb, multiPenalty, posBonus);
  }
  return decideFacing(
    legal,
    table,
    effective,
    potOdds,
    outs,
    pos,
    spr,
    stackBb,
    multiPenalty,
    posBonus,
    chenNorm
  );
}

function decideShortStack(
  legal: LegalActions,
  table: PokerTable,
  chen: number,
  chenNorm: number,
  potOdds: number,
  pos: number,
  stackBb: number,
  villains: number
): { action: ActionType; amount?: number } {
  let shoveBar = villains >= 4 ? 0.62 : villains >= 3 ? 0.52 : 0.42;
  shoveBar -= pos * 0.12;
  if (stackBb <= 8) shoveBar -= 0.08;

  if (legal.canCheck || legal.callAmount === 0) {
    if (legal.canBet && chenNorm >= shoveBar) return raiseTo(legal, legal.maxRaiseTo);
    return { action: "check" };
  }

  const callBar = potOdds + 0.04 + Math.max(0, villains - 1) * 0.03 - pos * 0.04;
  if (chenNorm >= callBar || chen >= 12) {
    if (legal.canCall) return { action: "call" };
    if (legal.canRaise) return raiseTo(legal, legal.maxRaiseTo);
  }
  if (chenNorm >= shoveBar + 0.08 && legal.canRaise) return raiseTo(legal, legal.maxRaiseTo);
  if (legal.canFold) return { action: "fold" };
  return legal.canCall ? { action: "call" } : { action: "check" };
}

function decideChecked(
  legal: LegalActions,
  table: PokerTable,
  eq: number,
  outs: number,
  pos: number,
  spr: number,
  _stackBb: number,
  multiPenalty: number,
  posBonus: number
): { action: ActionType; amount?: number } {
  const valueLine = 0.58 + multiPenalty - posBonus;
  const thinValue = 0.48 + multiPenalty * 0.5 - posBonus;
  const bluffLine = 0.22 + multiPenalty;
  const strongDraw = outs >= 8;
  const mediumDraw = outs >= 4;

  if (eq >= valueLine + 0.12 && legal.canBet) {
    return betPot(legal, spr < 2.5 ? 0.95 : 0.7 + eq * 0.35);
  }
  if (eq >= valueLine && legal.canBet) {
    return betPot(legal, 0.55 + eq * 0.3 + pos * 0.08);
  }
  if (
    eq >= thinValue &&
    legal.canBet &&
    (multiPenalty > 0.02 || table.street === "river" || Math.random() < 0.55)
  ) {
    return betPot(legal, 0.4 + eq * 0.25);
  }
  if ((strongDraw || (mediumDraw && pos > 0.45)) && legal.canBet && streetBeforeRiver(table.street)) {
    const freq = (strongDraw ? 0.72 : 0.45) + pos * 0.15;
    if (Math.random() < freq) return betPot(legal, 0.55 + pos * 0.15);
  }
  if (eq < bluffLine && legal.canBet && (table.street === "turn" || table.street === "river")) {
    let freq = 0.06 + pos * 0.1;
    if (table.street === "river") freq += 0.04;
    if (Math.random() < freq) return betPot(legal, 0.55 + Math.random() * 0.25);
  }
  if (
    table.street === "preflop" &&
    legal.canBet &&
    eq >= 0.62 - pos * 0.1 &&
    Math.random() < 0.18 + pos * 0.1
  ) {
    const to = Math.max(legal.minRaiseTo, Math.floor(table.bb * (3 + pos)));
    return raiseTo(legal, clamp(to, legal.minRaiseTo, legal.maxRaiseTo));
  }
  return { action: "check" };
}

function decideFacing(
  legal: LegalActions,
  table: PokerTable,
  eq: number,
  potOdds: number,
  outs: number,
  pos: number,
  spr: number,
  stackBb: number,
  multiPenalty: number,
  posBonus: number,
  chenNorm: number
): { action: ActionType; amount?: number } {
  let required = potOdds + 0.02 + multiPenalty - posBonus * 0.5;
  if (spr < 3) required -= 0.03;
  if (legal.callAmount <= table.bb) required -= 0.06;

  const raiseValue = required + 0.18 + multiPenalty * 0.5;
  const jamValue = 0.72 + multiPenalty * 0.3;
  const strongDraw = outs >= 8;
  const playableDraw = outs >= 4 && streetBeforeRiver(table.street);

  if (table.street === "preflop") {
    const onlyBlinds = legal.currentBet <= table.bb;
    const openBar = 0.4 - pos * 0.14 + multiPenalty * 0.5;
    const callBar = onlyBlinds ? openBar - 0.06 : required;
    const threeBetBar = 0.58 - pos * 0.12;

    if (onlyBlinds && chenNorm >= openBar && legal.canRaise) {
      const openTo = Math.max(legal.minRaiseTo, Math.floor(table.bb * (2.3 + (1 - pos) * 1.0)));
      if (stackBb <= 18 && chenNorm >= openBar + 0.08) return raiseTo(legal, legal.maxRaiseTo);
      return raiseTo(legal, clamp(openTo, legal.minRaiseTo, legal.maxRaiseTo));
    }
    if (chenNorm >= threeBetBar && legal.canRaise && !onlyBlinds) {
      const threeBet = Math.max(legal.minRaiseTo, Math.floor(legal.currentBet * (2.5 + pos * 0.5)));
      if (chenNorm >= 0.78 || stackBb <= 22) return raiseTo(legal, legal.maxRaiseTo);
      return raiseTo(legal, clamp(threeBet, legal.minRaiseTo, legal.maxRaiseTo));
    }
    if (!onlyBlinds && legal.canRaise && pos >= 0.7 && chenNorm >= 0.42 && Math.random() < 0.22) {
      const threeBet = Math.max(legal.minRaiseTo, Math.floor(legal.currentBet * 2.8));
      return raiseTo(legal, clamp(threeBet, legal.minRaiseTo, legal.maxRaiseTo));
    }
    if (legal.canCall && (chenNorm >= callBar || (onlyBlinds && chenNorm >= 0.32 && pos > 0.55))) {
      return { action: "call" };
    }
    if (legal.canFold) return { action: "fold" };
    if (legal.canCall) return { action: "call" };
    return { action: "check" };
  }

  if (eq + (playableDraw ? 0.06 : 0) < required - 0.07 && !strongDraw) {
    if (legal.canFold) return { action: "fold" };
  }

  if (eq >= raiseValue && legal.canRaise) {
    const potFrac = eq >= jamValue || spr < 2.2 ? 1.15 : 0.65 + eq * 0.45;
    if (eq >= jamValue && stackBb <= 25) return raiseTo(legal, legal.maxRaiseTo);
    return raisePot(legal, potFrac);
  }

  if (strongDraw && legal.canRaise && eq >= potOdds - 0.02 && Math.random() < 0.4 + pos * 0.25) {
    return raisePot(legal, 0.65 + pos * 0.2);
  }

  if (legal.canCall) {
    if (eq >= required - 0.02 || strongDraw || (playableDraw && eq >= required - 0.08)) {
      return { action: "call" };
    }
    if (legal.callAmount <= table.bb && eq >= 0.28) return { action: "call" };
    if (playableDraw && stackBb >= 40 && eq >= required - 0.12) return { action: "call" };
  }

  if (legal.canFold) return { action: "fold" };
  if (legal.canCall) return { action: "call" };
  return { action: "check" };
}

function betPot(legal: LegalActions, potFrac: number): { action: ActionType; amount?: number } {
  if (!legal.canBet) return { action: "check" };
  const amount = clamp(Math.floor(legal.pot * potFrac), legal.minRaiseTo, legal.maxRaiseTo);
  return { action: "bet", amount };
}

function raisePot(legal: LegalActions, potFrac: number): { action: ActionType; amount?: number } {
  if (!legal.canRaise) return legal.canCall ? { action: "call" } : { action: "fold" };
  const raiseBy = Math.max(legal.minRaiseTo - legal.currentBet, Math.floor(legal.pot * potFrac));
  const to = clamp(legal.currentBet + raiseBy, legal.minRaiseTo, legal.maxRaiseTo);
  return { action: "raise", amount: to };
}

function raiseTo(legal: LegalActions, to: number): { action: ActionType; amount?: number } {
  to = clamp(to, legal.minRaiseTo, legal.maxRaiseTo);
  if (legal.canBet && legal.currentBet === 0) return { action: "bet", amount: to };
  if (legal.canRaise) return { action: "raise", amount: to };
  if (legal.canCall) return { action: "call" };
  return { action: "check" };
}

function fallback(legal: LegalActions): { action: ActionType; amount?: number } {
  if (legal.canCheck) return { action: "check" };
  if (legal.canFold) return { action: "fold" };
  return { action: "check" };
}

function streetBeforeRiver(s: Street) {
  return s === "preflop" || s === "flop" || s === "turn";
}

// ——— Equity ———

export function estimateEquity(hole: Card[], board: Card[], opponents: number, trials: number): number {
  opponents = Math.max(1, Math.min(opponents, 8));
  trials = Math.max(40, trials);

  const deckLen = buildRemaining(hole, board);
  const boardNeed = Math.max(0, 5 - board.length);
  if (deckLen < 2 * opponents + boardNeed) return 0.35;

  let scoreSum = 0;
  for (let t = 0; t < trials; t++) {
    shufflePrefix(deckLen);
    let idx = 0;
    for (let i = 0; i < board.length; i++) boardScratch[i] = board[i];
    for (let i = 0; i < boardNeed; i++) boardScratch[board.length + i] = deckScratch[idx++];

    const heroScore = evalSeven(hole[0], hole[1], boardScratch);
    let bestOpp = -Infinity;
    let tiesAtBest = 0;
    for (let o = 0; o < opponents; o++) {
      const o0 = deckScratch[idx++];
      const o1 = deckScratch[idx++];
      const s = evalSeven(o0, o1, boardScratch);
      if (s > bestOpp) {
        bestOpp = s;
        tiesAtBest = 1;
      } else if (s === bestOpp) tiesAtBest++;
    }
    if (heroScore > bestOpp) scoreSum += 1;
    else if (heroScore === bestOpp) scoreSum += 1 / (1 + tiesAtBest);
  }
  return scoreSum / trials;
}

function evalSeven(h0: Card, h1: Card, board5: Card[]): number {
  evalBuf = [h0, h1, board5[0], board5[1], board5[2], board5[3], board5[4]];
  return evaluateBest(evalBuf).score;
}

function buildRemaining(hole: Card[], board: Card[]): number {
  deckScratch.length = 0;
  for (let suit = 0; suit < 4; suit++) {
    for (let rank = 2; rank <= 14; rank++) {
      const c: Card = { rank: rank as Card["rank"], suit: suit as Card["suit"] };
      if (hasCard(hole, c) || hasCard(board, c)) continue;
      deckScratch.push(c);
    }
  }
  return deckScratch.length;
}

function hasCard(list: Card[], c: Card) {
  return list.some((x) => x.rank === c.rank && x.suit === c.suit);
}

function shufflePrefix(n: number) {
  for (let i = n - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    const tmp = deckScratch[i];
    deckScratch[i] = deckScratch[j];
    deckScratch[j] = tmp;
  }
}

export function chenScore(a: Card, b: Card): number {
  const hi = Math.max(a.rank, b.rank);
  const lo = Math.min(a.rank, b.rank);
  const suited = a.suit === b.suit;
  const pair = a.rank === b.rank;
  const scoreRank = (r: number) =>
    r === 14 ? 10 : r === 13 ? 8 : r === 12 ? 7 : r === 11 ? 6 : r === 10 ? 5 : r / 2;
  let score = scoreRank(hi);
  if (pair) score = Math.max(5, score * 2);
  else {
    if (suited) score += 2;
    const gap = hi - lo - 1;
    if (gap === 0) score += 1;
    else if (gap === 1) score += 0;
    else if (gap === 2) score -= 1;
    else if (gap === 3) score -= 2;
    else score -= 4;
    if (gap <= 1 && hi < 12) score += 1;
  }
  return Math.max(0, score);
}

function estimateOuts(hole: Card[], board: Card[]): number {
  if (!board || board.length < 3 || board.length >= 5) return 0;
  let outs = 0;
  const suits = [0, 0, 0, 0];
  const ranks = new Array(15).fill(0);
  const add = (c: Card) => {
    suits[c.suit]++;
    ranks[c.rank]++;
  };
  add(hole[0]);
  add(hole[1]);
  for (const c of board) add(c);

  for (let s = 0; s < 4; s++) {
    if (suits[s] === 4 && (hole[0].suit === s || hole[1].suit === s)) outs += 9;
  }

  const present = new Array(15).fill(false);
  for (let r = 2; r <= 14; r++) if (ranks[r] > 0) present[r] = true;
  if (present[14]) present[1] = true;

  let bestNeed = 5;
  for (let high = 5; high <= 14; high++) {
    let have = 0;
    for (let r = high - 4; r <= high; r++) {
      const rr = r === 1 ? 1 : r;
      if (rr >= 1 && rr <= 14 && present[rr]) have++;
    }
    bestNeed = Math.min(bestNeed, 5 - have);
  }
  if (bestNeed === 1) outs += 8;
  else if (bestNeed === 2) outs += 4;

  let boardHigh = 0;
  for (const c of board) boardHigh = Math.max(boardHigh, c.rank);
  if (hole[0].rank > boardHigh && hole[0].rank !== hole[1].rank) outs += 3;
  if (hole[1].rank > boardHigh && hole[0].rank !== hole[1].rank) outs += 3;

  return Math.min(outs, 15);
}

function countVillains(table: PokerTable, seat: number) {
  let n = 0;
  for (const p of table.players) {
    if (p.seat === seat) continue;
    if (!p.folded && !p.eliminated) n++;
  }
  return n;
}

function positionScore(table: PokerTable, seat: number) {
  const n = table.players.length;
  if (n <= 1) return 0.5;
  const dist = (seat - table.dealer + n) % n;
  return 1 - dist / (n - 1);
}

function clamp(v: number, lo: number, hi: number) {
  return Math.max(lo, Math.min(hi, v));
}
function clamp01(v: number) {
  return clamp(v, 0, 1);
}

export function isBotId(id: string) {
  return id.startsWith("bot-");
}
