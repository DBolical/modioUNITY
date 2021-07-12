using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Networking;

namespace ModIO
{
    public static class DownloadClient
    {
        // ---------[ NESTED DATA-TYPES ]---------
        private class DownloadProgressMarkerCollection
        {
            // ---------[ FIELDS ]---------
            public int lastIndex;
            public int recordedCount;
            public float[] timeStamps;
            public Int64[] byteCounts;

            // ---------[ INITIALIZATION ]---------
            public DownloadProgressMarkerCollection(int markerCount)
            {
                this.lastIndex = -1;
                this.recordedCount = 0;
                this.timeStamps = new float[markerCount];
                this.byteCounts = new Int64[markerCount];
            }

            public DownloadProgressMarkerCollection() : this(0) {}
        }

        private class DownloadMonitorBehaviour : MonoBehaviour
        {
            public Coroutine coroutine = null;
        }
        private static DownloadMonitorBehaviour monitorBehaviour = null;

        // ---------[ CONSTANTS ]---------
        /// <summary>Marker count used for smoothing download speed average.</summary>
        public const int DOWNLOAD_SPEED_MARKER_COUNT = 10;

        /// <summary>Interval between download speed updates.</summary>
        public const float DOWNLOAD_SPEED_UPDATE_INTERVAL = 0.5f;

        // ---------[ IMAGE DOWNLOADS ]---------
        public static ImageRequest DownloadModLogo(ModProfile profile, LogoSize size)
        {
            Debug.Assert(profile != null, "[mod.io] Profile parameter cannot be null");

            return DownloadImage(profile.logoLocator.GetSizeURL(size));
        }

        // TODO(@jackson): Take ModMediaCollection instead of profile
        public static ImageRequest DownloadModGalleryImage(ModProfile profile,
                                                           string imageFileName,
                                                           ModGalleryImageSize size)
        {
            Debug.Assert(profile != null, "[mod.io] Profile parameter cannot be null");
            Debug.Assert(!String.IsNullOrEmpty(imageFileName),
                         "[mod.io] imageFileName parameter needs to be not null or empty (used as identifier for gallery images)");

            ImageRequest request = null;

            if(profile.media == null)
            {
                Debug.LogWarning("[mod.io] The given mod profile has no media information");
            }
            else
            {
                GalleryImageLocator locator = profile.media.GetGalleryImageWithFileName(imageFileName);
                if(locator == null)
                {
                    Debug.LogWarning("[mod.io] Unable to find mod gallery image with the file name \'"
                                     + imageFileName + "\' for the mod profile \'" + profile.name +
                                     "\'[" + profile.id + "]");
                }
                else
                {
                    request = DownloadClient.DownloadModGalleryImage(locator, size);
                }
            }

            return request;
        }

        public static ImageRequest DownloadModGalleryImage(GalleryImageLocator imageLocator,
                                                           ModGalleryImageSize size)
        {
            Debug.Assert(imageLocator != null, "[mod.io] imageLocator parameter cannot be null.");
            Debug.Assert(!String.IsNullOrEmpty(imageLocator.fileName), "[mod.io] imageFileName parameter needs to be not null or empty (used as identifier for gallery images)");

            ImageRequest request = null;
            request = DownloadImage(imageLocator.GetSizeURL(size));
            return request;
        }

        public static ImageRequest DownloadUserAvatar(UserProfile profile,
                                                      UserAvatarSize size)
        {
            Debug.Assert(profile != null, "[mod.io] Profile parameter cannot be null");

            ImageRequest request = null;

            if(profile.avatarLocator == null
               || String.IsNullOrEmpty(profile.avatarLocator.GetSizeURL(size)))
            {
                Debug.LogWarning("[mod.io] User Profile has no associated avatar information");
            }
            else
            {
                request = DownloadImage(profile.avatarLocator.GetSizeURL(size));
            }

            return request;
        }

        public static ImageRequest DownloadYouTubeThumbnail(string youTubeId)
        {
            Debug.Assert(!String.IsNullOrEmpty(youTubeId),
                         "[mod.io] YouTube video identifier cannot be empty");

            ImageRequest request = null;

            string thumbnailURL = Utility.GenerateYouTubeThumbnailURL(youTubeId);

            request = DownloadImage(thumbnailURL);

            return request;
        }

        public static ImageRequest DownloadImage(string imageURL)
        {
            ImageRequest request = new ImageRequest();
            request.isDone = false;

            UnityWebRequest webRequest = UnityWebRequest.Get(imageURL);
            webRequest.downloadHandler = new DownloadHandlerTexture(true);

            var operation = webRequest.SendWebRequest();
            operation.completed += (o) => DownloadClient.OnImageDownloadCompleted(operation, request);


            #if DEBUG
                DebugUtilities.DebugDownload(operation, LocalUser.instance, null);
            #endif

            return request;
        }

        private static void OnImageDownloadCompleted(UnityWebRequestAsyncOperation operation,
                                                     ImageRequest request)
        {
            UnityWebRequest webRequest = operation.webRequest;
            request.isDone = true;

            if(webRequest.isNetworkError || webRequest.isHttpError)
            {
                request.error = WebRequestError.GenerateFromWebRequest(webRequest);
                request.NotifyFailed();
            }
            else
            {
                request.imageTexture = (webRequest.downloadHandler as DownloadHandlerTexture).texture;
                request.NotifySucceeded();
            }
        }

        // ---------[ BINARY DOWNLOADS ]---------
        public static event Action<ModfileIdPair, FileDownloadInfo> modfileDownloadStarted;
        public static event Action<ModfileIdPair, FileDownloadInfo> modfileDownloadSucceeded;
        public static event Action<ModfileIdPair, WebRequestError> modfileDownloadFailed;
        public static Dictionary<ModfileIdPair, FileDownloadInfo> modfileDownloadMap = new Dictionary<ModfileIdPair, FileDownloadInfo>();

        private static Dictionary<ModfileIdPair, DownloadProgressMarkerCollection> modfileProgressMarkers = new Dictionary<ModfileIdPair, DownloadProgressMarkerCollection>();

        public static FileDownloadInfo GetActiveModBinaryDownload(int modId, int modfileId)
        {
            ModfileIdPair idPair = new ModfileIdPair()
            {
                modId = modId,
                modfileId = modfileId,
            };

            FileDownloadInfo downloadInfo;
            if(DownloadClient.modfileDownloadMap.TryGetValue(idPair, out downloadInfo))
            {
                return downloadInfo;
            }

            return null;
        }

        public static FileDownloadInfo StartModBinaryDownload(int modId, int modfileId,
                                                              string targetFilePath)
        {
            ModfileIdPair idPair = new ModfileIdPair()
            {
                modId = modId,
                modfileId = modfileId,
            };

            if(modfileDownloadMap.Keys.Contains(idPair))
            {
                Debug.LogWarning("[mod.io] Mod Binary with matching ids already downloading. TargetFilePath was not updated.");
            }
            else
            {
                modfileDownloadMap[idPair] = new FileDownloadInfo()
                {
                    target = targetFilePath,
                    fileSize = -1,
                    request = null,
                    isDone = false,
                };

                DownloadClient.modfileProgressMarkers[idPair] = new DownloadProgressMarkerCollection(DownloadClient.DOWNLOAD_SPEED_MARKER_COUNT);

                // - Acquire Download URL -
                APIClient.GetModfile(modId, modfileId,
                                     (mf) =>
                                     {
                                        // NOTE(@jackson): May have been cancelled
                                        FileDownloadInfo downloadInfo = GetActiveModBinaryDownload(modId, modfileId);
                                        if(downloadInfo != null)
                                        {
                                            modfileDownloadMap[idPair].fileSize = mf.fileSize;
                                            DownloadModBinary_Internal(idPair, mf.downloadLocator.binaryURL);
                                        }
                                     },
                                     (e) => { if(modfileDownloadFailed != null) { modfileDownloadFailed(idPair, e); } });
            }
            return modfileDownloadMap[idPair];
        }

        public static FileDownloadInfo StartModBinaryDownload(Modfile modfile, string targetFilePath)
        {
            ModfileIdPair idPair = new ModfileIdPair()
            {
                modId = modfile.modId,
                modfileId = modfile.id,
            };

            if(modfileDownloadMap.Keys.Contains(idPair))
            {
                Debug.LogWarning("[mod.io] Mod Binary for modfile is already downloading. TargetFilePath was not updated.");
            }
            else
            {
                modfileDownloadMap[idPair] = new FileDownloadInfo()
                {
                    target = targetFilePath,
                    fileSize = modfile.fileSize,
                    request = null,
                    isDone = false,
                };

                DownloadClient.modfileProgressMarkers[idPair] = new DownloadProgressMarkerCollection(DownloadClient.DOWNLOAD_SPEED_MARKER_COUNT);

                DownloadModBinary_Internal(idPair, modfile.downloadLocator.binaryURL);
            }

            return modfileDownloadMap[idPair];
        }

        private static void DownloadModBinary_Internal(ModfileIdPair idPair, string downloadURL)
        {
            FileDownloadInfo downloadInfo = modfileDownloadMap[idPair];
            downloadInfo.request = UnityWebRequest.Get(downloadURL);

            string tempFilePath = downloadInfo.target + ".download";

            DataStorage.WriteFile(tempFilePath, new byte[0], (p, success) =>
            {
                if(success)
                {
                    downloadInfo.request.downloadHandler = new DownloadHandlerFile(tempFilePath);

                    #if PLATFORM_PS4
                    // NOTE(@jackson): This workaround addresses an issue in UnityWebRequests on the
                    //  PS4 whereby redirects fail in specific cases. Special thanks to @Eamon of
                    //  Spiderling Studios (http://spiderlinggames.co.uk/)
                    downloadInfo.request.redirectLimit = 0;
                    #endif

                    var operation = downloadInfo.request.SendWebRequest();

                    #if DEBUG
                        DebugUtilities.DebugDownload(operation, LocalUser.instance, tempFilePath);
                    #endif

                    operation.completed += (o) => DownloadClient.OnModBinaryRequestCompleted(idPair);

                    DownloadClient.StartMonitoringSpeed();

                    // notify download started
                    if(DownloadClient.modfileDownloadStarted != null)
                    {
                        DownloadClient.modfileDownloadStarted(idPair, downloadInfo);
                    }
                }
                else if(DownloadClient.modfileDownloadFailed != null)
                {
                    string warningInfo = ("Failed to create download file on disk."
                                          + "\nSource: " + downloadURL
                                          + "\nDestination: " + tempFilePath + "\n\n");

                    modfileDownloadFailed(idPair, WebRequestError.GenerateLocal(warningInfo));
                }
            });
        }

        public static void CancelModBinaryDownload(int modId, int modfileId)
        {
            ModfileIdPair idPair = new ModfileIdPair()
            {
                modId = modId,
                modfileId = modfileId,
            };

            CancelModfileDownload_Internal(idPair);
        }

        public static void CancelAnyModBinaryDownloads(int modId)
        {
            List<ModfileIdPair> downloadsToCancel = new List<ModfileIdPair>();

            foreach(var kvp in modfileDownloadMap)
            {
                if(kvp.Key.modId == modId)
                {
                    downloadsToCancel.Add(kvp.Key);
                }
            }

            foreach(var idPair in downloadsToCancel)
            {
                CancelModfileDownload_Internal(idPair);
            }
        }

        private static void CancelModfileDownload_Internal(ModfileIdPair idPair)
        {
            FileDownloadInfo downloadInfo = null;
            if(modfileDownloadMap.TryGetValue(idPair, out downloadInfo))
            {
                if(downloadInfo.request != null)
                {
                    downloadInfo.request.Abort();
                }
                else
                {
                    downloadInfo.wasAborted = true;
                    downloadInfo.isDone = true;

                    modfileDownloadMap.Remove(idPair);
                }
            }
        }

        private static void OnModBinaryRequestCompleted(ModfileIdPair idPair)
        {
            FileDownloadInfo downloadInfo = DownloadClient.modfileDownloadMap[idPair];
            UnityWebRequest request = downloadInfo.request;

            downloadInfo.bytesPerSecond = 0;

            if(request.isNetworkError || request.isHttpError)
            {
                if(request.error.ToUpper() == "USER ABORTED"
                   || request.error.ToUpper() == "REQUEST ABORTED")
                {
                    downloadInfo.wasAborted = true;

                    DownloadClient.FinalizeDownload(idPair, downloadInfo);
                }

                // NOTE(@jackson): This workaround addresses an issue in UnityWebRequests on the
                //  PS4 whereby redirects fail in specific cases. Special thanks to @Eamon of
                //  Spiderling Studios (http://spiderlinggames.co.uk/)
                #if UNITY_PS4 || MODIO_DEV
                else if (downloadInfo.error.webRequest.responseCode == 302) // Redirect limit exceeded
                {
                    string headerLocation = string.Empty;
                    if (downloadInfo.error.webRequest.GetResponseHeaders().TryGetValue("location", out headerLocation)
                        && !request.url.Equals(headerLocation))
                    {
                        if(PluginSettings.REQUEST_LOGGING.logAllResponses)
                        {
                            Debug.LogFormat("[mod.io] Caught PS4 redirection error. Reattempting.\nURL: {0}", headerLocation);
                        }

                        downloadInfo.error = null;
                        downloadInfo.isDone = false;
                        DownloadModBinary_Internal(idPair, headerLocation);
                        return;
                    }
                }
                #endif // UNITY_PS4

                else
                {
                    downloadInfo.error = WebRequestError.GenerateFromWebRequest(request);

                    DownloadClient.FinalizeDownload(idPair, downloadInfo);
                }
            }
            else
            {
                DataStorage.MoveFile(downloadInfo.target + ".download", downloadInfo.target,
                (src, dst, success) =>
                {
                    if(!success)
                    {
                        string errorMessage = ("Download succeeded but failed to rename from"
                                               + " temporary file name."
                                               + "\nTemporary file name: "
                                               + downloadInfo.target + ".download");

                        downloadInfo.error = WebRequestError.GenerateLocal(errorMessage);
                    }

                    DownloadClient.FinalizeDownload(idPair, downloadInfo);
                });
            }
        }

        private static void FinalizeDownload(ModfileIdPair idPair, FileDownloadInfo downloadInfo)
        {
            downloadInfo.bytesPerSecond = 0;
            downloadInfo.isDone = true;

            if(downloadInfo.error == null && DownloadClient.modfileDownloadSucceeded != null)
            {
                DownloadClient.modfileDownloadSucceeded(idPair, downloadInfo);
            }
            else if(downloadInfo.error != null && DownloadClient.modfileDownloadFailed != null)
            {
                DownloadClient.modfileDownloadFailed(idPair, downloadInfo.error);
            }

            modfileDownloadMap.Remove(idPair);
            DownloadClient.modfileProgressMarkers.Remove(idPair);
        }

        private static void StartMonitoringSpeed()
        {
            if(DownloadClient.monitorBehaviour == null)
            {
                GameObject go = new GameObject("[mod.io] Download Speed Monitor");
                DownloadClient.monitorBehaviour = go.AddComponent<DownloadMonitorBehaviour>();
                GameObject.DontDestroyOnLoad(go);
            }

            if(DownloadClient.monitorBehaviour.coroutine == null)
            {
                DownloadClient.monitorBehaviour.coroutine
                 = DownloadClient.monitorBehaviour.StartCoroutine(SpeedMonitorCoroutine());
            }
        }

        private static System.Collections.IEnumerator SpeedMonitorCoroutine()
        {
            while(DownloadClient.modfileDownloadMap.Count > 0)
            {
                int downloadCount = DownloadClient.modfileDownloadMap.Count;
                int monitoredDownloadCount = 0;
                FileDownloadInfo[] infos
                    = new FileDownloadInfo[downloadCount];
                DownloadProgressMarkerCollection[] markerCollections
                    = new DownloadProgressMarkerCollection[downloadCount];

                // collect downloads to update
                foreach(var kvp in DownloadClient.modfileDownloadMap)
                {
                    DownloadProgressMarkerCollection markers = null;

                    if(!kvp.Value.isDone
                       && DownloadClient.modfileProgressMarkers.TryGetValue(kvp.Key, out markers))
                    {
                        infos[monitoredDownloadCount] = kvp.Value;
                        markerCollections[monitoredDownloadCount] = markers;

                        ++monitoredDownloadCount;
                    }
                }

                // update progress
                for(int i = 0;
                    i < monitoredDownloadCount;
                    ++i)
                {
                    FileDownloadInfo downloadInfo = infos[i];
                    DownloadProgressMarkerCollection markers = markerCollections[i];

                    Int64 bytesReceived = (downloadInfo.request == null ? 0
                                           : (Int64)downloadInfo.request.downloadedBytes);

                    DownloadClient.AddDownloadProgressMarker(markers, bytesReceived);
                    downloadInfo.bytesPerSecond = DownloadClient.CalculateAverageDownloadSpeed(markers);
                }

                yield return new WaitForSecondsRealtime(DownloadClient.DOWNLOAD_SPEED_UPDATE_INTERVAL);
            }

            DownloadClient.monitorBehaviour.coroutine = null;
        }

        private static Int64 CalculateAverageDownloadSpeed(DownloadProgressMarkerCollection markers)
        {
            if(markers.lastIndex < 0 || markers.recordedCount <= 1)
            {
                return 0;
            }

            int firstIndex = 0;
            if(markers.recordedCount > markers.timeStamps.Length)
            {
                firstIndex = (markers.lastIndex + 1) % markers.timeStamps.Length;
            }

            float initialTimeStamp = markers.timeStamps[firstIndex];
            Int64 initialByteCount = markers.byteCounts[firstIndex];

            float finalTimeStamp = markers.timeStamps[markers.lastIndex];
            Int64 finalByteCount = markers.byteCounts[markers.lastIndex];

            return (Int64)((finalByteCount - initialByteCount)/(finalTimeStamp - initialTimeStamp));
        }

        private static void AddDownloadProgressMarker(DownloadProgressMarkerCollection markers, Int64 bytesReceived)
        {
            float now = Time.unscaledTime;

            // ignore if not newer timeStamp
            if(markers.lastIndex >= 0
               && (now - markers.timeStamps[markers.lastIndex] <= 0f))
            {
                return;
            }

            if(markers.recordedCount <= 1
               && bytesReceived == 0)
            {
                // overwrite if unstarted
                markers.lastIndex = 0;
                markers.recordedCount = 1;
                markers.timeStamps[0] = now;
                markers.byteCounts[0] = 0;
            }
            else
            {
                // increment counters
                ++markers.lastIndex;
                markers.lastIndex %= markers.timeStamps.Length;
                ++markers.recordedCount;

                // add marker
                markers.timeStamps[markers.lastIndex] = now;
                markers.byteCounts[markers.lastIndex] = bytesReceived;
            }
        }

        // ---------[ OBSOLETE ]---------
        /// <summary>Enable logging of all web requests.</summary>
        [Obsolete("Use PluginSettings.REQUEST_LOGGING instead.")]
        public static bool logAllRequests
        {
            get { return PluginSettings.REQUEST_LOGGING.logAllResponses; }
        }
    }
}
