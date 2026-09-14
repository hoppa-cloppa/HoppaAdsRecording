# HoppaAdsRecording

Records gameplay touch interactions on device and writes them as a JSON log,
which the **Hoppa ad video editor** replays to reconstruct the player's finger
over a screen recording.

Current version: `1.0.0`

### What it does

You record your screen with the phone's own recorder and play the game. This
package runs alongside and captures what your **finger** did — every press,
release and drag, with timestamps and normalised coordinates. It also:

- **hides the game UI** while recording, so the capture is gameplay and nothing
  else, and puts it back afterwards;
- **flashes the screen white** at the start and end of a take, which is how the
  editor aligns the touch log to your video and trims the take out of a longer
  recording;
- **ignores taps on its own controls**, so the dev handle never lands in the
  data as a phantom tap;
- **shares the JSON** straight off the device — it goes out in the same message
  as the screen recording, with no cable and no server.

### Requirements

| | |
|---|---|
| Unity | **2021.3** or newer |
| [Unity-DevKit](https://github.com/eliranshaya/Unity-DevKit) | **required** — see below |
| [UniTask](https://github.com/Cysharp/UniTask) | required by the uploader |
| Input System | `com.unity.inputsystem` (resolved automatically) |
| Newtonsoft Json | `com.unity.nuget.newtonsoft-json` (resolved automatically) |

**DevKit and UniTask install from Git URLs, and Unity's Package Manager cannot
resolve a Git dependency declared inside another package.** They are therefore
*not* listed in this package's `dependencies` — your project's own
`Packages/manifest.json` must have them, exactly as it already does for every
other Hoppa package. Installing this one without them gives a missing-assembly
error, not a silent failure.

### Installation

Package Manager → **Add package from git URL…**

```
https://github.com/hoppa-cloppa/HoppaAdsRecording.git?path=/Packages/com.hoppaplay.adsrecording#v1.0.0
```

Or add it to `Packages/manifest.json` alongside DevKit:

```json
"com.eliranshaya.devkit": "https://github.com/eliranshaya/Unity-DevKit.git?path=/Packages/com.eliranshaya.devkit#1.1.1",
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.10",
"com.hoppaplay.adsrecording": "https://github.com/hoppa-cloppa/HoppaAdsRecording.git?path=/Packages/com.hoppaplay.adsrecording#v1.0.0"
```

### Usage

The whole package is one static class.

```csharp
using HoppaPlay.AdsRecording;

// Start. The lambda is how the tester gets back to your dev panel once the UI
// is hidden -- passing null strands them mid-capture.
AdsRecording.Start(
    openDevPanel: () => DevActions.Open(),
    keepTappable: myDevButton.gameObject,
    ignoreRects: ScreenRectOf(myDevButton));

// While recording
bool running = AdsRecording.IsRecording;
int taps      = AdsRecording.EventCount;
string status = AdsRecording.Describe();   // "REC 12.4s · 31 events"

// Finish: stop, save the JSON, open the share sheet.
RecordingShare.Result result = AdsRecording.StopAndShare();
Debug.Log(result.Location);

// Or stop without sharing, or throw the take away.
AdsRecording.Stop();
AdsRecording.Discard();
```

If your game's HUD objects are named differently from the defaults, tell the
mask once at startup:

```csharp
AdsRecording.SetHiddenObjectNames(new[] { "MainCanvas", "HudRoot" });
```

Safe to call unconditionally — without DevKit it is a no-op, so your startup
code needs no `#if`.

### DevKit requirement

Recording is a **development** feature and is gated on the `DEVKIT_ENABLED`
scripting define, which DevKit-enabled projects already set.

- With `DEVKIT_ENABLED`: everything works.
- Without it: every method is a no-op that logs once, and **the package still
  compiles**. A production build with recording turned off builds normally;
  it simply does not record.

Do not remove the DevKit package reference to disable recording — remove the
`DEVKIT_ENABLED` define instead. The assembly references DevKit either way.

### Updating

Change the tag at the end of the Git URL and let Package Manager resolve it:

```
...#v1.1.0
```

Then **Packages → In Project → Hoppa Ads Recording → Update**, or delete the
entry from `Packages/packages-lock.json` to force a re-resolve.

### Output format

The JSON handed to the ad editor. **This format is fixed** — the editor reads
it, so it must not change without changing the editor too.

```json
{
  "version": 1,
  "sessionId": "3f1c…",
  "videoWidth": 1080,
  "videoHeight": 2316,
  "syncMarkerTime": 4.783,
  "events": [
    { "time": 1.421, "x": 0.71, "y": 0.34, "type": "down", "pointerId": 0 }
  ]
}
```

`x` and `y` are fractions of the screen with the origin at the **bottom-left**
(Unity screen space); the editor flips Y on import. `time` is seconds from the
start of the capture. `syncMarkerTime` is when the start flash fired.

### Migration from the old PIB scripts

If your project has `Assets/.../Gamelogic/Recording/PIB*.cs`, delete that folder
and install this package. The classes were renamed:

| Before | After |
|---|---|
| `PIBTouchRecorder` | `TouchRecorder` |
| `PIBRecordingHandle` | `RecordingHandle` |
| `PIBRecordingUiMask` | `RecordingUiMask` |
| `PIBRecordingShare` | `RecordingShare` |
| `PIBRecordingUploader` | `RecordingUploader` |
| `PIBSyncFlash` | `SyncFlash` |
| namespace `PIB.Recording` | `HoppaPlay.AdsRecording` |

Prefer calling `AdsRecording` rather than the individual pieces — the start and
stop sequences have an order that matters, and the facade is what guarantees it.

**No prefab or scene references break.** The old scripts were never placed on a
GameObject in any asset — the recorder is created at runtime — so there are no
MonoBehaviour GUIDs to preserve. Verify in your own project before deleting:
search your `.prefab` and `.unity` files for the old scripts' GUIDs; if nothing
matches, the folder is safe to remove.

### Licence

MIT.
