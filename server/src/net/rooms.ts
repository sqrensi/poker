import { randomUUID } from "node:crypto";
import { WebSocket } from "ws";
import { cardCode } from "../poker/cards.js";
import { PokerTable } from "../poker/table.js";
import type { ActionType, Player } from "../poker/table.js";
import { profiles, type PlayerProfile, ONLINE_BUY_IN } from "../profile/store.js";
import { MatchQueue, QUEUE_MAX, QUEUE_MIN } from "./queue.js";
import { decideBot, isBotId } from "./bot.js";

export interface ClientMsg {
  type: string;
  name?: string;
  nickname?: string;
  playerId?: string;
  localCoins?: number;
  room?: string;
  action?: ActionType;
  amount?: number;
  publicBase?: string;
  count?: number;
}

function roomCode() {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let s = "";
  for (let i = 0; i < 6; i++) s += alphabet[Math.floor(Math.random() * alphabet.length)];
  return s;
}

interface SeatConn {
  ws: WebSocket | null;
  playerId: string;
  seat: number;
  name: string;
  rating: number;
  isBot: boolean;
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
  private economyApplied = false;
  /** Игроки, чей стек уже переведён в монеты (выход или конец матча). */
  private cashedOut = new Set<string>();
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
    ws: WebSocket | null,
    playerId: string,
    name: string,
    rating = 1000,
    isBot = false
  ): { ok: true; seat: number } | { ok: false; error: string } {
    if (this.started) return { ok: false, error: "Игра уже началась" };
    if (this.seats.length >= QUEUE_MAX) return { ok: false, error: `Стол полный (макс. ${QUEUE_MAX})` };
    if (this.seats.some((s) => s.playerId === playerId)) return { ok: false, error: "Уже за столом" };
    const seat = this.seats.length;
    const profile = isBot ? null : profiles.ensure(playerId, name);
    this.seats.push({
      ws,
      playerId,
      seat,
      name: (name || profile?.nickname || "Игрок").slice(0, 24),
      rating: profile?.rating || rating,
      isBot,
    });
    return { ok: true, seat };
  }

  addBot(): { ok: true; name: string } | { ok: false; error: string } {
    if (this.started) return { ok: false, error: "Игра уже началась" };
    if (this.seats.length >= QUEUE_MAX) return { ok: false, error: `Стол полный (макс. ${QUEUE_MAX})` };
    const n = this.seats.filter((s) => s.isBot).length + 1;
    const id = `bot-${randomUUID().slice(0, 8)}`;
    const name = `Бот ${n}`;
    const joined = this.add(null, id, name, 1000, true);
    if (!joined.ok) return { ok: false, error: joined.error };
    return { ok: true, name };
  }

  removeByWs(ws: WebSocket) {
    const idx = this.seats.findIndex((s) => s.ws === ws);
    if (idx < 0) return;
    if (!this.started) {
      this.seats.splice(idx, 1);
      this.seats.forEach((s, i) => (s.seat = i));
    } else {
      this.seats[idx].ws = null;
    }
  }

  /** Переводит текущий стек игрока в монеты (один раз за матч). */
  private cashOutStack(playerId: string, chips: number) {
    if (!this.fromQueue || isBotId(playerId) || this.cashedOut.has(playerId)) return;
    const amount = Math.max(0, Math.floor(chips));
    if (amount <= 0) return;
    profiles.cashOutChips(playerId, amount);
    this.cashedOut.add(playerId);
  }

  forfeitPlayer(playerId: string, reason = "вышел из матча") {
    const conn = this.seats.find((s) => s.playerId === playerId);
    if (!conn) return;
    conn.ws = null;
    if (!this.started || !this.table) return;
    const stack = this.table.players[conn.seat]?.chips ?? 0;
    this.cashOutStack(playerId, stack);
    this.table.forfeit(conn.seat, reason);
    this.settleMatch();
  }

  /** Хост может начать в любой момент при ≥2 местах (люди и/или боты). */
  start(): string | null {
    if (this.started) return "Уже запущено";
    if (this.seats.length < 2) return "Нужно минимум 2 игрока (добавьте друзей или ботов)";
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
    this.economyApplied = false;
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
    this.economyApplied = false;
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
    this.settleMatch();
    return null;
  }

  /** После next/rematch/start тоже проверяем матч. */
  maybeApplyRating(_prevStreet?: string) {
    const t = this.table;
    if (!t || this.ratingApplied) return;
    if (t.street !== "matchComplete") return;
    const winnerId =
      t.matchWinner >= 0 && t.players[t.matchWinner] ? t.players[t.matchWinner].id : "";
    if (!winnerId || isBotId(winnerId)) return;
    const ids = t.players.map((p) => p.id).filter((id) => !isBotId(id));
    if (ids.length < 2) return;
    profiles.applyMatchResult(ids, winnerId);
    this.ratingApplied = true;
    for (const s of this.seats) {
      const p = profiles.get(s.playerId);
      if (p) s.rating = p.rating;
    }
  }

  /** В конце онлайн-матча возвращаем оставшийся стек каждому игроку. */
  maybeApplyEconomy() {
    if (!this.fromQueue || this.economyApplied) return;
    const t = this.table;
    if (!t || t.street !== "matchComplete") return;
    for (const p of t.players) {
      if (isBotId(p.id)) continue;
      this.cashOutStack(p.id, p.chips);
    }
    this.economyApplied = true;
  }

  settleMatch() {
    this.maybeApplyRating();
    this.maybeApplyEconomy();
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
      coins: profiles.get(viewerId)?.coins ?? 0,
      buyIn: ONLINE_BUY_IN,
      lobby: this.seats.map((s) => {
        const p = s.isBot ? null : profiles.get(s.playerId);
        return {
          seat: s.seat,
          name: s.name,
          id: s.playerId,
          rating: p?.rating ?? s.rating,
          connected: s.isBot || !!(s.ws && s.ws.readyState === 1),
          isBot: s.isBot,
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
      if (s.isBot || !s.ws || s.ws.readyState !== 1) continue;
      s.ws.send(JSON.stringify(this.snapshotFor(s.playerId)));
    }
  }

  /** Ход бота, если сейчас его очередь. */
  tryBotAct(): boolean {
    if (!this.started || !this.table) return false;
    const seat = this.table.acting;
    if (seat < 0) return false;
    const conn = this.seats[seat];
    if (!conn?.isBot) return false;
    const d = decideBot(this.table, seat);
    const ok = this.table.apply(seat, d.action, d.amount ?? 0);
    if (!ok) {
      const legal = this.table.getLegal(seat);
      if (legal?.canCheck) this.table.apply(seat, "check");
      else if (legal?.canCall) this.table.apply(seat, "call");
      else if (legal?.canFold) this.table.apply(seat, "fold");
    }
    this.settleMatch();
    return true;
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
    const removed = this.queue.dequeue(playerId);
    if (removed) this.refundQueueEntry(removed);
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

  listOpenRooms() {
    const list: {
      code: string;
      hostName: string;
      players: number;
      maxPlayers: number;
      bots: number;
    }[] = [];
    for (const room of this.rooms.values()) {
      if (room.started) continue;
      const host = room.seats.find((s) => s.playerId === room.hostId);
      list.push({
        code: room.code,
        hostName: host?.name || "Хост",
        players: room.seats.length,
        maxPlayers: QUEUE_MAX,
        bots: room.seats.filter((s) => s.isBot).length,
      });
    }
    list.sort((a, b) => a.code.localeCompare(b.code));
    return list;
  }

  join(ws: WebSocket, playerId: string, name: string, code: string) {
    const removed = this.queue.dequeue(playerId);
    if (removed) this.refundQueueEntry(removed);
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
      const room = new Room(code, hostId, { fromQueue: true, chips: ONLINE_BUY_IN });
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
          if (e.buyInPaid) profiles.refundBuyIn(e.playerId, ONLINE_BUY_IN);
          if (e.ws.readyState === 1) {
            e.ws.send(JSON.stringify({ type: "error", error: err }));
          }
        }
        this.rooms.delete(code);
        continue;
      }

      for (const e of batch.players) {
        if (e.ws.readyState !== 1) continue;
        e.ws.send(
          JSON.stringify({
            type: "matched",
            code: room.code,
            players: batch.players.length,
            maxPlayers: batch.players.length,
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
      ws.send(JSON.stringify({ type: "queue_status", buyIn: ONLINE_BUY_IN, ...st }));
    }
  }

  profilePayload(p: PlayerProfile) {
    return {
      type: "profile" as const,
      playerId: p.playerId,
      nickname: p.nickname,
      rating: p.rating,
      coins: p.coins,
      matches: p.matches,
      wins: p.wins,
      buyIn: ONLINE_BUY_IN,
    };
  }

  refundQueueEntry(entry: { playerId: string; buyInPaid: boolean }) {
    if (!entry.buyInPaid) return;
    profiles.refundBuyIn(entry.playerId, ONLINE_BUY_IN);
  }

  handle(ws: WebSocket, playerId: string, msg: ClientMsg, publicBase: string) {
    if (msg.type === "auth") {
      const id = (msg.playerId || playerId).trim();
      if (!id) return { error: "Нужен playerId" };
      this.bindPlayer(ws, id, msg.nickname ?? msg.name);
      if (typeof msg.localCoins === "number" && Number.isFinite(msg.localCoins)) {
        profiles.applyLocalBalanceHint(id, msg.localCoins);
      }
      const profile = profiles.get(id)!;
      return this.profilePayload(profile);
    }

    if (msg.type === "set_nickname") {
      const nick = (msg.nickname ?? msg.name ?? "").trim();
      const p = profiles.setNickname(playerId, nick);
      if (!p) return { error: "Ник занят или неверный (3–16: буквы, цифры, _ -)" };
      this.wsProfile.set(ws, p);
      return this.profilePayload(p);
    }

    if (msg.type === "queue") {
      if (this.wsRoom.has(ws)) return { error: "Сначала выйдите из комнаты" };
      const profile = profiles.ensure(playerId, msg.nickname ?? msg.name);
      const charge = profiles.chargeBuyIn(profile.playerId, ONLINE_BUY_IN);
      if (!charge.ok) return { error: charge.error };
      const ticketId = randomUUID();
      const enq = this.queue.enqueue({
        ticketId,
        playerId: profile.playerId,
        nickname: profile.nickname,
        rating: profile.rating,
        ws,
        buyInPaid: true,
      });
      if (!enq.ok) {
        profiles.refundBuyIn(profile.playerId, ONLINE_BUY_IN);
        return { error: enq.error };
      }
      this.formMatchedTables();
      this.broadcastQueueStatuses();
      const updated = profiles.get(profile.playerId)!;
      const st = this.queue.statusFor(profile.playerId);
      return {
        type: "queue_status",
        coins: updated.coins,
        buyIn: ONLINE_BUY_IN,
        ...(st || { ticketId, position: 0, queueSize: 0, playersNeeded: QUEUE_MAX }),
      };
    }

    if (msg.type === "dequeue") {
      const removed = this.queue.dequeue(playerId);
      if (removed) this.refundQueueEntry(removed);
      const p = profiles.get(playerId);
      return { type: "queue_left", coins: p?.coins ?? 0 };
    }

    if (msg.type === "leave") {
      return this.leaveRoom(ws, playerId);
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
    if (msg.type === "list_rooms") {
      return { type: "rooms", rooms: this.listOpenRooms() };
    }

    const code = this.wsRoom.get(ws);
    if (!code) return { error: "Сначала войдите в комнату или встаньте в очередь" };
    const room = this.rooms.get(code);
    if (!room) return { error: "Комната исчезла" };

    if (msg.type === "add_bot") {
      if (playerId !== room.hostId) return { error: "Только хост может добавить бота" };
      const n = Math.min(Math.max(1, msg.count ?? 1), QUEUE_MAX - room.size);
      const added: string[] = [];
      for (let i = 0; i < n; i++) {
        const r = room.addBot();
        if (!r.ok) break;
        added.push(r.name);
      }
      if (!added.length) return { error: "Не удалось добавить бота" };
      room.broadcast();
      return { type: "bots_added", names: added, _broadcast: false };
    }

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
      room.settleMatch();
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
    this.tickOnlineAutoHands();
  }

  /** Онлайн-матчи из очереди: следующая раздача автоматически. */
  tickOnlineAutoHands() {
    for (const room of this.rooms.values()) {
      if (!room.fromQueue || !room.started || !room.table) continue;
      if (room.table.street === "handComplete") {
        const err = room.nextHand();
        if (!err) {
          room.settleMatch();
          room.broadcast();
        }
      }
    }
  }

  tickBots() {
    for (const room of this.rooms.values()) {
      if (!room.started) continue;
      if (room.tryBotAct()) room.broadcast();
    }
  }

  disconnect(ws: WebSocket) {
    const removed = this.queue.removeByWs(ws);
    for (const e of removed) this.refundQueueEntry(e);
    const code = this.wsRoom.get(ws);
    const playerId = this.wsPlayer.get(ws);
    this.wsRoom.delete(ws);
    this.wsPlayer.delete(ws);
    this.wsProfile.delete(ws);
    if (!code) return;
    const room = this.rooms.get(code);
    if (!room) return;
    if (room.started && playerId) {
      room.forfeitPlayer(playerId, "отключился");
      room.broadcast();
    } else {
      room.removeByWs(ws);
      const humans = room.seats.filter((s) => !s.isBot);
      if (!room.started && humans.length === 0) {
        this.rooms.delete(code);
        return;
      }
      if (!room.started && playerId === room.hostId && humans.length) {
        room.hostId = humans[0].playerId;
      }
      room.broadcast();
    }
  }

  leaveRoom(ws: WebSocket, playerId: string) {
    const code = this.wsRoom.get(ws);
    if (code) {
      const room = this.rooms.get(code);
      if (room?.started) {
        room.forfeitPlayer(playerId, "вышел из матча");
        room.broadcast();
      } else if (room) {
        room.removeByWs(ws);
      }
    }
    this.wsRoom.delete(ws);
    this.wsPlayer.delete(ws);
    this.wsProfile.delete(ws);
    const p = profiles.get(playerId);
    return { type: "left", coins: p?.coins ?? 0 };
  }

  onlineCount() {
    return this.wsPlayer.size;
  }

  get(code: string) {
    return this.rooms.get(code.toUpperCase());
  }
}
