import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const DATA_DIR = path.join(__dirname, "..", "..", "data");
const STORE_PATH = path.join(DATA_DIR, "profiles.json");

export const DEFAULT_RATING = 1000;
export const DEFAULT_COINS = 50_000;
export const ONLINE_BUY_IN = 1_000;
export const MIN_NICK = 3;
export const MAX_NICK = 16;

export interface PlayerProfile {
  playerId: string;
  nickname: string;
  rating: number;
  coins: number;
  matches: number;
  wins: number;
  updatedAt: number;
}

function ensureDir() {
  if (!fs.existsSync(DATA_DIR)) fs.mkdirSync(DATA_DIR, { recursive: true });
}

function normalizeProfile(raw: Partial<PlayerProfile> & { playerId: string }): PlayerProfile {
  return {
    playerId: raw.playerId,
    nickname: raw.nickname || "Игрок",
    rating: typeof raw.rating === "number" ? raw.rating : DEFAULT_RATING,
    coins: typeof raw.coins === "number" ? raw.coins : DEFAULT_COINS,
    matches: typeof raw.matches === "number" ? raw.matches : 0,
    wins: typeof raw.wins === "number" ? raw.wins : 0,
    updatedAt: typeof raw.updatedAt === "number" ? raw.updatedAt : Date.now(),
  };
}

function loadAll(): Record<string, PlayerProfile> {
  ensureDir();
  if (!fs.existsSync(STORE_PATH)) return {};
  try {
    const raw = JSON.parse(fs.readFileSync(STORE_PATH, "utf8"));
    if (!raw || typeof raw !== "object") return {};
    const out: Record<string, PlayerProfile> = {};
    for (const [id, p] of Object.entries(raw)) {
      if (!p || typeof p !== "object") continue;
      out[id] = normalizeProfile({ ...(p as PlayerProfile), playerId: id });
    }
    return out;
  } catch {
    return {};
  }
}

function saveAll(all: Record<string, PlayerProfile>) {
  ensureDir();
  fs.writeFileSync(STORE_PATH, JSON.stringify(all, null, 2), "utf8");
}

export function sanitizeNickname(raw: string | undefined, fallback: string): string {
  let n = (raw ?? "").trim().replace(/\s+/g, " ");
  n = n.replace(/[^\p{L}\p{N}_\- ]/gu, "");
  if (n.length < MIN_NICK) n = fallback;
  if (n.length > MAX_NICK) n = n.slice(0, MAX_NICK);
  return n;
}

export function isValidNickname(raw: string): boolean {
  const n = raw.trim();
  if (n.length < MIN_NICK || n.length > MAX_NICK) return false;
  return /^[\p{L}\p{N}_\- ]+$/u.test(n);
}

export class ProfileStore {
  private cache = loadAll();

  get(playerId: string): PlayerProfile | null {
    return this.cache[playerId] ?? null;
  }

  ensure(playerId: string, nickname?: string): PlayerProfile {
    const existing = this.cache[playerId];
    if (existing) {
      if (nickname && isValidNickname(nickname) && nickname.trim() !== existing.nickname) {
        return this.setNickname(playerId, nickname.trim()) ?? existing;
      }
      return existing;
    }
    const short = playerId.replace(/[^a-zA-Z0-9]/g, "").slice(-4).toUpperCase() || "0000";
    const profile: PlayerProfile = {
      playerId,
      nickname: sanitizeNickname(nickname, `Игрок_${short}`),
      rating: DEFAULT_RATING,
      coins: DEFAULT_COINS,
      matches: 0,
      wins: 0,
      updatedAt: Date.now(),
    };
    this.cache[playerId] = profile;
    this.persist();
    return profile;
  }

  setNickname(playerId: string, nickname: string): PlayerProfile | null {
    if (!isValidNickname(nickname)) return null;
    const p = this.ensure(playerId);
    const taken = Object.values(this.cache).some(
      (o) => o.playerId !== playerId && o.nickname.toLowerCase() === nickname.trim().toLowerCase()
    );
    if (taken) return null;
    p.nickname = nickname.trim();
    p.updatedAt = Date.now();
    this.persist();
    return p;
  }

  /** Списать взнос в очередь. */
  chargeBuyIn(playerId: string, amount = ONLINE_BUY_IN): { ok: true; coins: number } | { ok: false; error: string } {
    const p = this.ensure(playerId);
    if (p.coins < amount) {
      return { ok: false, error: `Недостаточно монет (нужно ${amount}, у вас ${p.coins})` };
    }
    p.coins -= amount;
    p.updatedAt = Date.now();
    this.persist();
    return { ok: true, coins: p.coins };
  }

  /** Вернуть взнос при отмене поиска / disconnect до матча. */
  refundBuyIn(playerId: string, amount = ONLINE_BUY_IN): PlayerProfile {
    const p = this.ensure(playerId);
    p.coins += amount;
    p.updatedAt = Date.now();
    this.persist();
    return p;
  }

  /** Выплата победителю онлайн-матча (взносы уже списаны при постановке в очередь). */
  payoutOnlineWinner(winnerId: string, pool: number): PlayerProfile {
    const p = this.ensure(winnerId);
    p.coins += pool;
    p.updatedAt = Date.now();
    this.persist();
    return p;
  }

  /** Elo-like update after match. Winner gains, others lose a little. */
  applyMatchResult(playerIds: string[], winnerId: string) {
    if (!playerIds.length) return;
    const profiles = playerIds.map((id) => this.ensure(id));
    const avg =
      profiles.reduce((s, p) => s + p.rating, 0) / Math.max(1, profiles.length);
    for (const p of profiles) {
      p.matches += 1;
      const expected = 1 / (1 + Math.pow(10, (avg - p.rating) / 400));
      const score = p.playerId === winnerId ? 1 : 0;
      const k = 24;
      const delta = Math.round(k * (score - expected));
      p.rating = Math.max(100, p.rating + delta);
      if (p.playerId === winnerId) p.wins += 1;
      p.updatedAt = Date.now();
    }
    this.persist();
  }

  leaderboard(limit = 20): PlayerProfile[] {
    return Object.values(this.cache)
      .sort((a, b) => b.rating - a.rating || b.wins - a.wins)
      .slice(0, limit);
  }

  private persist() {
    saveAll(this.cache);
  }
}

export const profiles = new ProfileStore();
