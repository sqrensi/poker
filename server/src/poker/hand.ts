import type { Card } from "./cards.js";

export enum HandCategory {
  HighCard = 0,
  OnePair = 1,
  TwoPair = 2,
  ThreeOfAKind = 3,
  Straight = 4,
  Flush = 5,
  FullHouse = 6,
  FourOfAKind = 7,
  StraightFlush = 8,
  RoyalFlush = 9,
}

export interface HandValue {
  category: HandCategory;
  score: number;
  description: string;
}

function pack(cat: HandCategory, parts: number[], desc: string): HandValue {
  let score = cat << 20;
  const shifts = [16, 12, 8, 4, 0];
  for (let i = 0; i < parts.length && i < 5; i++) score |= (parts[i] & 0xf) << shifts[i];
  return { category: cat, score, description: desc };
}

function nameNom(r: number) {
  const m: Record<number, string> = {
    14: "туз", 13: "король", 12: "дама", 11: "валет", 10: "десятка",
    9: "девятка", 8: "восьмёрка", 7: "семёрка", 6: "шестёрка", 5: "пятёрка", 4: "четвёрка", 3: "тройка", 2: "двойка",
  };
  return m[r] ?? String(r);
}

function nameGen(r: number) {
  const m: Record<number, string> = {
    14: "тузов", 13: "королей", 12: "дам", 11: "валетов", 10: "десяток",
    9: "девяток", 8: "восьмёрок", 7: "семёрок", 6: "шестёрок", 5: "пятёрок", 4: "четвёрок", 3: "троек", 2: "двоек",
  };
  return m[r] ?? String(r);
}

function nameTo(r: number) {
  const m: Record<number, string> = {
    14: "туза", 13: "короля", 12: "дамы", 11: "валета", 10: "десятки",
    9: "девятки", 8: "восьмёрки", 7: "семёрки", 6: "шестёрки", 5: "пятёрки", 4: "четвёрки", 3: "тройки", 2: "двойки",
  };
  return m[r] ?? String(r);
}

function straightHigh(ranksDesc: number[]): number {
  const unique: number[] = [];
  for (const r of ranksDesc) {
    if (!unique.length || unique[unique.length - 1] !== r) unique.push(r);
  }
  if (unique.length !== 5) return 0;
  if (unique[0] - unique[4] === 4) return unique[0];
  if (unique[0] === 14 && unique[1] === 5 && unique[2] === 4 && unique[3] === 3 && unique[4] === 2) return 5;
  return 0;
}

export function evaluateFive(cards: Card[]): HandValue {
  const ranks = cards.map((c) => c.rank).sort((a, b) => b - a);
  const suits = cards.map((c) => c.suit);
  const flush = suits.every((s) => s === suits[0]);
  const sHigh = straightHigh(ranks);
  const straight = sHigh > 0;

  const counts = new Map<number, number>();
  for (const r of ranks) counts.set(r, (counts.get(r) ?? 0) + 1);
  const groups = [...counts.entries()]
    .map(([rank, count]) => ({ rank, count }))
    .sort((a, b) => b.count - a.count || b.rank - a.rank);

  if (straight && flush) {
    if (sHigh === 14) return pack(HandCategory.RoyalFlush, [sHigh], "Рояль-флеш");
    return pack(HandCategory.StraightFlush, [sHigh], `Стрит-флеш до ${nameTo(sHigh)}`);
  }
  if (groups[0].count === 4) {
    return pack(HandCategory.FourOfAKind, [groups[0].rank, groups[1].rank], `Каре ${nameGen(groups[0].rank)}`);
  }
  if (groups[0].count === 3 && groups[1].count === 2) {
    return pack(HandCategory.FullHouse, [groups[0].rank, groups[1].rank], `Фулл-хаус: ${nameGen(groups[0].rank)} и ${nameGen(groups[1].rank)}`);
  }
  if (flush) return pack(HandCategory.Flush, ranks, `Флеш до ${nameTo(ranks[0])}`);
  if (straight) return pack(HandCategory.Straight, [sHigh], `Стрит до ${nameTo(sHigh)}`);
  if (groups[0].count === 3) {
    return pack(HandCategory.ThreeOfAKind, [groups[0].rank, groups[1].rank, groups[2].rank], `Сет ${nameGen(groups[0].rank)}`);
  }
  if (groups[0].count === 2 && groups[1].count === 2) {
    const hi = Math.max(groups[0].rank, groups[1].rank);
    const lo = Math.min(groups[0].rank, groups[1].rank);
    return pack(HandCategory.TwoPair, [hi, lo, groups[2].rank], `Две пары: ${nameGen(hi)} и ${nameGen(lo)}`);
  }
  if (groups[0].count === 2) {
    return pack(
      HandCategory.OnePair,
      [groups[0].rank, groups[1].rank, groups[2].rank, groups[3].rank],
      `Пара ${nameGen(groups[0].rank)}`,
    );
  }
  return pack(HandCategory.HighCard, ranks, `Старшая карта: ${nameNom(ranks[0])}`);
}

export function evaluateBest(cards: Card[]): HandValue {
  if (cards.length < 5) throw new Error("Need >= 5 cards");
  let best: HandValue | null = null;
  const idx = [0, 0, 0, 0, 0];
  const n = cards.length;
  const combine = (start: number, depth: number) => {
    if (depth === 5) {
      const five = idx.map((i) => cards[i]);
      const v = evaluateFive(five);
      if (!best || v.score > best.score) best = v;
      return;
    }
    for (let i = start; i <= n - (5 - depth); i++) {
      idx[depth] = i;
      combine(i + 1, depth + 1);
    }
  };
  combine(0, 0);
  return best!;
}
