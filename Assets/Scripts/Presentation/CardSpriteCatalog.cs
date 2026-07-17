using System.Collections.Generic;
using UnityEngine;
using Poker.Core;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Poker.Presentation
{
    /// <summary>
    /// Loads face/back sprites from "2D Cards Game Art Pack" (Standard Rounded Cards).
    /// </summary>
    public static class CardSpriteCatalog
    {
        const string PackRoot =
            "Assets/2D Cards Game Art Pack/Sprites/Standard 52 Cards/Standard Rounded Cards";

        static readonly Dictionary<int, Sprite> Faces = new Dictionary<int, Sprite>(52);
        static Sprite _back;
        static bool _loaded;
        static bool _logged;

        public static Sprite Back
        {
            get
            {
                EnsureLoaded();
                return _back;
            }
        }

        public static Sprite GetFace(Card card)
        {
            EnsureLoaded();
            Faces.TryGetValue(Key(card), out var sprite);
            return sprite;
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

#if UNITY_EDITOR
            LoadFromAssetDatabase();
#endif
            if (_back == null || Faces.Count == 0)
                LoadFromResources();

            if (!_logged)
            {
                _logged = true;
                Debug.Log($"[Poker] Card sprites loaded: {Faces.Count}/52 faces, back={(_back != null)}");
            }
        }

#if UNITY_EDITOR
        static void LoadFromAssetDatabase()
        {
            _back = AssetDatabase.LoadAssetAtPath<Sprite>($"{PackRoot}/Card Back/cardBackBlue.png");

            TryLoadSuit(Suit.Clubs, "Clubs", "cardClubs");
            TryLoadSuit(Suit.Diamonds, "Diamonds", "cardDiamonds");
            TryLoadSuit(Suit.Hearts, "Hearts", "cardHearts");
            TryLoadSuit(Suit.Spades, "Spades", "cardSpades");
        }

        static void TryLoadSuit(Suit suit, string folder, string prefix)
        {
            foreach (Rank rank in System.Enum.GetValues(typeof(Rank)))
            {
                string path = $"{PackRoot}/{folder}/{prefix}_{RankToken(rank)}.png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                    Faces[Key(suit, rank)] = sprite;
            }
        }
#endif

        static void LoadFromResources()
        {
            // Optional fallback: Assets/Resources/CardArt/...
            if (_back == null)
                _back = Resources.Load<Sprite>("CardArt/cardBackBlue");

            LoadSuitResources(Suit.Clubs, "cardClubs");
            LoadSuitResources(Suit.Diamonds, "cardDiamonds");
            LoadSuitResources(Suit.Hearts, "cardHearts");
            LoadSuitResources(Suit.Spades, "cardSpades");
        }

        static void LoadSuitResources(Suit suit, string prefix)
        {
            foreach (Rank rank in System.Enum.GetValues(typeof(Rank)))
            {
                int key = Key(suit, rank);
                if (Faces.ContainsKey(key)) continue;
                var sprite = Resources.Load<Sprite>($"CardArt/{prefix}_{RankToken(rank)}");
                if (sprite != null)
                    Faces[key] = sprite;
            }
        }

        static int Key(Card card) => Key(card.Suit, card.Rank);

        static int Key(Suit suit, Rank rank) => ((int)suit << 8) | (int)rank;

        static string RankToken(Rank rank)
        {
            switch (rank)
            {
                case Rank.Ace: return "A";
                case Rank.King: return "K";
                case Rank.Queen: return "Q";
                case Rank.Jack: return "J";
                case Rank.Ten: return "10";
                default: return ((int)rank).ToString();
            }
        }
    }
}
