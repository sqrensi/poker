using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.Game
{
    public sealed class PotResult
    {
        public int Amount { get; set; }
        public List<int> WinnerSeats { get; } = new List<int>();
        public string HandDescription { get; set; }
    }

    public sealed class HandResult
    {
        public List<PotResult> Pots { get; } = new List<PotResult>();
        public Dictionary<int, HandValue> ShowdownHands { get; } = new Dictionary<int, HandValue>();
    }

    /// <summary>Local Texas Hold'em — blinds, streets, betting, side pots, showdown.</summary>
    public sealed class PokerTable
    {
        public IReadOnlyList<Player> Players => _players;
        public IReadOnlyList<Card> Board => _board;
        public Street Street { get; private set; } = Street.Waiting;
        public int Pot { get; private set; }
        public int CurrentBet { get; private set; }
        public int DealerSeat { get; private set; }
        public int SmallBlindSeat { get; private set; }
        public int BigBlindSeat { get; private set; }
        public int ActingSeat { get; private set; } = -1;
        public int SmallBlind { get; }
        public int BigBlind { get; }
        public int HandNumber { get; private set; }
        public HandResult LastResult { get; private set; }
        public string LastActionLog { get; private set; } = "";
        /// <summary>Seat of match winner when Street == MatchComplete; otherwise -1.</summary>
        public int MatchWinnerSeat { get; private set; } = -1;
        public bool IsMatchOver => Street == Street.MatchComplete;

        public bool AwaitingHumanAction =>
            Street >= Street.Preflop && Street <= Street.River &&
            ActingSeat >= 0 &&
            _players[ActingSeat].Type == PlayerType.Human &&
            _players[ActingSeat].CanAct;

        readonly List<Player> _players;
        readonly List<Card> _board = new List<Card>(5);
        readonly Deck _deck;
        readonly int _startingChips;
        int _raiseSize;

        public event Action StateChanged;
        public event Action HandEnded;
        public event Action MatchEnded;

        public PokerTable(IEnumerable<Player> players, int smallBlind = 5, int bigBlind = 10, int? deckSeed = null)
        {
            _players = new List<Player>(players);
            if (_players.Count < 2 || _players.Count > 10)
                throw new ArgumentException("Need 2–10 players.");
            SmallBlind = smallBlind;
            BigBlind = bigBlind;
            _raiseSize = bigBlind;
            _deck = new Deck(deckSeed);
            DealerSeat = _players.Count - 1;
            _startingChips = _players.Count > 0 ? _players[0].Chips : 1000;
        }

        public void StartNewHand()
        {
            if (Street == Street.MatchComplete)
            {
                LastActionLog = "Матч уже окончен. Начните новую партию.";
                Notify();
                return;
            }

            MarkEliminations();
            if (CountWithChips() < 2)
            {
                FinishMatch("Недостаточно игроков с фишками.");
                return;
            }

            HandNumber++;
            LastResult = null;
            _board.Clear();
            Pot = 0;
            CurrentBet = 0;
            _raiseSize = BigBlind;
            LastActionLog = $"Раздача №{HandNumber}";
            ActingSeat = -1;

            foreach (var p in _players)
            {
                if (p.IsEliminated || p.Chips <= 0)
                {
                    p.IsEliminated = true;
                    p.HoleCards.Clear();
                    p.BetThisStreet = 0;
                    p.TotalBetThisHand = 0;
                    p.HasFolded = true;
                    p.IsAllIn = false;
                    p.HasActedThisStreet = false;
                    continue;
                }
                p.ResetForNewHand();
            }

            DealerSeat = NextSeatWithChips(DealerSeat);
            int live = CountWithChips();

            if (live == 2)
            {
                SmallBlindSeat = DealerSeat;
                BigBlindSeat = NextSeatWithChips(DealerSeat);
            }
            else
            {
                SmallBlindSeat = NextSeatWithChips(DealerSeat);
                BigBlindSeat = NextSeatWithChips(SmallBlindSeat);
            }

            _deck.Reset();
            _deck.Shuffle();

            PostBlind(SmallBlindSeat, SmallBlind, "SB");
            PostBlind(BigBlindSeat, BigBlind, "BB");
            CurrentBet = Max(_players[SmallBlindSeat].BetThisStreet, _players[BigBlindSeat].BetThisStreet);

            // Deal 2 hole cards starting left of dealer
            for (int round = 0; round < 2; round++)
            {
                int seat = DealerSeat;
                for (int i = 0; i < live; i++)
                {
                    seat = NextSeatWithChips(seat);
                    _players[seat].HoleCards.Add(_deck.Deal());
                }
            }

            Street = Street.Preflop;
            ActingSeat = NextCanAct(BigBlindSeat);

            // Если все в олл-ине на блайндах — сразу к доске
            if (ActingSeat < 0 || CountCanAct() <= 1)
            {
                if (CountInHand() <= 1)
                    AwardUncontested();
                else
                    RunOutToShowdown();
                return;
            }

            Notify();
        }

        /// <summary>Сброс фишек и новая партия с теми же игроками.</summary>
        public void RestartMatch(int? startingChips = null)
        {
            int chips = startingChips ?? _startingChips;
            foreach (var p in _players)
            {
                p.Chips = chips;
                p.IsEliminated = false;
                p.ResetForNewHand();
            }
            HandNumber = 0;
            MatchWinnerSeat = -1;
            LastResult = null;
            Pot = 0;
            CurrentBet = 0;
            ActingSeat = -1;
            _board.Clear();
            DealerSeat = _players.Count - 1;
            Street = Street.Waiting;
            LastActionLog = "Новая партия";
            StartNewHand();
        }

        public LegalActions GetLegalActions(int seat)
        {
            if (seat != ActingSeat || seat < 0)
                return default;

            var p = _players[seat];
            if (!p.CanAct)
                return default;

            int toCall = Max(0, CurrentBet - p.BetThisStreet);
            bool canCheck = toCall == 0;
            bool canCall = toCall > 0;
            int callAmount = Min(toCall, p.Chips);
            int maxRaiseTo = p.BetThisStreet + p.Chips;

            if (CurrentBet == 0)
            {
                int minBet = Min(BigBlind, maxRaiseTo);
                return new LegalActions(
                    canFold: false,
                    canCheck: true,
                    canCall: false,
                    callAmount: 0,
                    canBet: maxRaiseTo > 0,
                    canRaise: false,
                    minRaiseTo: minBet,
                    maxRaiseTo: maxRaiseTo,
                    pot: Pot,
                    currentBet: CurrentBet);
            }

            int minRaiseTo = CurrentBet + _raiseSize;
            if (minRaiseTo > maxRaiseTo)
                minRaiseTo = maxRaiseTo;

            return new LegalActions(
                canFold: true,
                canCheck: canCheck,
                canCall: canCall,
                callAmount: callAmount,
                canBet: false,
                canRaise: p.Chips > toCall,
                minRaiseTo: minRaiseTo,
                maxRaiseTo: maxRaiseTo,
                pot: Pot,
                currentBet: CurrentBet);
        }

        static int Min(int a, int b) => a < b ? a : b;
        static int Max(int a, int b) => a > b ? a : b;
        static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        public bool TryApplyAction(int seat, PlayerAction action)
        {
            if (Street < Street.Preflop || Street > Street.River)
                return false;
            if (seat != ActingSeat)
                return false;

            var p = _players[seat];
            if (!p.CanAct)
                return false;

            var legal = GetLegalActions(seat);

            switch (action.Type)
            {
                case ActionType.Fold:
                    if (legal.CanCheck) return false;
                    p.HasFolded = true;
                    p.HasActedThisStreet = true;
                    LastActionLog = $"{p.Name}: фолд";
                    break;

                case ActionType.Check:
                    if (!legal.CanCheck) return false;
                    p.HasActedThisStreet = true;
                    LastActionLog = $"{p.Name}: чек";
                    break;

                case ActionType.Call:
                    if (!legal.CanCall) return false;
                    {
                        int pay = legal.CallAmount;
                        Pot += p.CommitChips(pay);
                        p.HasActedThisStreet = true;
                        LastActionLog = p.IsAllIn
                            ? $"{p.Name}: колл олл-ин ({pay})"
                            : $"{p.Name}: колл {pay}";
                    }
                    break;

                case ActionType.Bet:
                case ActionType.Raise:
                case ActionType.AllIn:
                    if (!ApplyBetOrRaise(p, seat, action, legal))
                        return false;
                    break;

                default:
                    return false;
            }

            AdvanceAfterAction();
            Notify();
            return true;
        }

        bool ApplyBetOrRaise(Player p, int seat, PlayerAction action, LegalActions legal)
        {
            int raiseTo = action.Type == ActionType.AllIn
                ? p.BetThisStreet + p.Chips
                : action.Amount;

            if (CurrentBet == 0)
            {
                if (p.Chips <= 0) return false;
                raiseTo = action.Type == ActionType.AllIn
                    ? p.BetThisStreet + p.Chips
                    : Clamp(raiseTo, legal.MinRaiseTo, legal.MaxRaiseTo);

                int prev = CurrentBet;
                int pay = raiseTo - p.BetThisStreet;
                if (pay <= 0) return false;
                Pot += p.CommitChips(pay);
                _raiseSize = Max(BigBlind, p.BetThisStreet - prev);
                CurrentBet = p.BetThisStreet;
                ReopenAction(seat);
                p.HasActedThisStreet = true;
                LastActionLog = p.IsAllIn
                    ? $"{p.Name}: бет олл-ин ({CurrentBet})"
                    : $"{p.Name}: бет {CurrentBet}";
                return true;
            }

            // Raise / all-in
            int maxTo = legal.MaxRaiseTo;
            if (action.Type == ActionType.AllIn)
                raiseTo = maxTo;
            else
            {
                if (!legal.CanRaise && raiseTo < maxTo) return false;
                raiseTo = Clamp(raiseTo, Min(legal.MinRaiseTo, maxTo), maxTo);
            }

            int prevBet = CurrentBet;
            int need = raiseTo - p.BetThisStreet;
            if (need <= 0) return false;

            Pot += p.CommitChips(need);
            int newBet = p.BetThisStreet;

            if (newBet > prevBet)
            {
                int raisedBy = newBet - prevBet;
                bool fullRaise = raisedBy >= _raiseSize;
                CurrentBet = newBet;
                if (fullRaise)
                {
                    _raiseSize = raisedBy;
                    ReopenAction(seat);
                }
                else
                {
                    foreach (var o in _players)
                    {
                        if (o.SeatIndex == seat) continue;
                        if (!o.CanAct) continue;
                        if (o.BetThisStreet < CurrentBet)
                            o.HasActedThisStreet = false;
                    }
                }
            }

            p.HasActedThisStreet = true;
            LastActionLog = p.IsAllIn
                ? $"{p.Name}: рейз олл-ин до {newBet}"
                : $"{p.Name}: рейз до {newBet}";
            return true;
        }

        void AdvanceAfterAction()
        {
            if (CountInHand() == 1)
            {
                AwardUncontested();
                return;
            }

            if (BettingRoundDone())
            {
                GoNextStreet();
                return;
            }

            ActingSeat = NextCanAct(ActingSeat);
            if (ActingSeat < 0)
                GoNextStreet();
        }

        bool BettingRoundDone()
        {
            foreach (var p in _players)
            {
                if (p.HasFolded || p.IsAllIn) continue;
                if (p.Chips <= 0) continue;
                if (!p.HasActedThisStreet) return false;
                if (p.BetThisStreet != CurrentBet) return false;
            }
            return true;
        }

        bool OnlyOneCanActAndMatched()
        {
            int actors = 0;
            foreach (var p in _players)
            {
                if (p.CanAct) actors++;
            }
            return actors <= 1 && BettingRoundDone();
        }

        void GoNextStreet()
        {
            foreach (var p in _players)
                p.ResetStreet();
            CurrentBet = 0;
            _raiseSize = BigBlind;
            ActingSeat = -1;

            switch (Street)
            {
                case Street.Preflop:
                    DealBurnAndCommunity(3);
                    Street = Street.Flop;
                    LastActionLog = "Открыт флоп";
                    break;
                case Street.Flop:
                    DealBurnAndCommunity(1);
                    Street = Street.Turn;
                    LastActionLog = "Открыт тёрн";
                    break;
                case Street.Turn:
                    DealBurnAndCommunity(1);
                    Street = Street.River;
                    LastActionLog = "Открыт ривер";
                    break;
                case Street.River:
                    DoShowdown();
                    return;
            }

            if (CountCanAct() <= 1)
            {
                RunOutToShowdown();
                return;
            }

            ActingSeat = NextCanAct(DealerSeat);
            if (ActingSeat < 0)
                RunOutToShowdown();
        }

        void DealBurnAndCommunity(int count)
        {
            _deck.Burn();
            for (int i = 0; i < count; i++)
                _board.Add(_deck.Deal());
        }

        void RunOutToShowdown()
        {
            while (_board.Count < 3)
            {
                _deck.Burn();
                _board.Add(_deck.Deal());
            }
            while (_board.Count < 5)
            {
                _deck.Burn();
                _board.Add(_deck.Deal());
            }
            DoShowdown();
        }

        void AwardUncontested()
        {
            Player winner = null;
            foreach (var p in _players)
            {
                if (!p.HasFolded)
                {
                    winner = p;
                    break;
                }
            }
            if (winner == null) return;

            winner.Chips += Pot;
            LastResult = new HandResult();
            var pot = new PotResult { Amount = Pot, HandDescription = "Без вскрытия" };
            pot.WinnerSeats.Add(winner.SeatIndex);
            LastResult.Pots.Add(pot);
            LastActionLog = $"{winner.Name} забирает банк {Pot}";
            Pot = 0;
            Street = Street.HandComplete;
            ActingSeat = -1;
            AfterHandSettled();
            HandEnded?.Invoke();
        }

        void DoShowdown()
        {
            Street = Street.Showdown;
            var contenders = new List<Player>();
            foreach (var p in _players)
            {
                if (!p.HasFolded)
                    contenders.Add(p);
            }

            var result = new HandResult();
            foreach (var p in contenders)
            {
                var seven = new List<Card>(7);
                seven.AddRange(p.HoleCards);
                seven.AddRange(_board);
                result.ShowdownHands[p.SeatIndex] = HandEvaluator.EvaluateBest(seven);
            }

            DistributeSidePots(contenders, result);
            LastResult = result;
            Street = Street.HandComplete;
            ActingSeat = -1;
            LastActionLog = FormatShowdownLog(result);
            AfterHandSettled();
            HandEnded?.Invoke();
            Notify();
        }

        void AfterHandSettled()
        {
            MarkEliminations();
            if (CountWithChips() < 2)
                FinishMatch(null);
        }

        void MarkEliminations()
        {
            foreach (var p in _players)
            {
                if (p.Chips <= 0)
                    p.IsEliminated = true;
            }
        }

        void FinishMatch(string reasonPrefix)
        {
            MarkEliminations();
            Player winner = null;
            foreach (var p in _players)
            {
                if (p.Chips <= 0) continue;
                if (winner == null || p.Chips > winner.Chips)
                    winner = p;
            }

            // Если все на нуле (редкий сплит) — победитель по имени/индексу с макс. (0)
            if (winner == null)
            {
                foreach (var p in _players)
                {
                    if (winner == null || p.Chips > winner.Chips)
                        winner = p;
                }
            }

            Street = Street.MatchComplete;
            ActingSeat = -1;
            MatchWinnerSeat = winner != null ? winner.SeatIndex : -1;

            string who = winner != null
                ? (winner.Type == PlayerType.Human ? "Вы выиграли матч!" : $"{winner.Name} выигрывает матч!")
                : "Матч окончен";
            LastActionLog = string.IsNullOrEmpty(reasonPrefix) ? who : $"{reasonPrefix} {who}";
            MatchEnded?.Invoke();
            Notify();
        }

        static string FormatShowdownLog(HandResult result)
        {
            if (result.Pots.Count == 0) return "Вскрытие";
            var p = result.Pots[0];
            return $"Банк {p.Amount} → места [{string.Join(",", p.WinnerSeats)}] ({p.HandDescription})";
        }

        void DistributeSidePots(List<Player> contenders, HandResult result)
        {
            var levels = new SortedSet<int>();
            foreach (var p in _players)
            {
                if (p.TotalBetThisHand > 0)
                    levels.Add(p.TotalBetThisHand);
            }

            int prev = 0;
            foreach (int level in levels)
            {
                int layer = level - prev;
                if (layer <= 0) { prev = level; continue; }

                int contributors = 0;
                foreach (var p in _players)
                {
                    if (p.TotalBetThisHand >= level)
                        contributors++;
                }

                int potAmount = layer * contributors;
                if (potAmount <= 0) { prev = level; continue; }

                var eligible = new List<Player>();
                foreach (var p in contenders)
                {
                    if (p.TotalBetThisHand >= level)
                        eligible.Add(p);
                }
                if (eligible.Count == 0)
                    eligible.AddRange(contenders);

                HandValue best = result.ShowdownHands[eligible[0].SeatIndex];
                foreach (var p in eligible)
                {
                    var hv = result.ShowdownHands[p.SeatIndex];
                    if (hv.CompareTo(best) > 0) best = hv;
                }

                var winners = new List<Player>();
                foreach (var p in eligible)
                {
                    if (result.ShowdownHands[p.SeatIndex].CompareTo(best) == 0)
                        winners.Add(p);
                }

                int share = potAmount / winners.Count;
                int rem = potAmount % winners.Count;
                var potResult = new PotResult { Amount = potAmount, HandDescription = best.Description };
                for (int i = 0; i < winners.Count; i++)
                {
                    int gain = share + (i < rem ? 1 : 0);
                    winners[i].Chips += gain;
                    potResult.WinnerSeats.Add(winners[i].SeatIndex);
                }
                result.Pots.Add(potResult);
                prev = level;
            }

            Pot = 0;
        }

        void PostBlind(int seat, int amount, string label)
        {
            var p = _players[seat];
            int paid = p.CommitChips(amount);
            Pot += paid;
            string blindName = label == "SB" ? "МБ" : label == "BB" ? "ББ" : label;
            LastActionLog = $"{p.Name} ставит {blindName} {paid}";
        }

        void ReopenAction(int aggressorSeat)
        {
            foreach (var p in _players)
            {
                if (p.SeatIndex == aggressorSeat) continue;
                if (p.CanAct)
                    p.HasActedThisStreet = false;
            }
        }

        int CountInHand()
        {
            int n = 0;
            foreach (var p in _players)
                if (!p.HasFolded) n++;
            return n;
        }

        int CountWithChips()
        {
            int n = 0;
            foreach (var p in _players)
                if (p.Chips > 0) n++;
            return n;
        }

        int CountCanAct()
        {
            int n = 0;
            foreach (var p in _players)
                if (p.CanAct) n++;
            return n;
        }

        int NextSeatWithChips(int from)
        {
            int s = from;
            for (int i = 0; i < _players.Count; i++)
            {
                s = (s + 1) % _players.Count;
                if (_players[s].Chips > 0 && !_players[s].IsEliminated)
                    return s;
            }
            return from;
        }

        int NextCanAct(int from)
        {
            int s = from;
            for (int i = 0; i < _players.Count; i++)
            {
                s = (s + 1) % _players.Count;
                if (_players[s].CanAct) return s;
            }
            return -1;
        }

        void Notify() => StateChanged?.Invoke();
    }
}
