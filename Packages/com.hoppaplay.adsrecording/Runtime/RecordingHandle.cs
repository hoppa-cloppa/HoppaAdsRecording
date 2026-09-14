using UnityEngine;
#if DEVKIT_ENABLED
using System;
using UnityEngine.UI;
#endif

namespace HoppaPlay.AdsRecording
{
    /// <summary>
    /// An invisible tap target that reopens the dev panel while recording.
    ///
    /// Once the UI is hidden there is nothing left to tap, and the game's own
    /// dev button is somewhere in the middle of the action -- reaching it means
    /// tapping the board, which lands in the recording as a phantom move. This
    /// puts a target on the LEFT EDGE instead, a strip of screen a casual game
    /// almost never uses, so the way out is always in the same place and never
    /// on top of gameplay.
    ///
    /// Invisible rather than merely dim: the phone is recording the screen, so
    /// anything drawn here ends up in the finished ad. A fully transparent
    /// Image still receives raycasts, which a disabled one would not.
    ///
    /// Its rectangle is handed to the recorder as an ignore region, so tapping
    /// it to reload a level or stop is never mistaken for gameplay.
    /// </summary>
    public static class RecordingHandle
    {
#if DEVKIT_ENABLED

        /// <summary>
        /// Below DevKit's panel (short.MaxValue) on purpose.
        ///
        /// The panel has to sit above this, or once it is open the strip would
        /// swallow taps along its left edge -- including the actions this
        /// exists to reach.
        /// </summary>
        private const int SortingOrder = short.MaxValue - 100;

        /// <summary>Fraction of the screen the strip covers.</summary>
        private const float WidthFraction = 0.13f;
        private const float HeightFraction = 0.34f;

        private static GameObject _host;
        private static RectTransform _target;
        private static Action _onTap;

        public static bool IsVisible => _host != null && _host.activeSelf;

        /// <summary>
        /// The strip in screen pixels, or an empty rect when hidden.
        ///
        /// Computed from the screen rather than read back from the layout so it
        /// is correct on the very first frame, before the canvas has built.
        /// </summary>
        public static Rect ScreenRect
        {
            get
            {
                if (!IsVisible)
                {
                    return Rect.zero;
                }

                float width = Screen.width * WidthFraction;
                float height = Screen.height * HeightFraction;
                return new Rect(0f, (Screen.height - height) * 0.5f, width, height);
            }
        }

        /// <summary>Shows the strip; tapping it runs <paramref name="onTap"/>.</summary>
        public static void Show(Action onTap)
        {
            _onTap = onTap;
            EnsureHost();
            _host.SetActive(true);
            Layout();
        }

        public static void Hide()
        {
            if (_host != null)
            {
                _host.SetActive(false);
            }

            _onTap = null;
        }

        private static void EnsureHost()
        {
            if (_host != null)
            {
                Layout();
                return;
            }

            // The DevKit prefix is what makes RecordingUiMask leave this
            // alone; masking the way out would strand you mid-recording.
            _host = new GameObject("[DevKit] RecordingHandle");
            // Fully qualified: `using System` is in scope for Action, which
            // makes a bare `Object` ambiguous with System.Object.
            UnityEngine.Object.DontDestroyOnLoad(_host);

            Canvas canvas = _host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            _host.AddComponent<GraphicRaycaster>();

            GameObject target = new GameObject("Target", typeof(RectTransform));
            target.transform.SetParent(_host.transform, false);

            Image image = target.AddComponent<Image>();
            // Fully transparent but still hit-testable. Alpha 0 is the point:
            // the screen is being recorded, so any visible affordance would be
            // burned into the ad.
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;

            Button button = target.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => _onTap?.Invoke());

            _target = image.rectTransform;
            Layout();
        }

        /// <summary>
        /// Anchors the strip to the left edge, vertically centred.
        ///
        /// Anchored in fractions rather than pixels so a rotation or a resize
        /// keeps it in the same place without anything having to re-run.
        /// </summary>
        private static void Layout()
        {
            if (_target == null)
            {
                return;
            }

            _target.anchorMin = new Vector2(0f, 0.5f - HeightFraction * 0.5f);
            _target.anchorMax = new Vector2(WidthFraction, 0.5f + HeightFraction * 0.5f);
            _target.offsetMin = Vector2.zero;
            _target.offsetMax = Vector2.zero;
        }

#else
        /// <summary>No-op outside a dev build.</summary>
        public static void Show(System.Action onTap)
        {
        }

        /// <summary>No-op outside a dev build.</summary>
        public static void Hide()
        {
        }

        public static bool IsVisible => false;
        public static Rect ScreenRect => Rect.zero;
#endif
    }
}
