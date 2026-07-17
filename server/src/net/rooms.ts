import { WebSocket } from "ws";
import { cardCode } from "../poker/cards.js";
import { PokerTable } from "../poker/table.js";
import type { ActionType, Player } from "../poker/table.js";

export interface ClientMsg {
  type: string;
  name?: string;
  room?: string;
  action?: ActionType;
  amount?: number;
}

function roomCode() {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let s = "";
  for (let i = 0; i < 6; i++) s += alphabet[Math.floor(Math.random() * alphabet.length)];
  return s;
}

interface SeatConn {
  ws: WebSocket;
  playerId: string;
  seat: number;
  name: string;
}

export class Room {
  readonly code: string;
  hostId: string;
  seats: SeatConn[] = [];
  table: PokerTable | null = null;
  started = false;
  readonly createdAt = Date.now();
  private readonly startingChips: number;
  private readonly sb: number;
  private readonly bb: number;

  constructor(code: string, hostId: string, opts?: { chips?: number; sb?: number; bb?: number }) {
    this.code = code;
    this.hostId = hostId;
    this.startingChips = opts?.chips ?? 1000;
    this.sb = opts?.sb ?? 5;
    this.bb = opts?.bb ?? 10;
  }

  get size() {
    return this.seats.length;
  }

  add(ws: WebSocket, playerId: string, name: string): { ok: true; seat: number } | { ok: false; error: string } {
    if (this.started) return { ok: false, error: "Игра уже началась" };
    if (this.seats.length >= 6) return { ok: false, error: "Стол полный (макс. 6)" };
    if (this.seats.some((s) => s.playerId === playerId)) return { ok: false, error: "Уже за столом" };
    const seat = this.seats.length;
    this.seats.push({ ws, playerId, seat, name: name.slice(0, 24) || `Игрок ${seat + 1}` });
    return { ok: true, seat };
  }

  removeByWs(ws: WebSocket) {
    const idx = this.seats.findIndex((s) => s.ws === ws);
    if (idx < 0) return;
    if (!this.started) {
      this.seats.splice(idx, 1);
      this.seats.forEach((s, i) => (s.seat = i));
    } else {
      // keep seat, mark disconnected — chips remain, auto-fold when acting
      this.seats[idx].ws = null as unknown as WebSocket;
    }
  }

  start(): string | null {
    if (this.started) return "Уже запущено";
    if (this.seats.length < 2) return "Нужно минимум 2 игрока";
    const players: Player[] = this.seats.map((s) => ({
      seat: s.seat,
      id: s.playerId,
      name: s.name,
      chips: this.startingChips,
      hole: [],
      betStreet: 0,
      totalBet: 0,
      folded: false,
      allIn: false,
      acted: false,
    }));
    this.table = new PokerTable(players, this.sb, this.bb);
    this.started = true;
    this.table.startHand();
    return null;
  }

  nextHand(): string | null {
    if (!this.table) return "Стол не создан";
    if (this.table.street !== "handComplete") return "Раздача ещё идёт";
    this.table.startHand();
    return null;
  }

  action(playerId: string, action: ActionType, amount = 0): string | null {
    if (!this.table) return "Игра не начата";
    const seat = this.seats.find((s) => s.playerId === playerId);
    if (!seat) return "Вы не за столом";
    if (this.table.acting !== seat.seat) return "Сейчас не ваш ход";
    const ok = this.table.apply(seat.seat, action, amount);
    return ok ? null : "Недопустимое действие";
  }

  /** Public state for one viewer (hides others' hole cards). */
  snapshotFor(viewerId: string) {
    const t = this.table;
    const reveal = t && (t.street === "showdown" || t.street === "handComplete");
    return {
      type: "state" as const,
      code: this.code,
      started: this.started,
      hostId: this.hostId,
      you: viewerId,
      lobby: this.seats.map((s) => ({
        seat: s.seat,
        name: s.name,
        id: s.playerId,
        connected: !!(s.ws && s.ws.readyState === 1),
      })),
      table: t
        ? {
            street: t.street,
            pot: t.pot,
            currentBet: t.currentBet,
            dealer: t.dealer,
            sbSeat: t.sbSeat,
            bbSeat: t.bbSeat,
            acting: t.acting,
            handNumber: t.handNumber,
            lastLog: t.lastLog,
            board: t.board.map(cardCode),
            sb: t.sb,
            bb: t.bb,
            pots: t.lastPots,
            players: t.players.map((p) => {
              const showHole = reveal || p.id === viewerId;
              return {
                seat: p.seat,
                id: p.id,
                name: p.name,
                chips: p.chips,
                betStreet: p.betStreet,
                folded: p.folded,
                allIn: p.allIn,
                hole: showHole ? p.hole.map(cardCode) : p.hole.map(() => "??"),
              };
            }),
            legal: t.acting >= 0 && t.players[t.acting]?.id === viewerId ? t.getLegal(t.acting) : null,
          }
        : null,
    };
  }

  broadcast() {
    for (const s of this.seats) {
      if (!s.ws || s.ws.readyState !== 1) continue;
      s.ws.send(JSON.stringify(this.snapshotFor(s.playerId)));
    }
  }
}

export class RoomManager {
  private rooms = new Map<string, Room>();
  private wsRoom = new Map<WebSocket, string>();
  private wsPlayer = new Map<WebSocket, string>();

  create(ws: WebSocket, playerId: string, name: string, publicBase: string) {
    let code = roomCode();
    while (this.rooms.has(code)) code = roomCode();
    const room = new Room(code, playerId);
    this.rooms.set(code, room);
    const joined = room.add(ws, playerId, name);
    if (!joined.ok) return { error: joined.error };
    this.wsRoom.set(ws, code);
    this.wsPlayer.set(ws, playerId);
    return {
      type: "created",
      code,
      seat: joined.seat,
      url: `${publicBase}/?room=${code}`,
      _broadcast: true,
      _room: room,
    };
  }

  join(ws: WebSocket, playerId: string, name: string, code: string) {
    const room = this.rooms.get(code.toUpperCase());
    if (!room) return { error: "Комната не найдена" };
    const joined = room.add(ws, playerId, name);
    if (!joined.ok) return { error: joined.error };
    this.wsRoom.set(ws, room.code);
    this.wsPlayer.set(ws, playerId);
    return { type: "joined", code: room.code, seat: joined.seat, _broadcast: true, _room: room };
  }

  handle(ws: WebSocket, playerId: string, msg: ClientMsg, publicBase: string) {
    if (msg.type === "create") return this.create(ws, playerId, msg.name ?? "Хост", publicBase);
    if (msg.type === "join") {
      if (!msg.room) return { error: "Укажите код комнаты" };
      return this.join(ws, playerId, msg.name ?? "Игрок", msg.room);
    }
    const code = this.wsRoom.get(ws);
    if (!code) return { error: "Сначала войдите в комнату" };
    const room = this.rooms.get(code);
    if (!room) return { error: "Комната исчезла" };

    if (msg.type === "start") {
      if (playerId !== room.hostId) return { error: "Только хост может начать" };
      const err = room.start();
      if (err) return { error: err };
      room.broadcast();
      return { type: "ok" };
    }
    if (msg.type === "next") {
      if (playerId !== room.hostId) return { error: "Только хост" };
      const err = room.nextHand();
      if (err) return { error: err };
      room.broadcast();
      return { type: "ok" };
    }
    if (msg.type === "action") {
      if (!msg.action) return { error: "Нет действия" };
      const err = room.action(playerId, msg.action, msg.amount ?? 0);
      if (err) return { error: err };
      room.broadcast();
      return { type: "ok" };
    }
    if (msg.type === "ping") return { type: "pong" };
    return { error: "Неизвестная команда" };
  }

  disconnect(ws: WebSocket) {
    const code = this.wsRoom.get(ws);
    const playerId = this.wsPlayer.get(ws);
    this.wsRoom.delete(ws);
    this.wsPlayer.delete(ws);
    if (!code) return;
    const room = this.rooms.get(code);
    if (!room) return;
    room.removeByWs(ws);
    if (!room.started && room.size === 0) {
      this.rooms.delete(code);
      return;
    }
    // If host left lobby before start — transfer host
    if (!room.started && playerId === room.hostId && room.seats.length) {
      room.hostId = room.seats[0].playerId;
    }
    room.broadcast();
  }

  get(code: string) {
    return this.rooms.get(code.toUpperCase());
  }
}
