import { randomUUID } from "node:crypto";
import { WebSocket } from "ws";
import { cardCode } from "../poker/cards.js";
import { PokerTable } from "../poker/table.js";
import type { ActionType, Player } from "../poker/table.js";
import { profiles, type PlayerProfile } from "../profile/store.js";
import { MatchQueue, QUEUE_MAX, QUEUE_MIN } from "./queue.js";

export interface ClientMsg {
  type: string;
  name?: string;
  nickname?: string;
  playerId?: string;
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
  rating: number;
}

export class Room {
  readonly code: string;
  hostId: string;
  seats: SeatConn[] = [];
  table: PokerTable | null = null;
  started = false;
  /** Стол из очереди — старт автоматом, без ручного хоста. */
  readonly fromQueue: boolean;
  private ratingApplied = false;
  readonly createdAt = Date.now();
  private readonly startingChips: number;
  private readonly sb: number;
  private readonly bb: number;

  constructor(
    code: string,
    hostId: string,
    opts?: { chips?: number; sb?: number; bb?: number; fromQueue?: boolean }
  ) {
    this.code = code;
    this.hostId = hostId;
    this.startingChips = opts?.chips ?? 1000;
    this.sb = opts?.sb ?? 5;
    this.bb = opts?.bb ?? 10;
    this.fromQueue = !!opts?.fromQueue;
  }

  get size() {
    return this.seats.length;
  }

  add(
    ws: WebSocket,
    playerId: string,
    name: string,
    rating = 1000
  ): { ok: true; seat: number } | { ok: false; error: string } {
    if (this.started) return { ok: false, error: "Игра уже началась" };
    if (this.seats.length >= QUEUE_MAX) return { ok: false, error: `Стол полный (макс. ${QUEUE_MAX})` };
    if (this.seats.some((s) => s.playerId === playerId)) return { ok: false, error: "Уже за столом" };
    const seat = this.seats.length;
    const profile = profiles.ensure(playerId, name);
    this.seats.push({
      ws,
      playerId,
      seat,
      name: (name || profile.nickname).slice(0, 24),
      rating: profile.rating || rating,
    });
    return { ok: true, seat };
  }

  removeByWs(ws: WebSocket) {
    const idx = this.seats.findIndex((s) => s.ws === ws);
    if (idx < 0) return;
    if (!this.started) {
      this.seats.splice(idx, 1);
      this.seats.forEach((s, i) => (s.seat = i));
    } else {
      this.seats[idx].ws = null as unknown as WebSocket;
    }
  }

  start(): string | null {
    if (this.started) return "Уже запущено";
    if (this.seats.length < QUEUE_MIN) return `Нужно минимум ${QUEUE_MIN} игрока`;
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
    this.ratingApplied = false;
    this.table.startHand();
    return null;
  }

  nextHand(): string | null {
    if (!this.table) return "Стол не создан";
    if (this.table.street === "matchComplete") return "Матч окончен — нажмите «Новая партия»";
    if (this.table.street !== "handComplete") return "Раздача ещё идёт";
    this.table.startHand();
    return null;
  }

  rematch(): string | null {
    if (!this.table) return "Стол не создан";
    if (this.table.street !== "matchComplete" && this.table.street !== "handComplete") {
      return "Можно перезапустить после раздачи или конца матча";
    }
    this.ratingApplied = false;
    this.table.restartMatch();
    return null;
  }

  action(playerId: string, action: ActionType, amount = 0): string | null {
    if (!this.table) return "Игра не начата";
    const seat = this.seats.find((s) => s.playerId === playerId);
    if (!seat) return "Вы не за столом";
    if (this.table.acting !== seat.seat) return "Сейчас не ваш ход";
    const prev = this.table.street;
    const ok = this.table.apply(seat.seat, action, amount);
    if (!ok) return "Недопустимое действие";
    this.maybeApplyRating(prev);
    return null;
  }

  /** После next/rematch/start тоже проверяем матч. */
  maybeApplyRating(_prevStreet?: string) {
    const t = this.table;
    if (!t || this.ratingApplied) return;
    if (t.street !== "matchComplete") return;
    const winnerId =
      t.matchWinner >= 0 && t.players[t.matchWinner] ? t.players[t.matchWinner].id : "";
    if (!winnerId) return;
    const ids = t.players.map((p) => p.id);
    profiles.applyMatchResult(ids, winnerId);
    this.ratingApplied = true;
    // refresh seat ratings for UI
    for (const s of this.seats) {
      const p = profiles.get(s.playerId);
      if (p) s.rating = p.rating;
    }
  }

  snapshotFor(viewerId: string) {
    const t = this.table;
    const reveal =
      t && (t.street === "showdown" || t.street === "handComplete" || t.street === "matchComplete");
    return {
      type: "state" as const,
      code: this.code,
      started: this.started,
      hostId: this.hostId,
      fromQueue: this.fromQueue,
      you: viewerId,
      lobby: this.seats.map((s) => {
        const p = profiles.get(s.playerId);
        return {
          seat: s.seat,
          name: s.name,
          id: s.playerId,
          rating: p?.rating ?? s.rating,
          connected: !!(s.ws && s.ws.readyState === 1),
        };
      }),
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
            matchWinner: t.matchWinner,
            players: t.players.map((p) => {
              const showHole = reveal || p.id === viewerId;
              const prof = profiles.get(p.id);
              return {
                seat: p.seat,
                id: p.id,
                name: p.name,
                rating: prof?.rating ?? 1000,
                chips: p.chips,
                betStreet: p.betStreet,
                folded: p.folded,
                allIn: p.allIn,
                eliminated: !!p.eliminated || p.chips <= 0,
                hole: showHole ? p.hole.map(cardCode) : p.hole.map(() => "??"),
              };
            }),
            legal:
              t.acting >= 0 && t.players[t.acting]?.id === viewerId ? t.getLegal(t.acting) : null,
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
  private wsProfile = new Map<WebSocket, PlayerProfile>();
  readonly queue = new MatchQueue();

  bindPlayer(ws: WebSocket, playerId: string, nickname?: string): PlayerProfile {
    const profile = profiles.ensure(playerId, nickname);
    this.wsPlayer.set(ws, profile.playerId);
    this.wsProfile.set(ws, profile);
    return profile;
  }

  getPlayerId(ws: WebSocket): string | null {
    return this.wsPlayer.get(ws) ?? null;
  }

  getProfile(ws: WebSocket): PlayerProfile | null {
    const id = this.wsPlayer.get(ws);
    return id ? profiles.get(id) ?? this.wsProfile.get(ws) ?? null : null;
  }

  create(ws: WebSocket, playerId: string, name: string, publicBase: string) {
    this.queue.dequeue(playerId);
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
    this.queue.dequeue(playerId);
    const room = this.rooms.get(code.toUpperCase());
    if (!room) return { error: "Комната не найдена" };
    const joined = room.add(ws, playerId, name);
    if (!joined.ok) return { error: joined.error };
    this.wsRoom.set(ws, room.code);
    this.wsPlayer.set(ws, playerId);
    return { type: "joined", code: room.code, seat: joined.seat, _broadcast: true, _room: room };
  }

  /** Собирает стол из очереди и сразу стартует. */
  formMatchedTables() {
    const batches = this.queue.tryMatch();
    for (const batch of batches) {
      let code = roomCode();
      while (this.rooms.has(code)) code = roomCode();
      const hostId = batch.players[0].playerId;
      const room = new Room(code, hostId, { fromQueue: true });
      this.rooms.set(code, room);

      for (const e of batch.players) {
        const joined = room.add(e.ws, e.playerId, e.nickname, e.rating);
        if (!joined.ok) continue;
        this.wsRoom.set(e.ws, code);
        this.wsPlayer.set(e.ws, e.playerId);
      }

      const err = room.start();
      if (err) {
        for (const e of batch.players) {
          if (e.ws.readyState === 1) {
            e.ws.send(JSON.stringify({ type: "error", error: err }));
          }
        }
        continue;
      }

      for (const e of batch.players) {
        if (e.ws.readyState !== 1) continue;
        e.ws.send(
          JSON.stringify({
            type: "matched",
            code: room.code,
            players: batch.players.length,
            maxPlayers: QUEUE_MAX,
          })
        );
      }
      room.broadcast();
    }
  }

  broadcastQueueStatuses() {
    for (const [ws, playerId] of this.wsPlayer) {
      if (this.wsRoom.has(ws)) continue;
      const st = this.queue.statusFor(playerId);
      if (!st || ws.readyState !== 1) continue;
      ws.send(JSON.stringify({ type: "queue_status", ...st }));
    }
  }

  handle(ws: WebSocket, playerId: string, msg: ClientMsg, publicBase: string) {
    if (msg.type === "auth") {
      const id = (msg.playerId || playerId).trim();
      if (!id) return { error: "Нужен playerId" };
      const profile = this.bindPlayer(ws, id, msg.nickname ?? msg.name);
      return {
        type: "profile",
        playerId: profile.playerId,
        nickname: profile.nickname,
        rating: profile.rating,
        matches: profile.matches,
        wins: profile.wins,
      };
    }

    if (msg.type === "set_nickname") {
      const nick = (msg.nickname ?? msg.name ?? "").trim();
      const p = profiles.setNickname(playerId, nick);
      if (!p) return { error: "Ник занят или неверный (3–16: буквы, цифры, _ -)" };
      this.wsProfile.set(ws, p);
      return {
        type: "profile",
        playerId: p.playerId,
        nickname: p.nickname,
        rating: p.rating,
        matches: p.matches,
        wins: p.wins,
      };
    }

    if (msg.type === "queue") {
      if (this.wsRoom.has(ws)) return { error: "Сначала выйдите из комнаты" };
      const profile = profiles.ensure(playerId, msg.nickname ?? msg.name);
      const ticketId = randomUUID();
      const enq = this.queue.enqueue({
        ticketId,
        playerId: profile.playerId,
        nickname: profile.nickname,
        rating: profile.rating,
        ws,
      });
      if (!enq.ok) return { error: enq.error };
      this.formMatchedTables();
      this.broadcastQueueStatuses();
      const st = this.queue.statusFor(profile.playerId);
      return { type: "queue_status", ...(st || { ticketId, position: 0, queueSize: 0 }) };
    }

    if (msg.type === "dequeue") {
      this.queue.dequeue(playerId);
      return { type: "queue_left" };
    }

    if (msg.type === "leaderboard") {
      return { type: "leaderboard", entries: profiles.leaderboard(20) };
    }

    if (msg.type === "create") {
      const profile = profiles.ensure(playerId, msg.name);
      return this.create(ws, playerId, profile.nickname, publicBase);
    }
    if (msg.type === "join") {
      if (!msg.room) return { error: "Укажите код комнаты" };
      const profile = profiles.ensure(playerId, msg.name);
      return this.join(ws, playerId, profile.nickname, msg.room);
    }

    const code = this.wsRoom.get(ws);
    if (!code) return { error: "Сначала войдите в комнату или встаньте в очередь" };
    const room = this.rooms.get(code);
    if (!room) return { error: "Комната исчезла" };

    if (msg.type === "start") {
      if (room.fromQueue) return { error: "Матч из очереди стартует сам" };
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
      room.maybeApplyRating();
      room.broadcast();
      return { type: "ok" };
    }
    if (msg.type === "rematch") {
      if (playerId !== room.hostId) return { error: "Только хост" };
      const err = room.rematch();
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

  tickQueue() {
    this.formMatchedTables();
    this.broadcastQueueStatuses();
  }

  disconnect(ws: WebSocket) {
    this.queue.removeByWs(ws);
    const code = this.wsRoom.get(ws);
    const playerId = this.wsPlayer.get(ws);
    this.wsRoom.delete(ws);
    this.wsPlayer.delete(ws);
    this.wsProfile.delete(ws);
    if (!code) return;
    const room = this.rooms.get(code);
    if (!room) return;
    room.removeByWs(ws);
    if (!room.started && room.size === 0) {
      this.rooms.delete(code);
      return;
    }
    if (!room.started && playerId === room.hostId && room.seats.length) {
      room.hostId = room.seats[0].playerId;
    }
    room.broadcast();
  }

  onlineCount() {
    return this.wsPlayer.size;
  }

  get(code: string) {
    return this.rooms.get(code.toUpperCase());
  }
}
