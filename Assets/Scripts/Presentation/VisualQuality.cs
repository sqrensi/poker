using UnityEngine;

namespace Poker.Presentation
{
    /// <summary>
    /// Runtime visual polish: MSAA, anisotropic filtering, camera flags.
    /// </summary>
    public static class VisualQuality
    {
        static bool _applied;

        public static void Apply(Camera cam = null)
        {
            if (!_applied)
            {
                _applied = true;
                // Prefer highest quality tier that exists.
                int ultra = QualitySettings.names.Length - 1;
                if (ultra >= 0)
                    QualitySettings.SetQualityLevel(ultra, true);

                QualitySettings.antiAliasing = Application.isMobilePlatform ? 2 : 8;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                QualitySettings.softParticles = true;
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.High;
            }

            if (cam == null)
                cam = Camera.main;
            if (cam == null) return;

            cam.allowMSAA = true;
            cam.allowHDR = true;
            cam.depthTextureMode = DepthTextureMode.None;
        }
    }
}
