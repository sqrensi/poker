using UnityEngine;

namespace Poker.Presentation
{
    /// <summary>
    /// Только альбом (лево/право). Вертикаль выключена.
    /// В редакторе/симуляторе не ставим AutoRotation — Device Simulator падает на UIOrientation.AutoRotation.
    /// </summary>
    public static class OrientationLock
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;

            if (!(Application.isMobilePlatform || IsNarrowPhone()))
                return;

#if UNITY_EDITOR
            // Simulator: фиксированный ландшафт (кнопки поворота в Game view работают сами)
            Screen.orientation = ScreenOrientation.LandscapeLeft;
#else
            if (Screen.height > Screen.width)
                Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.orientation = ScreenOrientation.AutoRotation;
#endif
        }

        static bool IsNarrowPhone()
        {
            float shortest = Mathf.Min(Screen.width, Screen.height);
            return shortest > 0 && shortest <= 900f;
        }
    }
}
