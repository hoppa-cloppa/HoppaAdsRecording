using UnityEngine;
#if DEVKIT_ENABLED
using System.Collections.Generic;
using UnityEngine.UI;
#endif

namespace HoppaPlay.AdsRecording
{
    /// <summary>
    /// Hides on-screen UI while a recording is running, and puts it back
    /// afterwards.
    ///
    /// Works ON DEVICE, which is the whole point: an editor-only recording
    /// mode does nothing on a phone, and a phone is where the footage comes
    /// from. Disable the visuals, remember what was on, restore it -- with
    /// three details that matter:
    ///
    ///   1. It never draws a hand. The finger is reconstructed later from the
    ///      touch log by the ad editor, so rendering one during capture would
    ///      put two hands in the finished video.
    ///
    ///   2. It skips DevKit's own overlay. That panel is how you stop the
    ///      recording, and hiding it would strand you mid-capture. DevKit names
    ///      its root objects with a known prefix, which is what identifies it.
    ///
    ///   3. The button that opens the dev panel is made TRANSPARENT rather than
    ///      disabled. A disabled Graphic stops raycasting, so the button would
    ///      be untappable; alpha 0 keeps it working while keeping it out of the
    ///      video.
    /// </summary>
    public static class RecordingUiMask
    {
#if DEVKIT_ENABLED

        /// <summary>DevKitScene names every root it creates with this prefix.</summary>
        private const string DevKitNamePrefix = "[DevKit] ";

        /*
          Objects switched off entirely for the duration of a capture.

          Matched BY NAME, which is the part a game integrating this package
          has to know about: these are the names used by the project this was
          extracted from, and a different game will have different ones. Use
          `SetHiddenObjectNames` to replace the list rather than editing it
          here -- hard-coding one game's hierarchy into a shared package is
          exactly the coupling this extraction exists to remove.
        */
        private static readonly HashSet<string> DefaultHiddenObjectNames = new()
        {
            "Canvas (Clone)",
            "MobileQualityManager",
            "DevGameHud"
        };

        private static HashSet<string> _hiddenObjectNames = new(DefaultHiddenObjectNames);

        private static HashSet<string> DisabledObjectNames => _hiddenObjectNames;

        /// <summary>
        /// Replaces the set of GameObject names hidden during a capture.
        /// </summary>
        /// <remarks>
        /// Call once at startup if your game's hierarchy differs from the
        /// defaults. Passing null or an empty set restores the defaults rather
        /// than hiding nothing, because "hide nothing" is almost never what
        /// someone means and silently recording the whole HUD is expensive to
        /// discover -- you only find out after the capture.
        /// </remarks>
        public static void SetHiddenObjectNames(IEnumerable<string> names)
        {
            HashSet<string> replacement = names == null
                ? null
                : new HashSet<string>(names);

            _hiddenObjectNames = replacement is { Count: > 0 }
                ? replacement
                : new HashSet<string>(DefaultHiddenObjectNames);
        }

        private static readonly Dictionary<Graphic, bool> OriginalGraphicStates = new();
        private static readonly Dictionary<Renderer, bool> OriginalRendererStates = new();
        private static readonly Dictionary<GameObject, bool> OriginalObjectStates = new();
        private static readonly Dictionary<Graphic, Color> OriginalColors = new();

        public static bool IsApplied { get; private set; }

        /// <summary>
        /// Hides the UI. <paramref name="keepTappable"/> is left invisible but
        /// still interactive, so the dev panel can be reopened to stop.
        /// </summary>
        public static void Apply(GameObject keepTappable)
        {
            if (IsApplied)
            {
                return;
            }

            Transform keep = keepTappable != null ? keepTappable.transform : null;

            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();

            foreach (Canvas canvas in canvases)
            {
                if (canvas == null ||
                    !IsValidSceneObject(canvas.gameObject) ||
                    IsDevKitOwned(canvas.transform))
                {
                    continue;
                }

                HideGraphics(canvas, keep);
                HideRenderers(canvas);
            }

            DisableNamedObjects();

            IsApplied = true;
        }

        public static void Restore()
        {
            foreach (KeyValuePair<Graphic, Color> pair in OriginalColors)
            {
                if (pair.Key != null)
                {
                    pair.Key.color = pair.Value;
                }
            }

            foreach (KeyValuePair<Graphic, bool> pair in OriginalGraphicStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.enabled = pair.Value;
                }
            }

            foreach (KeyValuePair<Renderer, bool> pair in OriginalRendererStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.enabled = pair.Value;
                }
            }

            foreach (KeyValuePair<GameObject, bool> pair in OriginalObjectStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }

            OriginalColors.Clear();
            OriginalGraphicStates.Clear();
            OriginalRendererStates.Clear();
            OriginalObjectStates.Clear();

            IsApplied = false;
        }

        // ---------------------------------------------------------------------

        private static void HideGraphics(Canvas canvas, Transform keep)
        {
            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);

            foreach (Graphic graphic in graphics)
            {
                if (graphic == null)
                {
                    continue;
                }

                if (IsInside(graphic.transform, keep))
                {
                    // Invisible but still raycasting, so it can still be tapped.
                    if (!OriginalColors.ContainsKey(graphic))
                    {
                        OriginalColors.Add(graphic, graphic.color);
                    }

                    Color transparent = graphic.color;
                    transparent.a = 0f;
                    graphic.color = transparent;
                    continue;
                }

                if (!OriginalGraphicStates.ContainsKey(graphic))
                {
                    OriginalGraphicStates.Add(graphic, graphic.enabled);
                }

                graphic.enabled = false;
            }
        }

        private static void HideRenderers(Canvas canvas)
        {
            Renderer[] renderers = canvas.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!OriginalRendererStates.ContainsKey(renderer))
                {
                    OriginalRendererStates.Add(renderer, renderer.enabled);
                }

                renderer.enabled = false;
            }
        }

        private static void DisableNamedObjects()
        {
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (GameObject obj in all)
            {
                if (obj == null ||
                    !IsValidSceneObject(obj) ||
                    IsDevKitOwned(obj.transform) ||
                    !DisabledObjectNames.Contains(obj.name))
                {
                    continue;
                }

                if (!OriginalObjectStates.ContainsKey(obj))
                {
                    OriginalObjectStates.Add(obj, obj.activeSelf);
                }

                obj.SetActive(false);
            }
        }

        private static bool IsInside(Transform current, Transform ancestor)
        {
            if (ancestor == null || current == null)
            {
                return false;
            }

            return current == ancestor || current.IsChildOf(ancestor);
        }

        /// <summary>
        /// True for anything under a DevKit root. Walking to the root rather
        /// than checking the object itself matters because the panel's contents
        /// are built as children at runtime.
        /// </summary>
        private static bool IsDevKitOwned(Transform transform)
        {
            Transform current = transform;

            while (current != null)
            {
                if (current.name.StartsWith(DevKitNamePrefix))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        /// <summary>
        /// Filters out prefabs and other assets, which FindObjectsOfTypeAll
        /// also returns.
        /// </summary>
        private static bool IsValidSceneObject(GameObject obj)
        {
            return obj != null && obj.scene.IsValid();
        }

#endif
    }
}
