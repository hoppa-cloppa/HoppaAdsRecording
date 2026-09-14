using UnityEngine;
#if DEVKIT_ENABLED
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DevKit;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
// Disambiguates from UnityEngine.Touch, which is the legacy input type.
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace HoppaPlay.AdsRecording
{
    /// <summary>
    /// Records touches while the player plays, in the canonical interaction
    /// format the web video editor consumes.
    ///
    /// It records ONLY touches, never video. The phone's own screen recorder
    /// captures the picture far better than we could, and the person adds that
    /// file to the project afterwards. That keeps this class free of
    /// MediaProjection, MediaCodec and storage permissions entirely.
    ///
    /// Output (see the editor's interactionData.ts):
    ///   {
    ///     "version": 1,
    ///     "sessionId": "...",
    ///     "videoWidth": 1080,
    ///     "videoHeight": 1920,
    ///     "events": [ { "time": 1.421, "x": 0.71, "y": 0.34, "type": "down" } ]
    ///   }
    /// </summary>
    public class TouchRecorder : MonoBehaviour
    {
#if DEVKIT_ENABLED

        /// <summary>Guard against a session left running for hours by accident.</summary>
        private const int MaxEvents = 200000;

        private readonly List<TouchSample> _samples = new();
        private readonly HashSet<int> _gameplayPointers = new();
        private DevKitBootstrap _devPanel;

        private double _startTime;
        private string _sessionId;
        private int _captureWidth;
        private int _captureHeight;
        private bool _isRecording;
        private bool _hitEventLimit;
        private Vector2 _lastMousePosition;

        /// <summary>Tap-log time at which the sync flash was shown.</summary>
        private double _syncMarkerTime;

        /// <summary>
        /// Whether finger movement between press and release is recorded.
        ///
        /// Off by default. A drag-heavy log runs to hundreds of kilobytes,
        /// which is fine for a file but too big to share as a message -- and
        /// sharing is how a recording actually leaves the phone. Turn it on for
        /// a swipe game, where the path is the gameplay.
        /// </summary>
        public bool RecordDrags { get; set; }

        /// <summary>
        /// Screen-space rectangles whose touches are never recorded.
        ///
        /// The dev button and the recording handle both live here: tapping one
        /// to reload a level or stop is not gameplay, and would otherwise
        /// appear in the data as a phantom tap. The recorder reads touches at
        /// the OS level, so a UI element consuming one does NOT hide it from
        /// here -- the regions have to be excluded explicitly.
        /// </summary>
        public readonly List<Rect> IgnoreRects = new();

        /// <summary>Replaces the ignore regions, dropping any empty ones.</summary>
        public void SetIgnoreRects(params Rect[] rects)
        {
            IgnoreRects.Clear();
            if (rects == null)
            {
                return;
            }

            foreach (Rect rect in rects)
            {
                if (rect.width > 0f && rect.height > 0f)
                {
                    IgnoreRects.Add(rect);
                }
            }
        }

        /// <summary>Size of the exported JSON, for deciding how to send it.</summary>
        public int JsonSizeBytes => Encoding.UTF8.GetByteCount(ToJson());

        public bool IsRecording => _isRecording;
        public int EventCount => _samples.Count;

        public double ElapsedSeconds =>
            _isRecording ? Now() - _startTime : 0d;

        private readonly struct TouchSample
        {
            public readonly double Time;
            public readonly float X;
            public readonly float Y;
            public readonly string Type;
            public readonly int PointerId;

            public TouchSample(double time, float x, float y, string type, int pointerId)
            {
                Time = time;
                X = x;
                Y = y;
                Type = type;
                PointerId = pointerId;
            }
        }

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void Awake()
        {
            // DevKit exposes visibility on its scene component, not DevActions.
            // Resolve once, including an inactive bootstrap, before capture starts.
            _devPanel = FindFirstObjectByType<DevKitBootstrap>(FindObjectsInactive.Include);
        }

        public void StartRecording()
        {
            if (_isRecording)
            {
                return;
            }

            _samples.Clear();
            _gameplayPointers.Clear();
            _hitEventLimit = false;
            _sessionId = Guid.NewGuid().ToString("N");
            _startTime = Now();

            // Captured once: the coordinates are normalized against these, and
            // a mid-session orientation change would otherwise silently change
            // what the numbers mean.
            _captureWidth = Screen.width;
            _captureHeight = Screen.height;

            EnhancedTouchSupport.Enable();
            Touch.onFingerDown += OnFingerDown;
            Touch.onFingerMove += OnFingerMove;
            Touch.onFingerUp += OnFingerUp;

            // Shown AFTER _startTime is set, and its own timestamp recorded, so
            // the editor never has to assume the flash was at exactly zero.
            SyncFlash.Show();
            _syncMarkerTime = Now() - _startTime;

            _isRecording = true;
        }

        public void StopRecording()
        {
            if (!_isRecording)
            {
                return;
            }

            Touch.onFingerDown -= OnFingerDown;
            Touch.onFingerMove -= OnFingerMove;
            Touch.onFingerUp -= OnFingerUp;
            EnhancedTouchSupport.Disable();

            _isRecording = false;
            _gameplayPointers.Clear();
        }

        private void OnDestroy()
        {
            StopRecording();
        }

        /// <summary>
        /// Records mouse clicks as taps.
        ///
        /// EnhancedTouch only reports real touches, so without this nothing at
        /// all is captured when playing in the Editor -- which is where a
        /// recording is easiest to test. Skipped whenever a finger is on the
        /// screen, because a touch device also reports a simulated mouse and
        /// recording both would duplicate every tap.
        /// </summary>
        private void Update()
        {
            if (!_isRecording || Touch.activeTouches.Count > 0)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                RecordAt(mouse.position.ReadValue(), "down", 0);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                RecordAt(mouse.position.ReadValue(), "up", 0);
            }
            else if (mouse.leftButton.isPressed)
            {
                Vector2 position = mouse.position.ReadValue();

                // Only when it actually moved: polling every frame would add 60
                // identical samples a second to a stationary press.
                if ((position - _lastMousePosition).sqrMagnitude > 1f)
                {
                    RecordAt(position, "move", 0);
                }

                _lastMousePosition = position;
            }
        }

        /// <summary>
        /// Writes the recording to a file on the device and returns its path.
        ///
        /// The zero-network path: no server address, no cleartext policy, no
        /// wifi. Pull the file over USB (or share it) and import it in the
        /// editor by hand. Worth having as the fallback whenever an upload
        /// fails, because it isolates "did the capture work" from "did the
        /// network work".
        /// </summary>
        public string SaveToFile(string fileName = "taps.json")
        {
            string path = System.IO.Path.Combine(
                Application.persistentDataPath,
                fileName);

            System.IO.File.WriteAllText(path, ToJson(), Encoding.UTF8);
            Debug.Log($"[TouchRecorder] {_samples.Count} events -> {path}");

            return path;
        }

        // ---------------------------------------------------------------------
        // Capture
        // ---------------------------------------------------------------------

        private void OnFingerDown(Finger finger) => Record(finger, "down");

        private void OnFingerMove(Finger finger) => Record(finger, "move");

        private void OnFingerUp(Finger finger) => Record(finger, "up");

        private void Record(Finger finger, string type) =>
            RecordAt(finger.screenPosition, type, finger.index);

        /// <summary>Shared by the touch and mouse paths.</summary>
        private void RecordAt(Vector2 position, string type, int pointerId)
        {
            if (!_isRecording || _captureWidth <= 0 || _captureHeight <= 0)
            {
                return;
            }

            // Decide ownership at press time. A panel action can close the panel
            // before its release arrives; that release must still stay excluded.
            if (type == "down")
            {
                _gameplayPointers.Remove(pointerId);
                if (_devPanel != null && _devPanel.IsOpen)
                {
                    return;
                }

                for (int i = 0; i < IgnoreRects.Count; i++)
                {
                    if (IgnoreRects[i].Contains(position))
                    {
                        return;
                    }
                }

                _gameplayPointers.Add(pointerId);
            }
            else if (!_gameplayPointers.Contains(pointerId))
            {
                return;
            }

            if (type == "up")
            {
                _gameplayPointers.Remove(pointerId);
            }

            if (type == "move" && (!RecordDrags || (_devPanel != null && _devPanel.IsOpen)))
            {
                return;
            }

            if (_samples.Count >= MaxEvents)
            {
                _hitEventLimit = true;
                return;
            }

            // Unity's screen origin is BOTTOM-left; the canonical format is
            // TOP-left, so Y is flipped here. Getting this wrong mirrors every
            // tap vertically, which looks plausible enough to miss.
            float x = Mathf.Clamp01(position.x / _captureWidth);
            float y = Mathf.Clamp01(1f - (position.y / _captureHeight));

            _samples.Add(new TouchSample(
                Now() - _startTime,
                x,
                y,
                type,
                pointerId));
        }

        /// <summary>
        /// Unscaled real time on purpose.
        ///
        /// The tester can set Time.timeScale to 5, and Time.time would then run
        /// five times faster than the screen recording does. Timestamps must
        /// match the video's wall clock, not the simulation's.
        /// </summary>
        private static double Now() => Time.realtimeSinceStartupAsDouble;

        // ---------------------------------------------------------------------
        // Export
        // ---------------------------------------------------------------------

        public bool HitEventLimit => _hitEventLimit;

        /// <summary>
        /// Serialises to the canonical interaction format.
        ///
        /// Written by hand with InvariantCulture rather than through a
        /// serializer: on a device with a German or Turkish locale, the default
        /// float formatting emits "0,71" and the resulting document is not JSON
        /// at all. This is a real failure mode on real phones.
        /// </summary>
        public string ToJson()
        {
            StringBuilder builder = new StringBuilder(_samples.Count * 72 + 128);

            builder.Append("{\"version\":1,\"sessionId\":\"")
                .Append(_sessionId ?? string.Empty)
                .Append("\",\"videoWidth\":")
                .Append(_captureWidth.ToString(CultureInfo.InvariantCulture))
                .Append(",\"videoHeight\":")
                .Append(_captureHeight.ToString(CultureInfo.InvariantCulture))
                .Append(",\"syncMarkerTime\":")
                .Append(_syncMarkerTime.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(",\"events\":[");

            for (int i = 0; i < _samples.Count; i++)
            {
                TouchSample sample = _samples[i];

                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append("{\"time\":")
                    .Append(sample.Time.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(",\"x\":")
                    .Append(sample.X.ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(",\"y\":")
                    .Append(sample.Y.ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(",\"type\":\"")
                    .Append(sample.Type)
                    .Append('"');

                // Only written for secondary fingers: the editor treats a
                // missing pointerId as the primary pointer.
                if (sample.PointerId > 0)
                {
                    builder.Append(",\"pointerId\":")
                        .Append(sample.PointerId.ToString(CultureInfo.InvariantCulture));
                }

                builder.Append('}');
            }

            builder.Append("]}");

            return builder.ToString();
        }

        public string Describe()
        {
            if (!_isRecording)
            {
                return _samples.Count > 0
                    ? $"stopped · {_samples.Count} events"
                    : "idle";
            }

            return $"REC {ElapsedSeconds:0.0}s · {_samples.Count} events";
        }

#endif
    }
}
