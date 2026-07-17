import type { WebSocket } from "ws";

export const QUEUE_MIN = 2;
export const QUEUE_MAX = 10;
/** Сколько ждать добора до макс. стола после появления минимума. */
export const QUEUE_FILL_MS = 12_000;

export interface QueueEntry {
  ticketId: string;
  playerId: string;
  nickname: string;
  rating: number;
  ws: WebSocket;
  enqueuedAt: number;
}

export interface MatchedTable {
  players: QueueEntry[];
}

/**
 * Упрощённый FIFO-матчмейкинг под покер:
 * — без worker pool / снапшотов
 * — стол 2–10 человек
 * — при 10 — сразу; при ≥2 — через FILL_MS добора
 */
export class MatchQueue {
  private queue: QueueEntry[] = [];

  get size() {
    return this.queue.length;
  }

  has(playerId: string) {
    return this.queue.some((e) => e.playerId === playerId);
  }

  getTicket(playerId: string) {
    return this.queue.find((e) => e.playerId === playerId) ?? null;
  }

  enqueue(entry: Omit<QueueEntry, "enqueuedAt">): { ok: true } | { ok: false; error: string } {
    if (this.has(entry.playerId)) return { ok: false, error: "Уже в очереди" };
    this.queue.push({ ...entry, enqueuedAt: Date.now() });
    return { ok: true };
  }

  dequeue(playerId: string): boolean {
    const i = this.queue.findIndex((e) => e.playerId === playerId);
    if (i < 0) return false;
    this.queue.splice(i, 1);
    return true;
  }

  removeByWs(ws: WebSocket) {
    this.queue = this.queue.filter((e) => e.ws !== ws);
  }

  /** Достаёт готовые столы (может быть несколько). */
  tryMatch(now = Date.now()): MatchedTable[] {
    const out: MatchedTable[] = [];
    while (this.queue.length >= QUEUE_MIN) {
      const oldest = this.queue[0];
      const waited = now - oldest.enqueuedAt;
      const canFill = this.queue.length >= QUEUE_MAX;
      const timedFill = this.queue.length >= QUEUE_MIN && waited >= QUEUE_FILL_MS;
      if (!canFill && !timedFill) break;

      const take = Math.min(QUEUE_MAX, this.queue.length);
      // Не начинать с 1 — уже гарантировано >= MIN
      const batch = this.queue.splice(0, take);
      out.push({ players: batch });
    }
    return out;
  }

  /** Статус для UI. */
  statusFor(playerId: string) {
    const idx = this.queue.findIndex((e) => e.playerId === playerId);
    if (idx < 0) return null;
    const e = this.queue[idx];
    return {
      ticketId: e.ticketId,
      position: idx + 1,
      queueSize: this.queue.length,
      waitedSec: Math.floor((Date.now() - e.enqueuedAt) / 1000),
      minPlayers: QUEUE_MIN,
      maxPlayers: QUEUE_MAX,
    };
  }

  snapshot() {
    return {
      queueSize: this.queue.length,
      minPlayers: QUEUE_MIN,
      maxPlayers: QUEUE_MAX,
    };
  }
}
