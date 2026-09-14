using System.Text;
using UnityEngine;

namespace HoppaPlay.AdsRecording
{
    /// <summary>
    /// Gets a finished tap recording off the device as a real .json FILE.
    ///
    /// The route is MediaStore, not a FileProvider. A FileProvider needs an
    /// authority in the manifest, a paths resource and -- under AGP 8, which
    /// Unity 6 builds with -- an Android library module carrying its own
    /// namespace. Getting any of that wrong breaks the whole Android build,
    /// which is a bad trade for a dev-only convenience. MediaStore needs
    /// nothing but code.
    ///
    /// It also lands somewhere useful: the phone's own Downloads folder, which
    /// the Files app lists and which shows up over USB. So even if the share
    /// sheet is dismissed or fails, the file is already saved and reachable.
    ///
    /// Order of preference:
    ///   1. Write into Downloads via MediaStore, then offer the share sheet.
    ///   2. Failing that, write to the app's private folder and share the JSON
    ///      as text, which is what this did before and always works.
    /// </summary>
    public static class RecordingShare
    {
        public readonly struct Result
        {
            public readonly bool Saved;
            /// <summary>Where it went, in words, for the dev panel to show.</summary>
            public readonly string Location;

            public Result(bool saved, string location)
            {
                Saved = saved;
                Location = location;
            }
        }

        /// <summary>
        /// Saves the recording as a file and opens the share sheet on it.
        /// </summary>
        public static Result SaveAndShare(string json, string fileName)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new Result(false, "nothing recorded");
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                string uri = WriteToDownloads(json, fileName);
                if (!string.IsNullOrEmpty(uri))
                {
                    // The file exists either way; a failed share sheet is not a
                    // failed save, so the result stays positive.
                    ShareUri(uri, fileName);
                    return new Result(true, $"Downloads/{fileName}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RecordingShare] Downloads save failed: {e.Message}");
            }
#endif

            // Fallback: keep a copy where the app can always write, and share
            // the contents as text so the recording still leaves the phone.
            string path = WriteLocally(json, fileName);
            ShareText(json, fileName);
            return new Result(true, path);
        }

        /// <summary>Writes to the app's own folder, which never needs permission.</summary>
        public static string WriteLocally(string json, string fileName)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            System.IO.File.WriteAllText(path, json, Encoding.UTF8);
            return path;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Inserts the JSON into the public Downloads collection.
        ///
        /// Returns the content URI, or null when MediaStore's Downloads
        /// collection does not exist -- it arrived in API 29, and an older
        /// device simply takes the fallback.
        /// </summary>
        private static string WriteToDownloads(string json, string fileName)
        {
            using AndroidJavaClass player =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                player.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject resolver =
                activity.Call<AndroidJavaObject>("getContentResolver");

            // Nested Java classes use $ in their binary name.
            using AndroidJavaClass downloads =
                new AndroidJavaClass("android.provider.MediaStore$Downloads");
            using AndroidJavaObject collection =
                downloads.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");

            using AndroidJavaObject values =
                new AndroidJavaObject("android.content.ContentValues");
            values.Call("put", "_display_name", fileName);
            values.Call("put", "mime_type", "application/json");
            // Relative to the shared storage root; this is what puts it in the
            // folder every file manager shows first.
            values.Call("put", "relative_path", "Download");

            using AndroidJavaObject uri =
                resolver.Call<AndroidJavaObject>("insert", collection, values);
            if (uri == null)
            {
                return null;
            }

            using (AndroidJavaObject stream =
                   resolver.Call<AndroidJavaObject>("openOutputStream", uri))
            {
                if (stream == null)
                {
                    return null;
                }

                // Java's byte is signed, so the payload is handed over as
                // sbyte[]; a byte[] would be marshalled to the wrong array type.
                byte[] utf8 = Encoding.UTF8.GetBytes(json);
                sbyte[] signed = new sbyte[utf8.Length];
                System.Buffer.BlockCopy(utf8, 0, signed, 0, utf8.Length);

                stream.Call("write", signed);
                stream.Call("flush");
                stream.Call("close");
            }

            return uri.Call<string>("toString");
        }

        /// <summary>Opens the share sheet on a content URI, as a JSON file.</summary>
        private static void ShareUri(string uriString, string subject)
        {
            try
            {
                using AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri");
                using AndroidJavaObject uri =
                    uriClass.CallStatic<AndroidJavaObject>("parse", uriString);

                using AndroidJavaClass intentClass =
                    new AndroidJavaClass("android.content.Intent");
                using AndroidJavaObject intent =
                    new AndroidJavaObject("android.content.Intent");

                intent.Call<AndroidJavaObject>(
                    "setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                intent.Call<AndroidJavaObject>("setType", "application/json");
                intent.Call<AndroidJavaObject>(
                    "putExtra", intentClass.GetStatic<string>("EXTRA_STREAM"), uri);
                intent.Call<AndroidJavaObject>(
                    "putExtra", intentClass.GetStatic<string>("EXTRA_SUBJECT"), subject);
                // Without this the receiving app is handed a URI it may not read.
                intent.Call<AndroidJavaObject>(
                    "addFlags", intentClass.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION"));

                using AndroidJavaClass player =
                    new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject chooser =
                    intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, subject);

                activity.Call("startActivity", chooser);
            }
            catch (System.Exception e)
            {
                // The file is already in Downloads, so this is not fatal.
                Debug.LogWarning($"[RecordingShare] Share sheet failed: {e.Message}");
            }
        }
#endif

        /// <summary>
        /// Shares the JSON as message text.
        ///
        /// The fallback for anywhere the Downloads route is unavailable. Fine
        /// for a tap-only recording of a few kB, awkward for one full of drag
        /// samples -- which is why drags stay off.
        /// </summary>
        public static bool ShareText(string json, string subject)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using AndroidJavaClass intentClass =
                    new AndroidJavaClass("android.content.Intent");
                using AndroidJavaObject intent =
                    new AndroidJavaObject("android.content.Intent");

                intent.Call<AndroidJavaObject>(
                    "setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>(
                    "putExtra", intentClass.GetStatic<string>("EXTRA_SUBJECT"), subject);
                intent.Call<AndroidJavaObject>(
                    "putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), json);

                using AndroidJavaClass player =
                    new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject chooser =
                    intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, subject);

                activity.Call("startActivity", chooser);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RecordingShare] Text share failed: {e.Message}");
            }
#endif

            return Copy(json);
        }

        /// <summary>
        /// Puts the JSON on the system clipboard.
        ///
        /// The last resort, and the only route that needs nothing from the OS
        /// at all: paste it into any chat, mail app or text file.
        /// </summary>
        public static bool Copy(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            GUIUtility.systemCopyBuffer = json;
            return true;
        }
    }
}
