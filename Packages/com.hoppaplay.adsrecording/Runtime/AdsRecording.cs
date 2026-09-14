using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoppaPlay.AdsRecording
{
    /// <summary>
    /// The whole package, from a game's point of view.
    /// </summary>
    /// <remarks>
    /// Everything else in here is implementation. A game should not need to
    /// know that a capture is four cooperating pieces -- a recorder, a screen
    /// mask, a drag handle and a sync flash -- only that it starts, stops, and
    /// produces a file.
    ///
    /// That is not cosmetic. The pieces have to be driven in a specific ORDER
    /// and every exit has to run the same teardown, or a tester is left with
    /// the UI hidden and no way to get it back. Before this facade that
    /// sequence lived in the game's own developer component, which meant the
    /// next game to adopt recording would have had to reproduce it correctly
    /// from reading someone else's code.
    ///
    /// REQUIRES DEVKIT. Without the DEVKIT_ENABLED define every method here is
    /// a no-op that logs once, so a production build neither records nor fails
    /// to compile. See the README.
    /// </remarks>
    public static class AdsRecording
    {
        private static TouchRecorder _recorder;

        /// <summary>Whether a capture is running right now.</summary>
        public static bool IsRecording =>
#if DEVKIT_ENABLED
            _recorder != null && _recorder.IsRecording;
#else
            false;
#endif

        /// <summary>Touch events captured so far.</summary>
        public static int EventCount =>
#if DEVKIT_ENABLED
            _recorder != null ? _recorder.EventCount : 0;
#else
            0;
#endif

        /// <summary>Seconds since the capture began, or 0.</summary>
        public static double ElapsedSeconds =>
#if DEVKIT_ENABLED
            _recorder != null ? _recorder.ElapsedSeconds : 0d;
#else
            0d;
#endif

        /// <summary>
        /// Begins a capture: flashes to mark the start, hides the UI, and
        /// starts logging touches.
        /// </summary>
        /// <param name="openDevPanel">
        /// Invoked when the on-screen handle is tapped. This is the ONLY way
        /// back once the UI is hidden, so a game that passes null strands its
        /// tester mid-capture.
        /// </param>
        /// <param name="keepTappable">
        /// Left invisible but still interactive -- a second way back in. A
        /// disabled Graphic stops raycasting, so this one is made transparent
        /// instead.
        /// </param>
        /// <param name="ignoreRects">
        /// Screen areas whose touches are NOT gameplay and must not reach the
        /// data. The handle's own rect is added automatically; pass anything
        /// else that is chrome rather than game.
        /// </param>
        public static void Start(
            Action openDevPanel,
            GameObject keepTappable = null,
            params Rect[] ignoreRects)
        {
#if DEVKIT_ENABLED
            if (IsRecording) return;

            TouchRecorder recorder = Recorder;

            // Shown BEFORE the rects are collected: its own screen rect is one
            // of them, and it does not have one until it exists.
            if (openDevPanel != null) RecordingHandle.Show(openDevPanel);

            Rect[] ignored = new Rect[(ignoreRects?.Length ?? 0) + 1];
            if (ignoreRects != null) Array.Copy(ignoreRects, ignored, ignoreRects.Length);
            ignored[ignored.Length - 1] = RecordingHandle.ScreenRect;
            recorder.SetIgnoreRects(ignored);

            // Flashes from inside StartRecording, so the white frame and the
            // first logged event share a clock. The editor finds the start of a
            // take by that flash; marking it from out here would put the two a
            // frame or two apart and shift every tap by the difference.
            recorder.StartRecording();

            RecordingUiMask.Apply(keepTappable);
#else
            WarnDisabled();
#endif
        }

        /// <summary>
        /// Ends a capture and puts the screen back, keeping the data.
        /// </summary>
        /// <remarks>
        /// Every exit runs through here so none of them can forget a step.
        /// It also FLASHES, which is what lets the editor find the END of a
        /// take: without it, the tester's trip back into the dev panel to stop
        /// and share was left on the end of every recording and had to be
        /// trimmed by hand.
        /// </remarks>
        public static void Stop()
        {
#if DEVKIT_ENABLED
            if (_recorder == null) return;

            // Only when a take is genuinely ending, and BEFORE Restore() so the
            // flash covers the frames in which the UI reappears rather than
            // following them. Flashing again on a second Stop would leave a
            // later mark that the editor would read as the end of the take.
            if (_recorder.IsRecording) SyncFlash.Show();

            _recorder.StopRecording();
            RecordingHandle.Hide();
            RecordingUiMask.Restore();
#else
            WarnDisabled();
#endif
        }

        /// <summary>
        /// Stops, writes the JSON to a real file, and opens the share sheet.
        /// </summary>
        /// <remarks>
        /// The route that needs neither a cable nor a server: the file lands in
        /// Downloads and goes out in the same message as the screen recording.
        /// Saved BEFORE the sheet opens, so dismissing the sheet does not lose
        /// the take.
        /// </remarks>
        public static RecordingShare.Result StopAndShare(string fileName = "taps.json")
        {
#if DEVKIT_ENABLED
            if (_recorder == null || (!_recorder.IsRecording && _recorder.EventCount == 0))
            {
                return default;
            }

            string json = _recorder.ToJson();
            Stop();
            return RecordingShare.SaveAndShare(json, fileName);
#else
            WarnDisabled();
            return default;
#endif
        }

        /// <summary>
        /// Names the GameObjects hidden during a capture, replacing the
        /// defaults.
        /// </summary>
        /// <remarks>
        /// Call once at startup. The defaults are the HUD names of the project
        /// this package was extracted from, so any other game almost certainly
        /// wants its own.
        ///
        /// Exposed HERE rather than on <see cref="RecordingUiMask"/> so it can
        /// be called unconditionally: the mask itself only exists with DevKit,
        /// and a game should not have to wrap its own startup code in
        /// <c>#if DEVKIT_ENABLED</c> to configure a package it installed.
        /// </remarks>
        public static void SetHiddenObjectNames(IEnumerable<string> names)
        {
#if DEVKIT_ENABLED
            RecordingUiMask.SetHiddenObjectNames(names);
#endif
        }

        /// <summary>Ends the capture and throws the data away.</summary>
        public static void Discard()
        {
#if DEVKIT_ENABLED
            Stop();
            if (_recorder != null) Destroy(_recorder);
#else
            WarnDisabled();
#endif
        }

        /// <summary>
        /// The capture as JSON, in the format the Hoppa ad editor reads.
        /// </summary>
        public static string ToJson() =>
#if DEVKIT_ENABLED
            _recorder != null ? _recorder.ToJson() : string.Empty;
#else
            string.Empty;
#endif

        /// <summary>A one-line status, for a dev-panel watch.</summary>
        public static string Describe() =>
#if DEVKIT_ENABLED
            _recorder != null ? _recorder.Describe() : "no recorder";
#else
            "recording unavailable (DevKit disabled)";
#endif

#if DEVKIT_ENABLED
        /// <summary>
        /// The recorder, created on first use and kept across scene loads.
        /// </summary>
        /// <remarks>
        /// A capture routinely outlives the scene it started in -- the whole
        /// point is to record a run through several -- so this survives a load
        /// rather than being wired into a scene, which is also why the package
        /// ships no prefab for a game to place.
        /// </remarks>
        private static TouchRecorder Recorder
        {
            get
            {
                if (_recorder != null) return _recorder;

                _recorder = UnityEngine.Object.FindFirstObjectByType<TouchRecorder>(
                    FindObjectsInactive.Include);

                if (_recorder == null)
                {
                    GameObject host = new(nameof(TouchRecorder));
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    _recorder = host.AddComponent<TouchRecorder>();
                }

                return _recorder;
            }
        }

        private static void Destroy(TouchRecorder recorder)
        {
            if (recorder == null) return;
            UnityEngine.Object.Destroy(recorder.gameObject);
            _recorder = null;
        }
#else
        private static bool _warned;

        private static void WarnDisabled()
        {
            if (_warned) return;
            _warned = true;
            Debug.Log(
                "[HoppaAdsRecording] Recording is unavailable: DEVKIT_ENABLED is not defined. " +
                "This is expected in a production build.");
        }
#endif
    }
}
