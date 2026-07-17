export type Suit = 0 | 1 | 2 | 3; // C D H S
export type Rank = 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14;

export interface Card {
  rank: Rank;
  suit: Suit;
}

export function cardCode(c: Card): string {
  const r =
    c.rank === 14 ? "A" : c.rank === 13 ? "K" : c.rank === 12 ? "Q" : c.rank === 11 ? "J" : c.rank === 10 ? "T" : String(c.rank);
  const s = ["C", "D", "H", "S"][c.suit];
  return r + s;
}

export class Deck {
  private cards: Card[] = [];
  private rng: () => number;

  constructor(seed?: number) {
    let s = seed ?? Date.now();
    this.rng = () => {
      s = (s * 1664525 + 1013904223) >>> 0;
      return s / 0x100000000;
    };
    this.reset();
  }

  reset() {
    this.cards = [];
    for (let suit = 0; suit < 4; suit++) {
      for (let rank = 2; rank <= 14; rank++) {
        this.cards.push({ rank: rank as Rank, suit: suit as Suit });
      }
    }
  }

  shuffle() {
    for (let i = this.cards.length - 1; i > 0; i--) {
      const j = Math.floor(this.rng() * (i + 1));
      [this.cards[i], this.cards[j]] = [this.cards[j], this.cards[i]];
    }
  }

  deal(): Card {
    const c = this.cards.pop();
    if (!c) throw new Error("Deck empty");
    return c;
  }

  burn() {
    if (this.cards.length) this.cards.pop();
  }
}
