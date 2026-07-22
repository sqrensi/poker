using System;
using System.Collections.Generic;
using System.Text.Json;
using Poker.Core;

namespace Poker.Network
{
    public sealed class OnlineLegalActions
    {
        public bool CanFold;
        public bool CanCheck;
        public bool CanCall;
        public int CallAmount;
        public bool CanBet;
        public bool CanRaise;
        public int MinRaiseTo;
        public int MaxRaiseTo;
        public int Pot;
        public int CurrentBet;
        public bool HasAny => CanFold || CanCheck || CanCall || CanBet || CanRaise;
    }

    public sealed class OnlineSeatPlayer
    {
        public int Seat;
        public string Id;
        public string Name;
        public int Rating;
        public int Chips;
        public int BetStreet;
        public bool Folded;
        public bool AllIn;
        public bool Eliminated;
        public readonly List<Card> Hole = new List<Card>(2);
        public bool HoleHidden;
    }

    public sealed class OnlineGameState
    {
        public string Code;
        public bool Started;
        public string YouId;
        public int Coins;
        public bool FromQueue;
        public string Street = "";
        public int Pot;
        public int CurrentBet;
        public int Dealer;
        public int SbSeat;
        public int BbSeat;
        public int Acting = -1;
        public int HandNumber;
        public string LastLog = "";
        public int MatchWinner = -1;
        public int BigBlind = 10;
        public readonly List<Card> Board = new List<Card>(5);
        public readonly List<OnlineSeatPlayer> Players = new List<OnlineSeatPlayer>(4);
        public OnlineLegalActions Legal;

        public bool IsMyTurn(OnlineLegalActions legal) => legal != null && legal.HasAny;

        public static bool TryParse(string json, out OnlineGameState state)
        {
            state = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var tp) && tp.GetString() != "state")
                    return false;

                var s = new OnlineGameState
                {
                    Code = GetStr(root, "code"),
                    Started = GetBool(root, "started"),
                    YouId = GetStr(root, "you"),
                    Coins = GetInt(root, "coins"),
                    FromQueue = GetBool(root, "fromQueue"),
                };

                if (root.TryGetProperty("table", out var table) && table.ValueKind == JsonValueKind.Object)
                {
                    s.Street = GetStr(table, "street");
                    s.Pot = GetInt(table, "pot");
                    s.CurrentBet = GetInt(table, "currentBet");
                    s.Dealer = GetInt(table, "dealer");
                    s.SbSeat = GetInt(table, "sbSeat");
                    s.BbSeat = GetInt(table, "bbSeat");
                    s.Acting = GetInt(table, "acting", -1);
                    s.HandNumber = GetInt(table, "handNumber");
                    s.LastLog = GetStr(table, "lastLog");
                    s.MatchWinner = GetInt(table, "matchWinner", -1);
                    s.BigBlind = GetInt(table, "bb", 10);

                    if (table.TryGetProperty("board", out var board) && board.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in board.EnumerateArray())
                        {
                            string code = c.GetString();
                            if (CardParser.TryParse(code, out var card))
                                s.Board.Add(card);
                        }
                    }

                    if (table.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in players.EnumerateArray())
                        {
                            var op = new OnlineSeatPlayer
                            {
                                Seat = GetInt(p, "seat"),
                                Id = GetStr(p, "id"),
                                Name = GetStr(p, "name"),
                                Rating = GetInt(p, "rating"),
                                Chips = GetInt(p, "chips"),
                                BetStreet = GetInt(p, "betStreet"),
                                Folded = GetBool(p, "folded"),
                                AllIn = GetBool(p, "allIn"),
                                Eliminated = GetBool(p, "eliminated"),
                            };
                            if (p.TryGetProperty("hole", out var hole) && hole.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var hc in hole.EnumerateArray())
                                {
                                    string code = hc.GetString();
                                    if (code == "??") { op.HoleHidden = true; continue; }
                                    if (CardParser.TryParse(code, out var card))
                                        op.Hole.Add(card);
                                }
                            }
                            s.Players.Add(op);
                        }
                    }

                    if (table.TryGetProperty("legal", out var legal) && legal.ValueKind == JsonValueKind.Object)
                    {
                        s.Legal = new OnlineLegalActions
                        {
                            CanFold = GetBool(legal, "canFold"),
                            CanCheck = GetBool(legal, "canCheck"),
                            CanCall = GetBool(legal, "canCall"),
                            CallAmount = GetInt(legal, "callAmount"),
                            CanBet = GetBool(legal, "canBet"),
                            CanRaise = GetBool(legal, "canRaise"),
                            MinRaiseTo = GetInt(legal, "minRaiseTo"),
                            MaxRaiseTo = GetInt(legal, "maxRaiseTo"),
                            Pot = GetInt(legal, "pot"),
                            CurrentBet = GetInt(legal, "currentBet"),
                        };
                    }
                }

                state = s;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static string GetStr(JsonElement el, string name, string def = "")
            => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

        static int GetInt(JsonElement el, string name, int def = 0)
            => el.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : def;

        static bool GetBool(JsonElement el, string name, bool def = false)
        {
            if (!el.TryGetProperty(name, out var v)) return def;
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => def
            };
        }
    }

    public sealed class OnlineQueueStatus
    {
        public int QueueSize;
        public int PlayersNeeded;
        public int WaitedSec;
        public int Coins;

        public static bool TryParse(string json, out OnlineQueueStatus st)
        {
            st = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var tp) ? tp.GetString() : "";
                if (type != "queue_status") return false;
                st = new OnlineQueueStatus
                {
                    QueueSize = root.TryGetProperty("queueSize", out var qs) && qs.TryGetInt32(out var q) ? q : 0,
                    PlayersNeeded = root.TryGetProperty("playersNeeded", out var pn) && pn.TryGetInt32(out var n) ? n : 0,
                    WaitedSec = root.TryGetProperty("waitedSec", out var ws) && ws.TryGetInt32(out var w) ? w : 0,
                    Coins = root.TryGetProperty("coins", out var c) && c.TryGetInt32(out var coins) ? coins : 0,
                };
                return true;
            }
            catch { return false; }
        }
    }

    public sealed class OnlineProfile
    {
        public string PlayerId;
        public string Nickname;
        public int Rating;
        public int Coins;

        public static bool TryParse(string json, out OnlineProfile p)
        {
            p = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var tp) && tp.GetString() != "profile")
                    return false;
                p = new OnlineProfile
                {
                    PlayerId = root.TryGetProperty("playerId", out var id) ? id.GetString() : "",
                    Nickname = root.TryGetProperty("nickname", out var n) ? n.GetString() : "",
                    Rating = root.TryGetProperty("rating", out var r) && r.TryGetInt32(out var rv) ? rv : 1000,
                    Coins = root.TryGetProperty("coins", out var c) && c.TryGetInt32(out var cv) ? cv : 0,
                };
                return true;
            }
            catch { return false; }
        }
    }
}
