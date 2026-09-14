using UnityEngine;
#if DEVKIT_ENABLED
using UnityEngine.UI;
#endif

namespace HoppaPlay.AdsRecording
{
    /// <summary>
    /// Flashes the screen white for a few frames when a recording starts.
    ///
    /// This is the landmark that lets the editor align two recordings that were
    /// never started together: the phone's screen recorder captures the flash
    /// as pixels, the tap log notes the moment it was shown, and the difference
    /// between them is exactly how far the taps have to move.
    ///
    /// Deliberately a full-screen white fill rather than a marker or a QR code.
    /// It survives scaling, compression and letterboxing, and the editor can
    /// find it with nothing more than average frame brightness -- no template
    /// matching, no assumptions about where on screen it was drawn.
    /// </summary>
    public static class SyncFlash
    {
#if DEVKIT_ENABLED

        /// <summary>
        /// How long the flash stays up.
        ///
        /// Long enough that a 30fps screen recorder cannot miss it between
        /// frames, short enough to be a blink rather than something an editor
        /// has to trim out. About 4 frames at 60fps.
        /// </summary>
        private const float FlashSeconds = 0.07f;

        /// <summary>
        /// Above every game canvas, and above DevKit's own panel, so nothing
        /// can draw over the flash and dim it.
        /// </summary>
        private const int SortingOrder = short.MaxValue;

        private static GameObject _host;
        private static float _hideAt;

        /// <summary>Shows the flash. Returns immediately; it hides itself.</summary>
        public static void Show()
        {
            EnsureHost();
            _host.SetActive(true);
            _hideAt = Time.realtimeSinceStartup + FlashSeconds;

            SyncFlashRunner.Ensure();
        }

        /// <summary>Called each frame by the runner; hides once the time is up.</summary>
        internal static void Tick()
        {
            if (_host != null &&
                _host.activeSelf &&
                Time.realtimeSinceStartup >= _hideAt)
            {
                _host.SetActive(false);
            }
        }

        private static void EnsureHost()
        {
            if (_host != null)
            {
                return;
            }

            // Named with the DevKit prefix so RecordingUiMask leaves it
            // alone; the mask hides game UI, and hiding the flash would defeat
            // the entire point of it.
            _host = new GameObject("[DevKit] SyncFlash");
            Object.DontDestroyOnLoad(_host);

            Canvas canvas = _host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            GameObject fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(_host.transform, false);

            Image image = fill.AddComponent<Image>();
            image.color = Color.white;
            // Never intercept a touch: the player may be mid-tap when this
            // appears, and swallowing it would lose a real event.
            image.raycastTarget = false;

            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _host.SetActive(false);
        }

#else
        /// <summary>No-op outside a dev build.</summary>
        public static void Show()
        {
        }
#endif
    }

#if DEVKIT_ENABLED
    /// <summary>
    /// Drives <see cref="SyncFlash"/>, which is static and so has no Update
    /// of its own. Created on demand and kept for the session.
    /// </summary>
    internal sealed class SyncFlashRunner : MonoBehaviour
    {
        private static SyncFlashRunner _instance;

        internal static void Ensure()
        {
            if (_instance != null)
            {
                return;
            }

            GameObject host = new GameObject("[DevKit] SyncFlashRunner");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<SyncFlashRunner>();
        }

        private void Update()
        {
            SyncFlash.Tick();
        }
    }
#endif
}
