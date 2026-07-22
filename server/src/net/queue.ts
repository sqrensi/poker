import type { WebSocket } from "ws";

/** Онлайн-матч: ровно 4 игрока из очереди. */
export const QUEUE_MIN = 4;
export const QUEUE_MAX = 4;
export const QUEUE_FILL_MS = 0;

export interface QueueEntry {
  ticketId: string;
  playerId: string;
  nickname: string;
  rating: number;
  ws: WebSocket;
  enqueuedAt: number;
  buyInPaid: boolean;
}

export interface MatchedTable {
  players: QueueEntry[];
}

/**
 * FIFO-матчмейкинг: как только в очереди 4 человека — отдельный стол.
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

  dequeue(playerId: string): QueueEntry | null {
    const i = this.queue.findIndex((e) => e.playerId === playerId);
    if (i < 0) return null;
    const [removed] = this.queue.splice(i, 1);
    return removed ?? null;
  }

  removeByWs(ws: WebSocket): QueueEntry[] {
    const removed = this.queue.filter((e) => e.ws === ws);
    this.queue = this.queue.filter((e) => e.ws !== ws);
    return removed;
  }

  /** Достаёт готовые столы (может быть несколько за один тик). */
  tryMatch(_now = Date.now()): MatchedTable[] {
    const out: MatchedTable[] = [];
    while (this.queue.length >= QUEUE_MAX) {
      const batch = this.queue.splice(0, QUEUE_MAX);
      out.push({ players: batch });
    }
    return out;
  }

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
      playersNeeded: Math.max(0, QUEUE_MAX - this.queue.length),
    };
  }

  snapshot() {
    return {
      queueSize: this.queue.length,
      minPlayers: QUEUE_MIN,
      maxPlayers: QUEUE_MAX,
      playersNeeded: Math.max(0, QUEUE_MAX - this.queue.length),
    };
  }
}
