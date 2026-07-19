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
                bool mobile = Application.isMobilePlatform;

                if (!mobile)
                {
                    int ultra = QualitySettings.names.Length - 1;
                    if (ultra >= 0)
                        QualitySettings.SetQualityLevel(ultra, true);
                }
                else if (QualitySettings.names.Length > 0)
                {
                    // На телефоне — средний/низкий пресет, не Ultra
                    int mid = Mathf.Clamp(QualitySettings.names.Length / 2, 0, QualitySettings.names.Length - 1);
                    QualitySettings.SetQualityLevel(mid, true);
                }

                QualitySettings.antiAliasing = mobile ? 2 : 8;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                QualitySettings.softParticles = !mobile;
                QualitySettings.shadows = mobile ? ShadowQuality.Disable : ShadowQuality.All;
                QualitySettings.shadowResolution = mobile ? ShadowResolution.Low : ShadowResolution.High;
            }

            if (cam == null)
                cam = Camera.main;
            if (cam == null) return;

            cam.allowMSAA = true;
            cam.allowHDR = !Application.isMobilePlatform;
            cam.depthTextureMode = DepthTextureMode.None;
        }
    }
}
