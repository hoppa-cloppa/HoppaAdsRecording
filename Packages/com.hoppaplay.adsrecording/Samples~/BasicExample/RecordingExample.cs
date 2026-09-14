using UnityEngine;
using HoppaPlay.AdsRecording;

namespace HoppaPlay.AdsRecording.Samples
{
    /// <summary>
    /// The smallest thing that records an ad.
    /// </summary>
    /// <remarks>
    /// Drop this on any GameObject in a scene and press Play on a device (or in
    /// the editor with DEVKIT_ENABLED defined). Two on-screen buttons start and
    /// finish a take.
    ///
    /// A real game would drive the same three calls from its dev panel rather
    /// than from OnGUI -- DevKit's DevActions is what Pinata Blast uses -- but
    /// the calls themselves are exactly these.
    /// </remarks>
    public class RecordingExample : MonoBehaviour
    {
        private string _lastResult = "nothing recorded yet";

        private void OnGUI()
        {
            GUI.skin.button.fontSize = 32;
            GUI.skin.label.fontSize = 24;

            if (!AdsRecording.IsRecording)
            {
                if (GUI.Button(new Rect(40, 40, 420, 90), "Start recording"))
                {
                    /*
                      The callback is the way back once the UI is hidden. Here it
                      does nothing because OnGUI is not hidden by the mask, but a
                      real game MUST pass something that reopens its dev panel --
                      otherwise the tester cannot stop the take.
                    */
                    AdsRecording.Start(openDevPanel: () => { });
                }
            }
            else
            {
                if (GUI.Button(new Rect(40, 40, 420, 90), "Stop & share"))
                {
                    RecordingShare.Result result = AdsRecording.StopAndShare();
                    _lastResult = result.Saved
                        ? $"saved to {result.Location}"
                        : "save failed";
                }

                if (GUI.Button(new Rect(40, 150, 420, 90), "Discard"))
                {
                    AdsRecording.Discard();
                    _lastResult = "discarded";
                }
            }

            GUI.Label(new Rect(40, 270, 900, 40), AdsRecording.Describe());
            GUI.Label(new Rect(40, 320, 900, 40), _lastResult);
        }
    }
}
