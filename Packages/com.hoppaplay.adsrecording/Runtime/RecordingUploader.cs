using UnityEngine;
#if DEVKIT_ENABLED
using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;
#endif

namespace HoppaPlay.AdsRecording
{
    /// <summary>
    /// Uploads a finished touch recording to the video editor's recording API.
    ///
    /// The API is deliberately three small calls rather than one big one, so a
    /// dropped mobile connection costs a retry of one part instead of the whole
    /// session:
    ///
    ///   POST /api/recordings                 -> open a session, get an id
    ///   POST /api/recordings/{id}/events     -> the touch log (idempotent)
    ///   POST /api/recordings/{id}/complete   -> create the project, get a URL
    ///
    /// Video is NOT uploaded from here. The phone's own screen recorder writes
    /// it to the gallery, out of reach without storage permissions, so the
    /// person adds it from the editor link instead.
    /// </summary>
    public static class RecordingUploader
    {
#if DEVKIT_ENABLED

        /// <summary>
        /// Where the editor is running. A LAN address while developing; set it
        /// to the deployed host before handing builds to anyone else.
        /// </summary>
        public static string BaseUrl { get; set; } = "http://192.168.33.23:3000";

        /// <summary>Matches RECORDING_API_KEY on the server. Empty means open.</summary>
        public static string ApiKey { get; set; } = string.Empty;

        private const int TimeoutSeconds = 60;
        private const int MaxAttempts = 3;

        public readonly struct UploadResult
        {
            public readonly bool Success;
            public readonly string EditorUrl;
            public readonly string Error;

            private UploadResult(bool success, string editorUrl, string error)
            {
                Success = success;
                EditorUrl = editorUrl;
                Error = error;
            }

            public static UploadResult Ok(string editorUrl) =>
                new(true, editorUrl, null);

            public static UploadResult Fail(string error) =>
                new(false, null, error);
        }

        /// <summary>
        /// Runs the whole handshake. Never throws: failures come back as a
        /// message to show in the dev panel.
        /// </summary>
        public static async UniTask<UploadResult> UploadAsync(
            string interactionJson,
            string label,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(interactionJson))
            {
                return UploadResult.Fail("Nothing recorded.");
            }

            try
            {
                string sessionBody = BuildSessionBody(label);

                JObject created = await PostJsonAsync(
                    $"{BaseUrl}/api/recordings", sessionBody, ct);

                string recordingId = created?["recordingId"]?.ToString();
                if (string.IsNullOrEmpty(recordingId))
                {
                    return UploadResult.Fail("Server did not return a recording id.");
                }

                await PostJsonAsync(
                    $"{BaseUrl}/api/recordings/{recordingId}/events",
                    interactionJson,
                    ct,
                    fileName: "touches.json");

                JObject completed = await PostJsonAsync(
                    $"{BaseUrl}/api/recordings/{recordingId}/complete",
                    "{}",
                    ct);

                string editorUrl = completed?["editorUrl"]?.ToString();
                if (string.IsNullOrEmpty(editorUrl))
                {
                    return UploadResult.Fail("Server did not return an editor URL.");
                }

                return UploadResult.Ok($"{BaseUrl}{editorUrl}");
            }
            catch (OperationCanceledException)
            {
                return UploadResult.Fail("Upload cancelled.");
            }
            catch (Exception e)
            {
                return UploadResult.Fail(e.Message);
            }
        }

        private static string BuildSessionBody(string label)
        {
            JObject body = new()
            {
                ["label"] = label ?? string.Empty,
                ["appVersion"] = Application.version,
                ["deviceModel"] = SystemInfo.deviceModel,
                ["platform"] = Application.platform.ToString()
            };

            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// POSTs a JSON body, retrying transient failures.
        ///
        /// Only connection and 5xx errors are retried: a 4xx means the request
        /// itself is wrong and sending it again would fail identically. Every
        /// endpoint is idempotent, so retrying is safe.
        /// </summary>
        private static async UniTask<JObject> PostJsonAsync(
            string url,
            string body,
            CancellationToken ct,
            string fileName = null)
        {
            string lastError = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                using UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = TimeoutSeconds
                };

                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrEmpty(ApiKey))
                {
                    request.SetRequestHeader("x-api-key", ApiKey);
                }

                if (!string.IsNullOrEmpty(fileName))
                {
                    // Percent-encoded because header values must be latin-1.
                    request.SetRequestHeader("x-file-name", UnityWebRequest.EscapeURL(fileName));
                }

                try
                {
                    await request.SendWebRequest().ToUniTask(cancellationToken: ct);
                }
                catch (UnityWebRequestException)
                {
                    // Handled below by inspecting the response.
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string text = request.downloadHandler.text;
                    return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
                }

                long status = request.responseCode;
                lastError = DescribeFailure(request);

                bool worthRetrying = status == 0 || status >= 500;
                if (!worthRetrying || attempt == MaxAttempts)
                {
                    throw new Exception(lastError);
                }

                // Back off so a server that is briefly busy is not hammered.
                await UniTask.Delay(TimeSpan.FromSeconds(attempt), cancellationToken: ct);
            }

            throw new Exception(lastError ?? "Upload failed.");
        }

        /// <summary>Prefers the API's own message over UnityWebRequest's generic one.</summary>
        private static string DescribeFailure(UnityWebRequest request)
        {
            string body = request.downloadHandler?.text;

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    string message = JObject.Parse(body)["error"]?.ToString();
                    if (!string.IsNullOrEmpty(message))
                    {
                        return $"{request.responseCode}: {message}";
                    }
                }
                catch
                {
                    // Not JSON; fall through.
                }
            }

            return request.responseCode > 0
                ? $"{request.responseCode}: {request.error}"
                : $"Could not reach {request.url}. Is the editor running and on the same network?";
        }

#endif
    }
}
