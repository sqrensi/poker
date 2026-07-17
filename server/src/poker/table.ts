import type { Card } from "./cards.js";
import { Deck } from "./cards.js";
import { evaluateBest, type HandValue } from "./hand.js";

export type Street = "waiting" | "preflop" | "flop" | "turn" | "river" | "showdown" | "handComplete";
export type ActionType = "fold" | "check" | "call" | "bet" | "raise" | "allin";

export interface Player {
  seat: number;
  id: string;
  name: string;
  chips: number;
  hole: Card[];
  betStreet: number;
  totalBet: number;
  folded: boolean;
  allIn: boolean;
  acted: boolean;
}

export interface LegalActions {
  canFold: boolean;
  canCheck: boolean;
  canCall: boolean;
  callAmount: number;
  canBet: boolean;
  canRaise: boolean;
  minRaiseTo: number;
  maxRaiseTo: number;
  pot: number;
  currentBet: number;
}

export interface PotResult {
  amount: number;
  winners: number[];
  description: string;
}

function clamp(v: number, lo: number, hi: number) {
  return Math.max(lo, Math.min(hi, v));
}

export class PokerTable {
  players: Player[];
  board: Card[] = [];
  street: Street = "waiting";
  pot = 0;
  currentBet = 0;
  dealer = 0;
  sbSeat = 0;
  bbSeat = 0;
  acting = -1;
  handNumber = 0;
  lastLog = "";
  lastPots: PotResult[] = [];
  showdownHands: Record<number, HandValue> = {};
  readonly sb: number;
  readonly bb: number;
  private deck: Deck;
  private raiseSize: number;

  constructor(players: Player[], sb = 5, bb = 10, seed?: number) {
    if (players.length < 2 || players.length > 9) throw new Error("2-9 players");
    this.players = players;
    this.sb = sb;
    this.bb = bb;
    this.raiseSize = bb;
    this.deck = new Deck(seed);
    this.dealer = players.length - 1;
  }

  private canAct(p: Player) {
    return !p.folded && !p.allIn && p.chips > 0;
  }

  private commit(p: Player, amount: number) {
    const paid = Math.min(amount, p.chips);
    p.chips -= paid;
    p.betStreet += paid;
    p.totalBet += paid;
    if (p.chips === 0) p.allIn = true;
    return paid;
  }

  private countChips() {
    return this.players.filter((p) => p.chips > 0).length;
  }

  private countInHand() {
    return this.players.filter((p) => !p.folded).length;
  }

  private nextWithChips(from: number) {
    let s = from;
    for (let i = 0; i < this.players.length; i++) {
      s = (s + 1) % this.players.length;
      if (this.players[s].chips > 0) return s;
    }
    return from;
  }

  private nextCanAct(from: number) {
    let s = from;
    for (let i = 0; i < this.players.length; i++) {
      s = (s + 1) % this.players.length;
      if (this.canAct(this.players[s])) return s;
    }
    return -1;
  }

  startHand() {
    if (this.countChips() < 2) {
      this.street = "handComplete";
      this.lastLog = "Недостаточно игроков с фишками";
      return;
    }
    this.handNumber++;
    this.board = [];
    this.pot = 0;
    this.currentBet = 0;
    this.raiseSize = this.bb;
    this.lastPots = [];
    this.showdownHands = {};
    this.lastLog = `Раздача №${this.handNumber}`;
    this.acting = -1;

    for (const p of this.players) {
      p.hole = [];
      p.betStreet = 0;
      p.totalBet = 0;
      p.folded = false;
      p.allIn = false;
      p.acted = false;
    }

    this.dealer = this.nextWithChips(this.dealer);
    const live = this.countChips();
    if (live === 2) {
      this.sbSeat = this.dealer;
      this.bbSeat = this.nextWithChips(this.dealer);
    } else {
      this.sbSeat = this.nextWithChips(this.dealer);
      this.bbSeat = this.nextWithChips(this.sbSeat);
    }

    this.deck.reset();
    this.deck.shuffle();
    this.postBlind(this.sbSeat, this.sb, "МБ");
    this.postBlind(this.bbSeat, this.bb, "ББ");
    this.currentBet = Math.max(this.players[this.sbSeat].betStreet, this.players[this.bbSeat].betStreet);

    for (let r = 0; r < 2; r++) {
      let seat = this.dealer;
      for (let i = 0; i < live; i++) {
        seat = this.nextWithChips(seat);
        this.players[seat].hole.push(this.deck.deal());
      }
    }

    this.street = "preflop";
    this.acting = this.nextCanAct(this.bbSeat);
    if (this.acting < 0) this.runOut();
  }

  private postBlind(seat: number, amount: number, label: string) {
    const p = this.players[seat];
    const paid = this.commit(p, amount);
    this.pot += paid;
    this.lastLog = `${p.name} ставит ${label} ${paid}`;
  }

  getLegal(seat: number): LegalActions | null {
    if (seat !== this.acting || seat < 0) return null;
    const p = this.players[seat];
    if (!this.canAct(p)) return null;
    const toCall = Math.max(0, this.currentBet - p.betStreet);
    const maxRaiseTo = p.betStreet + p.chips;
    if (this.currentBet === 0) {
      const minBet = Math.min(this.bb, maxRaiseTo);
      return {
        canFold: false,
        canCheck: true,
        canCall: false,
        callAmount: 0,
        canBet: maxRaiseTo > 0,
        canRaise: false,
        minRaiseTo: minBet,
        maxRaiseTo,
        pot: this.pot,
        currentBet: this.currentBet,
      };
    }
    let minRaiseTo = this.currentBet + this.raiseSize;
    if (minRaiseTo > maxRaiseTo) minRaiseTo = maxRaiseTo;
    return {
      canFold: true,
      canCheck: toCall === 0,
      canCall: toCall > 0,
      callAmount: Math.min(toCall, p.chips),
      canBet: false,
      canRaise: p.chips > toCall,
      minRaiseTo,
      maxRaiseTo,
      pot: this.pot,
      currentBet: this.currentBet,
    };
  }

  apply(seat: number, type: ActionType, amount = 0): boolean {
    if (this.street !== "preflop" && this.street !== "flop" && this.street !== "turn" && this.street !== "river") return false;
    if (seat !== this.acting) return false;
    const p = this.players[seat];
    if (!this.canAct(p)) return false;
    const legal = this.getLegal(seat);
    if (!legal) return false;

    if (type === "fold") {
      if (legal.canCheck) return false;
      p.folded = true;
      p.acted = true;
      this.lastLog = `${p.name}: фолд`;
    } else if (type === "check") {
      if (!legal.canCheck) return false;
      p.acted = true;
      this.lastLog = `${p.name}: чек`;
    } else if (type === "call") {
      if (!legal.canCall) return false;
      const pay = legal.callAmount;
      this.pot += this.commit(p, pay);
      p.acted = true;
      this.lastLog = p.allIn ? `${p.name}: колл олл-ин (${pay})` : `${p.name}: колл ${pay}`;
    } else if (type === "bet" || type === "raise" || type === "allin") {
      if (!this.applyBetRaise(p, seat, type, amount, legal)) return false;
    } else return false;

    this.advance();
    return true;
  }

  private reopen(except: number) {
    for (const o of this.players) {
      if (o.seat === except) continue;
      if (this.canAct(o)) o.acted = false;
    }
  }

  private applyBetRaise(p: Player, seat: number, type: ActionType, amount: number, legal: LegalActions): boolean {
    let raiseTo = type === "allin" ? p.betStreet + p.chips : amount;
    if (this.currentBet === 0) {
      if (p.chips <= 0) return false;
      raiseTo = type === "allin" ? p.betStreet + p.chips : clamp(raiseTo, legal.minRaiseTo, legal.maxRaiseTo);
      const prev = this.currentBet;
      const pay = raiseTo - p.betStreet;
      if (pay <= 0) return false;
      this.pot += this.commit(p, pay);
      this.raiseSize = Math.max(this.bb, p.betStreet - prev);
      this.currentBet = p.betStreet;
      this.reopen(seat);
      p.acted = true;
      this.lastLog = p.allIn ? `${p.name}: бет олл-ин (${this.currentBet})` : `${p.name}: бет ${this.currentBet}`;
      return true;
    }
    const maxTo = legal.maxRaiseTo;
    if (type === "allin") raiseTo = maxTo;
    else {
      if (!legal.canRaise && raiseTo < maxTo) return false;
      raiseTo = clamp(raiseTo, Math.min(legal.minRaiseTo, maxTo), maxTo);
    }
    const prevBet = this.currentBet;
    const need = raiseTo - p.betStreet;
    if (need <= 0) return false;
    this.pot += this.commit(p, need);
    const newBet = p.betStreet;
    if (newBet > prevBet) {
      const raisedBy = newBet - prevBet;
      const full = raisedBy >= this.raiseSize;
      this.currentBet = newBet;
      if (full) {
        this.raiseSize = raisedBy;
        this.reopen(seat);
      } else {
        for (const o of this.players) {
          if (o.seat === seat) continue;
          if (!this.canAct(o)) continue;
          if (o.betStreet < this.currentBet) o.acted = false;
        }
      }
    }
    p.acted = true;
    this.lastLog = p.allIn ? `${p.name}: рейз олл-ин до ${newBet}` : `${p.name}: рейз до ${newBet}`;
    return true;
  }

  private bettingDone() {
    for (const p of this.players) {
      if (p.folded || p.allIn) continue;
      if (p.chips <= 0) continue;
      if (!p.acted) return false;
      if (p.betStreet !== this.currentBet) return false;
    }
    return true;
  }

  private advance() {
    if (this.countInHand() === 1) {
      this.awardUncontested();
      return;
    }
    if (this.bettingDone()) {
      this.nextStreet();
      return;
    }
    this.acting = this.nextCanAct(this.acting);
    if (this.acting < 0) this.nextStreet();
  }

  private resetStreet() {
    for (const p of this.players) {
      p.betStreet = 0;
      p.acted = false;
    }
    this.currentBet = 0;
    this.raiseSize = this.bb;
    this.acting = -1;
  }

  private dealCommunity(n: number) {
    this.deck.burn();
    for (let i = 0; i < n; i++) this.board.push(this.deck.deal());
  }

  private nextStreet() {
    this.resetStreet();
    if (this.street === "preflop") {
      this.dealCommunity(3);
      this.street = "flop";
      this.lastLog = "Открыт флоп";
    } else if (this.street === "flop") {
      this.dealCommunity(1);
      this.street = "turn";
      this.lastLog = "Открыт тёрн";
    } else if (this.street === "turn") {
      this.dealCommunity(1);
      this.street = "river";
      this.lastLog = "Открыт ривер";
    } else if (this.street === "river") {
      this.showdown();
      return;
    }
    const actors = this.players.filter((p) => this.canAct(p)).length;
    if (actors <= 1) {
      this.runOut();
      return;
    }
    this.acting = this.nextCanAct(this.dealer);
    if (this.acting < 0) this.runOut();
  }

  private runOut() {
    while (this.board.length < 3) {
      this.deck.burn();
      this.board.push(this.deck.deal());
    }
    while (this.board.length < 5) {
      this.deck.burn();
      this.board.push(this.deck.deal());
    }
    this.showdown();
  }

  private awardUncontested() {
    const winner = this.players.find((p) => !p.folded);
    if (!winner) return;
    winner.chips += this.pot;
    this.lastPots = [{ amount: this.pot, winners: [winner.seat], description: "Без вскрытия" }];
    this.lastLog = `${winner.name} забирает банк ${this.pot}`;
    this.pot = 0;
    this.street = "handComplete";
    this.acting = -1;
  }

  private showdown() {
    this.street = "showdown";
    const contenders = this.players.filter((p) => !p.folded);
    this.showdownHands = {};
    for (const p of contenders) {
      this.showdownHands[p.seat] = evaluateBest([...p.hole, ...this.board]);
    }
    this.distribute(contenders);
    this.street = "handComplete";
    this.acting = -1;
    const pot = this.lastPots[0];
    this.lastLog = pot
      ? `Банк ${pot.amount} → [${pot.winners.join(",")}] (${pot.description})`
      : "Вскрытие";
  }

  private distribute(contenders: Player[]) {
    const levels = [...new Set(this.players.filter((p) => p.totalBet > 0).map((p) => p.totalBet))].sort((a, b) => a - b);
    let prev = 0;
    this.lastPots = [];
    for (const level of levels) {
      const layer = level - prev;
      if (layer <= 0) {
        prev = level;
        continue;
      }
      const contributors = this.players.filter((p) => p.totalBet >= level).length;
      const potAmount = layer * contributors;
      if (potAmount <= 0) {
        prev = level;
        continue;
      }
      let eligible = contenders.filter((p) => p.totalBet >= level);
      if (!eligible.length) eligible = [...contenders];
      let best = this.showdownHands[eligible[0].seat];
      for (const p of eligible) {
        const hv = this.showdownHands[p.seat];
        if (hv.score > best.score) best = hv;
      }
      const winners = eligible.filter((p) => this.showdownHands[p.seat].score === best.score);
      const share = Math.floor(potAmount / winners.length);
      const rem = potAmount % winners.length;
      const pot: PotResult = { amount: potAmount, winners: [], description: best.description };
      winners.forEach((w, i) => {
        const gain = share + (i < rem ? 1 : 0);
        w.chips += gain;
        pot.winners.push(w.seat);
      });
      this.lastPots.push(pot);
      prev = level;
    }
    this.pot = 0;
  }
}
