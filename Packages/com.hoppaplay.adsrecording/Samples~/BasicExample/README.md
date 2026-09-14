# Basic example

1. Install the package (see the package README).
2. Make sure `DEVKIT_ENABLED` is in your scripting define symbols — without it
   recording is a no-op by design.
3. Add `RecordingExample` to any GameObject in a scene.
4. Build to a device, start your phone's screen recorder, press **Start
   recording**, and play.
5. Press **Stop & share**. The JSON lands in Downloads and the share sheet
   opens — send it along with the screen recording.
6. Upload both to the Hoppa ad video editor.

The three calls that matter are `AdsRecording.Start`, `AdsRecording.StopAndShare`
and `AdsRecording.Discard`. Everything else in the package is implementation.
