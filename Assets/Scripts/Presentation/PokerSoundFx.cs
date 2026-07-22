using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Poker.Presentation
{
    /// <summary>UI и игровые звуки из Assets/Resources/PokerSounds.</summary>
    public static class PokerSoundFx
    {
        public enum Sfx
        {
            Button,
            Fold,
            Check,
            Call,
            Raise
        }

        const float Volume = 0.82f;

        static AudioSource _source;
        static AudioClip _button;
        static AudioClip _fold;
        static AudioClip _check;
        static AudioClip _call;
        static AudioClip _raise;
        static bool _ready;

        public static void WarmUp()
        {
            if (_ready) return;
            EnsureSource();
            _button = Load("button");
            _fold = Load("fold");
            _check = Load("check");
            _call = Load("call");
            _raise = Load("raise");
            _ready = true;
        }

        static AudioClip Load(string name) => Resources.Load<AudioClip>($"PokerSounds/{name}");

        static void EnsureSource()
        {
            if (_source != null) return;
            var go = new GameObject("PokerSoundFx");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.volume = 1f;
        }

        public static void Play(Sfx sfx)
        {
            WarmUp();
            AudioClip clip = sfx switch
            {
                Sfx.Button => _button,
                Sfx.Fold => _fold,
                Sfx.Check => _check,
                Sfx.Call => _call,
                Sfx.Raise => _raise,
                _ => null
            };
            if (clip != null && _source != null)
                _source.PlayOneShot(clip, Volume);
        }

        public static UnityAction WithButton(UnityAction action)
        {
            return () =>
            {
                Play(Sfx.Button);
                action?.Invoke();
            };
        }

        public static void BindButton(Button btn, UnityAction action)
        {
            if (btn == null) return;
            UiPressAnimation.Attach(btn);
            btn.onClick.AddListener(WithButton(action));
        }
    }
}
