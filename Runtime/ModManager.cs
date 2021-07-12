using System;
using System.Collections.Generic;
using System.Linq;
using Path = System.IO.Path;

using UnityEngine;

using ModIO.API;

namespace ModIO
{
    public static class ModManager
    {
        // ---------[ NESTED FIELDS ]---------
        /// <summary>A structure used to store data on disk.</summary>
        private struct PersistentData
        {
            public ModIOVersion lastRunVersion;
        }

        // ---------[ EVENTS ]---------
        /// <summary>An event that notifies listeners that a mod has been installed.</summary>
        public static event Action<ModfileIdPair> onModBinaryInstalled;

        /// <summary>An event that notifies listeners that mods have been uninstalled.</summary>
        public static event Action<ModfileIdPair[]> onModBinariesUninstalled;

        // ---------[ CONSTANTS ]---------
        /// <summary>File name used to store the persistent data.</summary>
        public const string PERSISTENTDATA_FILENAME = "mod_manager.data";

        /// <summary>File name used to store the persistent data.</summary>
        public static readonly string PERSISTENTDATA_FILEPATH;

        // ---------[ FIELDS ]---------
        /// <summary>Data that needs to be stored across sessions.</summary>
        private static PersistentData m_data;

        /// <summary>Minimum mod profile count to request from the API.</summary>
        public static int minimumFetchSize = APIPaginationParameters.LIMIT_MAX;

        // ---------[ INITIALIZATION ]---------
        /// <summary>Initializes the ModManager settings.</summary>
        static ModManager()
        {
            ModManager.PERSISTENTDATA_FILEPATH = IOUtilities.CombinePath(DataStorage.CACHE_DIRECTORY, PERSISTENTDATA_FILENAME);

            DataStorage.ReadJSONFile<PersistentData>(ModManager.PERSISTENTDATA_FILEPATH, (p, success, data) =>
            {
                if(!success)
                {
                    data = new PersistentData();
                }
                else if(data.lastRunVersion < ModIOVersion.Current)
                {
                    DataUpdater.UpdateFromVersion(data.lastRunVersion);
                }

                data.lastRunVersion = ModIOVersion.Current;
                ModManager.m_data = data;

                DataStorage.WriteJSONFile(PERSISTENTDATA_FILEPATH, ModManager.m_data, null);
            });
        }

        #region Managing Mods Locally
        //-------------------------------------//
        // ---------[ MOD MANAGEMENT ]---------
        //-------------------------------------//
        /// <summary>Generates the path for a given modfile install directory.</summary>
        public static string GetModInstallDirectory(int modId, int modfileId)
        {
            return IOUtilities.CombinePath(DataStorage.INSTALLATION_DIRECTORY,
                                           modId.ToString() + "_" + modfileId.ToString());
        }

        /// <summary>Extracts a mod archive to the installs folder and removes other installed versions.</summary>
        public static void TryInstallMod(int modId, int modfileId, Action<bool> onComplete)
        {
            // Needs to have a valid mod id otherwise we mess with player-added mods!
            Debug.Assert(modId != ModProfile.NULL_ID);

            // Define vars
            string installDirectory = ModManager.GetModInstallDirectory(modId, modfileId);
            string tempLocation = IOUtilities.CombinePath(CacheClient.GenerateModBinariesDirectoryPath(modId),
                                                          modfileId.ToString());
            string archivePath = CacheClient.GenerateModBinaryZipFilePath(modId, modfileId);

            // Define the callbacks
            PlatformIOCallbacks.GetFileExistsCallback onArchiveExists = null;
            Action<bool> onOldVersionsUninstalled = null;
            PlatformIOCallbacks.DeleteFileCallback onArchiveDeleted = null;

            onArchiveExists = (path, success) =>
            {
                if(!success)
                {
                    Debug.LogWarning("[mod.io] Unable to extract binary to the mod install folder."
                                     + "\nMod Binary ZipFile [" + archivePath + "] does not exist.");

                    if(onComplete != null)
                    {
                        onComplete.Invoke(false);
                    }
                }
                else
                {
                    DataStorage.DeleteDirectory(tempLocation, (dd_path, dd_success) =>
                    DataStorage.CreateDirectory(tempLocation, (cd_path, cd_success) =>
                    {
                        if(dd_success && cd_success)
                        {
                            bool didExtract = CompressionModule.ExtractAll(archivePath, tempLocation);

                            if(didExtract)
                            {
                                // Remove old versions
                                ModManager.UninstallMod(modId, onOldVersionsUninstalled);
                            }
                            else
                            {
                                DataStorage.DeleteDirectory(tempLocation, null);

                                if(onComplete != null)
                                {
                                    onComplete.Invoke(false);
                                }
                            }
                        }
                        else
                        {
                            if(onComplete != null)
                            {
                                onComplete.Invoke(false);
                            }
                        }
                    }));
                }
            };

            onOldVersionsUninstalled = (success) =>
            {
                if(!success)
                {
                    Debug.LogWarning("[mod.io] Unable to extract binary to the mod install folder."
                                     + "\nFailed to uninstall existing versions of this mod.");

                    DataStorage.DeleteDirectory(tempLocation, null);
                    DataStorage.DeleteFile(archivePath, null);

                    if(onComplete != null)
                    {
                        onComplete.Invoke(false);
                    }
                }
                else
                {
                    DataStorage.DeleteDirectory(installDirectory, (dd_path, dd_success) =>
                    DataStorage.CreateDirectory(DataStorage.INSTALLATION_DIRECTORY, (cd_path, cd_success) =>
                    DataStorage.MoveDirectory(tempLocation, installDirectory, (md_src, md_dst, md_success) =>
                    {
                        if(dd_success && cd_success && md_success)
                        {
                            DataStorage.DeleteFile(archivePath, onArchiveDeleted);
                        }
                        else
                        {
                            Debug.LogWarning("[mod.io] Unable to relocate the mod data from a temp folder to the installations folder."
                                             + "\nSrc: " + tempLocation
                                             + "\nDest: " + installDirectory);

                            if(onComplete != null)
                            {
                                onComplete.Invoke(false);
                            }
                        }
                    })));
                }
            };

            onArchiveDeleted = (path, success) =>
            {
                if(ModManager.onModBinaryInstalled != null)
                {
                    ModfileIdPair idPair = new ModfileIdPair()
                    {
                        modId = modId,
                        modfileId = modfileId,
                    };
                    ModManager.onModBinaryInstalled(idPair);
                }

                if(onComplete != null)
                {
                    onComplete.Invoke(true);
                }
            };

            // Run Install Process
            DataStorage.GetFileExists(archivePath, onArchiveExists);
        }

        /// <summary>Removes all versions of a mod from the installs folder.</summary>
        public static void UninstallMod(int modId, Action<bool> onComplete)
        {
            // Don't accidentally uninstall player-added mods!
            Debug.Assert(modId != ModProfile.NULL_ID);

            ModManager.QueryInstalledMods(new int[] { modId }, (installedMods) =>
            {
                List<ModfileIdPair> successfulUninstalls = new List<ModfileIdPair>();
                int index = 0;
                Action uninstallAction = null;

                uninstallAction = () =>
                {
                    if(index < installedMods.Count)
                    {
                        var installInfo = installedMods.ElementAt(index++);
                        DataStorage.DeleteDirectory(installInfo.Value, (p, success) =>
                        {
                            if(success) { successfulUninstalls.Add(installInfo.Key); }

                            uninstallAction();
                        });
                    }
                    else
                    {
                        // notify uninstall listeners
                        if(ModManager.onModBinariesUninstalled != null)
                        {
                            ModManager.onModBinariesUninstalled(successfulUninstalls.ToArray());
                        }

                        // invoke callback
                        if(onComplete != null)
                        {
                            onComplete.Invoke(successfulUninstalls.Count == installedMods.Count);
                        }
                    }
                };

                uninstallAction.Invoke();
            });
        }

        /// <summary>Removes a specific version of a mod from the installs folder.</summary>
        public static void TryUninstallModVersion(int modId, int modfileId, Action<bool> onComplete)
        {
            // Don't accidentally uninstall player-added mods!
            Debug.Assert(modId != ModProfile.NULL_ID);

            ModManager.QueryInstalledMods(new int[] { modId }, (installedMods) =>
            {
                foreach(var installInfo in installedMods)
                {
                    if(installInfo.Key.modfileId == modfileId)
                    {
                        DataStorage.DeleteDirectory(installInfo.Value, (path, success) =>
                        {
                            if(success && ModManager.onModBinariesUninstalled != null)
                            {
                                ModfileIdPair idPair = new ModfileIdPair()
                                {
                                    modId = modId,
                                    modfileId = modfileId,
                                };

                                ModManager.onModBinariesUninstalled(new ModfileIdPair[] { idPair });
                            }

                            if(onComplete != null)
                            {
                                onComplete.Invoke(success);
                            }
                        });

                        return;
                    }
                }

                if(onComplete != null)
                {
                    onComplete.Invoke(true);
                }
            });
        }

        /// <summary>Returns all of the mod directories of installed mods.</summary>
        public static void QueryInstalledModDirectories(bool excludeDisabledMods, Action<List<string>> onComplete)
        {
            Debug.Assert(onComplete != null);

            List<int> modIdFilter = null;
            if(excludeDisabledMods)
            {
                modIdFilter = new List<int>(LocalUser.EnabledModIds);
                // Include drop-ins
                modIdFilter.Add(ModProfile.NULL_ID);
            }

            ModManager.QueryInstalledMods(modIdFilter, (installedMods) =>
            {
                List<string> directories = new List<string>();

                foreach(var kvp in installedMods)
                {
                    directories.Add(kvp.Value);
                }

                onComplete.Invoke(directories);
            });
        }

        /// <summary>Returns all of the mod version info of installed mods.</summary>
        public static void QueryInstalledModVersions(bool excludeDisabledMods, Action<List<ModfileIdPair>> onComplete)
        {
            Debug.Assert(onComplete != null);

            List<int> modIdFilter = null;
            if(excludeDisabledMods)
            {
                modIdFilter = new List<int>(LocalUser.EnabledModIds);
            }

            ModManager.QueryInstalledMods(modIdFilter, (installedMods) =>
            {
                List<ModfileIdPair> versions = new List<ModfileIdPair>();

                foreach(var kvp in installedMods)
                {
                    if(kvp.Key.modId != ModProfile.NULL_ID)
                    {
                        versions.Add(kvp.Key);
                    }
                }

                onComplete.Invoke(versions);
            });
        }

        /// <summary>Returns the data of all the mods installed.</summary>
        /// <remarks>This does not ensure a unique collection of ModfileIdPair objects, as there
        /// may be multiple directories in this folder are not named in the modId_modfileId
        /// format.</remarks>
        public static void QueryInstalledMods(IList<int> modIdFilter, Action<IList<KeyValuePair<ModfileIdPair, string>>> onComplete)
        {
            Debug.Assert(onComplete != null);

            DataStorage.GetDirectories(DataStorage.INSTALLATION_DIRECTORY, (path, exists, modDirectories) =>
            {
                var installedModMap = new List<KeyValuePair<ModfileIdPair, string>>();

                if(exists && modDirectories != null)
                {
                    foreach(string modDirectory in modDirectories)
                    {
                        string folderName = IOUtilities.GetPathItemName(modDirectory);
                        string[] folderNameParts = folderName.Split('_');

                        int modId;
                        int modfileId;
                        if(!(folderNameParts.Length > 0
                             && Int32.TryParse(folderNameParts[0], out modId)))
                        {
                            modId = ModProfile.NULL_ID;
                        }

                        if(modIdFilter == null
                           || modIdFilter.Contains(modId))
                        {
                            if(!(modId != ModProfile.NULL_ID
                                 && folderNameParts.Length > 1
                                 && Int32.TryParse(folderNameParts[1], out modfileId)))
                            {
                                modfileId = Modfile.NULL_ID;
                            }

                            ModfileIdPair idPair = new ModfileIdPair()
                            {
                                modId = modId,
                                modfileId = modfileId,
                            };

                            var kvp = new KeyValuePair<ModfileIdPair, string>(idPair, modDirectory);
                            installedModMap.Add(kvp);
                        }
                    }
                }

                if(onComplete != null)
                {
                    onComplete.Invoke(installedModMap);
                }
            });
        }

        /// <summary>Downloads and installs a single mod</summary>
        public static void DownloadAndUpdateMod(int modId, Action onSuccess, Action<WebRequestError> onError)
        {
            Debug.Assert(modId != ModProfile.NULL_ID);

            // vars
            ModProfile profile = null;
            Modfile modfile = null;
            string installDir = null;
            string zipFilePath = null;

            // --- local callbacks ---
            Action<ModProfile> onGetModProfile = null;
            PlatformIOCallbacks.GetFileSizeAndHashCallback onGetModProfile_OnGetFileInfo = null;
            Action<ModfileIdPair, FileDownloadInfo> onDownloadSucceeded = null;
            Action<ModfileIdPair, WebRequestError> onDownloadFailed = null;
            Action<bool> onInstalled = null;

            onGetModProfile = (p) =>
            {
                profile = p;
                modfile = p.currentBuild;

                installDir = ModManager.GetModInstallDirectory(p.id, modfile.id);

                DataStorage.GetDirectoryExists(installDir, (gde_path, gde_exists) =>
                {
                    if(gde_exists)
                    {
                        if(onSuccess != null)
                        {
                            onSuccess.Invoke();
                        }
                    }
                    else
                    {
                        zipFilePath = CacheClient.GenerateModBinaryZipFilePath(profile.id, modfile.id);
                        DataStorage.GetFileSizeAndHash(zipFilePath, onGetModProfile_OnGetFileInfo);
                    }
                });
            };

            onGetModProfile_OnGetFileInfo = (path, success, fileSize, fileHash) =>
            {
                bool isBinaryZipValid = (success
                                         && modfile.fileSize == fileSize
                                         && (modfile.fileHash == null || modfile.fileHash.md5 == fileHash));

                if(isBinaryZipValid)
                {
                    ModManager.TryInstallMod(profile.id, modfile.id, onInstalled);
                }
                else
                {
                    DownloadClient.StartModBinaryDownload(modfile,
                                                          CacheClient.GenerateModBinaryZipFilePath(profile.id, profile.currentBuild.id));

                    DownloadClient.modfileDownloadSucceeded += onDownloadSucceeded;
                    DownloadClient.modfileDownloadFailed += onDownloadFailed;
                }
            };


            onDownloadSucceeded = (mip, downloadInfo) =>
            {
                if(mip.modId == modId)
                {
                    DownloadClient.modfileDownloadSucceeded -= onDownloadSucceeded;
                    DownloadClient.modfileDownloadFailed -= onDownloadFailed;

                    ModManager.TryInstallMod(profile.id, modfile.id, onInstalled);
                }
            };

            onDownloadFailed = (mip, e) =>
            {
                if(mip.modId == modId)
                {
                    DownloadClient.modfileDownloadSucceeded -= onDownloadSucceeded;
                    DownloadClient.modfileDownloadFailed -= onDownloadFailed;

                    if(onError != null)
                    {
                        onError(e);
                    }
                }
            };

            onInstalled = (success) =>
            {
                if(success)
                {
                    if(onSuccess != null)
                    {
                        onSuccess();
                    }
                }
                else
                {
                    if(onError != null)
                    {
                        string message = ("Successfully downloaded but failed to install mod \'"
                                          + profile.name + "\'. See logged message for details.");
                        onError(WebRequestError.GenerateLocal(message));
                    }
                }
            };


            // --- GO! ----
            APIClient.GetMod(modId, onGetModProfile, onError);
        }

        /// <summary>Downloads and updates mods to the latest version.</summary>
        public static System.Collections.IEnumerator DownloadAndUpdateMods_Coroutine(IList<int> modIds, Action onCompleted = null)
        {
            Debug.Assert(modIds != null);

            // early out for 0 mods
            if(modIds.Count == 0)
            {
                if(onCompleted != null) { onCompleted(); }
                yield break;
            }

            // --- local delegates ---
            Func<WebRequestError, int> calcReattemptDelay = (requestError) =>
            {
                if(requestError.limitedUntilTimeStamp > 0)
                {
                    return (requestError.limitedUntilTimeStamp - ServerTimeStamp.Now);
                }
                else if(!requestError.isRequestUnresolvable)
                {
                    if(requestError.isServerUnreachable)
                    {
                        return 60;
                    }
                    else
                    {
                        return 15;
                    }
                }
                else
                {
                    return 0;
                }
            };

            // - vars -
            int attemptCount = 0;
            int attemptLimit = 2;
            bool isRequestResolved = false;

            // - fetch the latest build info -
            List<Modfile> lastestBuilds = new List<Modfile>(modIds.Count);

            RequestFilter modFilter = new RequestFilter();
            modFilter.AddFieldFilter(GetAllModsFilterFields.id, new InArrayFilter<int>()
            {
                filterArray = modIds.ToArray()
            });

            while(!isRequestResolved
                  && attemptCount < attemptLimit)
            {
                bool isDone = false;
                WebRequestError error = null;
                List<ModProfile> profiles = null;

                ModManager.FetchAllResultsForQuery<ModProfile>((p,s,e) => APIClient.GetAllMods(modFilter,p,s,e),
                (r) =>
                {
                    profiles = r;
                    isDone = true;
                },
                (e) =>
                {
                    error = e;
                    isDone = true;
                });

                while(!isDone) { yield return null; }

                if(error != null)
                {
                    if(error.isAuthenticationInvalid)
                    {
                        isRequestResolved = true;
                    }
                    else if(error.isRequestUnresolvable)
                    {
                        isRequestResolved = true;
                    }
                    else
                    {
                        ++attemptCount;

                        int reattemptDelay = calcReattemptDelay(error);
                        yield return new WaitForSecondsRealtime(reattemptDelay);
                    }
                }
                else
                {
                    foreach(ModProfile profile in profiles)
                    {
                        lastestBuilds.Add(profile.currentBuild);
                    }

                    isRequestResolved = true;
                }
            }

            // - remove any builds that are already installed -
            System.Collections.IEnumerator assertCoroutine = ModManager.AssertDownloadedAndInstalled_Coroutine(lastestBuilds);
            while(assertCoroutine.MoveNext()) { yield return assertCoroutine.Current; }

            if(onCompleted != null)
            {
                onCompleted();
            }
        }

        /// <summary>Asserts that the given modfiles are downloaded and installed.</summary>
        public static System.Collections.IEnumerator AssertDownloadedAndInstalled_Coroutine(IEnumerable<Modfile> modfiles,
                                                                                            Action onCompleted = null)
        {
            Debug.Assert(modfiles != null);

            List<Modfile> unmatchedModfiles = new List<Modfile>(modfiles);

            // early out for 0 modfiles
            if(unmatchedModfiles.Count == 0)
            {
                if(onCompleted != null) { onCompleted(); }
                yield break;
            }

            // check for installs
            bool gotModVersions = false;
            List<ModfileIdPair> installedModVersions = null;
            ModManager.QueryInstalledModVersions(false,
            (r) =>
            {
                installedModVersions = r;
                gotModVersions = true;
            });

            while(!gotModVersions) { yield return null;}

            for(int i = 0;
                i < unmatchedModfiles.Count;
                ++i)
            {
                Modfile m = unmatchedModfiles[i];

                if(m == null)
                {
                    unmatchedModfiles.RemoveAt(i);
                    --i;
                }
                else
                {
                    // check if installed
                    bool isInstalled = false;

                    foreach(ModfileIdPair idPair in installedModVersions)
                    {
                        if(idPair.modId == m.modId
                           && idPair.modfileId == m.id)
                        {
                            isInstalled = true;
                            break;
                        }
                    }

                    // check for zip
                    if(!isInstalled)
                    {
                        string zipFilePath = CacheClient.GenerateModBinaryZipFilePath(m.modId, m.id);

                        bool fileExists = false;
                        Int64 fileSize = -1;
                        string fileHash = null;

                        bool isIOOpDone = false;
                        DataStorage.GetFileSizeAndHash(zipFilePath, (p, success, byteCount, md5Hash) =>
                        {
                            fileExists = success;
                            fileSize = byteCount;
                            fileHash = md5Hash;

                            isIOOpDone = true;
                        });

                        while(!isIOOpDone) { yield return null; }

                        bool isDownloadedAndValid = (fileExists
                                                     && m.fileSize == fileSize
                                                     && (m.fileHash == null
                                                         || m.fileHash.md5 == fileHash));

                        if(isDownloadedAndValid)
                        {
                            bool installDone = false;
                            ModManager.TryInstallMod(m.modId, m.id, (success) =>
                            {
                                installDone = true;
                                isInstalled = success;
                            });

                            while(!installDone) { yield return null; }
                        }
                    }

                    // update
                    if(isInstalled)
                    {
                        unmatchedModfiles.RemoveAt(i);
                        --i;
                    }
                }
            }

            // check for expired download links
            int awaitingModfileUpdates = 0;
            List<Modfile> badModfiles = new List<Modfile>();

            for(int i = 0;
                i < unmatchedModfiles.Count;
                ++i)
            {
                int modIndex = i;
                Modfile modfile = unmatchedModfiles[i];

                if(modfile.downloadLocator == null
                   || modfile.downloadLocator.dateExpires <= ServerTimeStamp.Now)
                {
                    ++awaitingModfileUpdates;

                    APIClient.GetModfile(modfile.modId, modfile.id,
                    (updatedModfile) =>
                    {
                        --awaitingModfileUpdates;

                        if(modfile.downloadLocator == null)
                        {
                            badModfiles.Add(modfile);

                            Debug.LogWarning("[mod.io] Unable to get a good download locator for"
                                             + " (modId:" + modfile.modId.ToString()
                                             + "-modfileId:" + modfile.id.ToString()
                                             + ").");
                        }
                        else
                        {
                            unmatchedModfiles[modIndex] = updatedModfile;
                        }
                    },
                    (e) =>
                    {
                        --awaitingModfileUpdates;

                        badModfiles.Add(modfile);

                        Debug.LogWarning("[mod.io] Unable to get a good download locator for"
                                         + " (modId:" + modfile.modId.ToString()
                                         + "-modfileId:" + modfile.id.ToString()
                                         + ").\n---[ Response Info ]---\n"
                                         + DebugUtilities.GetResponseInfo(e.webRequest));
                    });
                }
            }

            while(awaitingModfileUpdates > 0) { yield return null; }

            foreach(Modfile brokenModfile in badModfiles)
            {
                unmatchedModfiles.Remove(brokenModfile);
            }

            // - download and install -
            if(unmatchedModfiles.Count > 0)
            {
                bool startNextDownload = false;
                Modfile downloadingModfile = null;

                // set up event listeners
                Action<ModfileIdPair, FileDownloadInfo> onDownloadSucceeded = (idPair, info) =>
                {
                    if(idPair.modfileId == downloadingModfile.id)
                    {
                        ModManager.TryInstallMod(downloadingModfile.modId, downloadingModfile.id,
                        (success) =>
                        {
                            if(!success)
                            {
                                Debug.LogWarning("[mod.io] Successfully downloaded but failed to install mod (id:"
                                                 + downloadingModfile.modId.ToString()
                                                 + "-modfile:" + downloadingModfile.id.ToString()
                                                  + "). See logged message for details.");
                            }

                            startNextDownload = true;
                        });
                    }
                };

                Action<ModfileIdPair, WebRequestError> onDownloadFailed = (idPair, e) =>
                {
                    if(idPair.modfileId == downloadingModfile.id)
                    {
                        Debug.LogWarning("[mod.io] Failed to download mod (id:"
                                         + downloadingModfile.modId.ToString()
                                         + "-modfile:" + downloadingModfile.id.ToString()
                                         + "). See logged message for details.");

                        startNextDownload = true;
                    }
                };

                DownloadClient.modfileDownloadSucceeded += onDownloadSucceeded;
                DownloadClient.modfileDownloadFailed += onDownloadFailed;

                // go!
                foreach(Modfile modfile in unmatchedModfiles)
                {
                    // check if already downloading
                    if(DownloadClient.GetActiveModBinaryDownload(modfile.modId, modfile.id) != null)
                    {
                        continue;
                    }

                    downloadingModfile = modfile;
                    startNextDownload = false;

                    string zipPath = CacheClient.GenerateModBinaryZipFilePath(downloadingModfile.modId,
                                                                              downloadingModfile.id);

                    DownloadClient.StartModBinaryDownload(modfile, zipPath);

                    while(!startNextDownload) { yield return null; }
                }

                DownloadClient.modfileDownloadSucceeded -= onDownloadSucceeded;
                DownloadClient.modfileDownloadFailed -= onDownloadFailed;
            }

            // done!
            if(onCompleted != null)
            {
                onCompleted();
            }
        }
        #endregion

        #region Game Profile
        //-------------------------------------//
        // ---------[ GAME PROFILE ]-----------
        //-------------------------------------//
        /// <summary>Fetches and caches the Game Profile (if not already cached).</summary>
        public static void GetGameProfile(Action<GameProfile> onSuccess,
                                          Action<WebRequestError> onError)
        {
            CacheClient.LoadGameProfile((cachedProfile) =>
            {
                if(cachedProfile != null)
                {
                    if(onSuccess != null)
                    {
                        onSuccess(cachedProfile);
                    }
                }
                else
                {
                    Action<GameProfile> onGetProfile = (profile) =>
                    {
                        CacheClient.SaveGameProfile(profile, null);
                        if(onSuccess != null) { onSuccess(profile); }
                    };

                    APIClient.GetGame(onGetProfile,
                                      onError);
                }
            });
        }
        #endregion

        #region Mod Profiles
        //-------------------------------------//
        // ---------[ MOD PROFILES ]-----------
        //-------------------------------------//
        /// <summary>Fetches a mod profile checking the cache, storing if subscribed.</summary>
        public static void GetModProfile(int modId,
                                         Action<ModProfile> onSuccess,
                                         Action<WebRequestError> onError)
        {
            if(onSuccess == null && onError == null) { return; }

            APIClient.GetMod(modId,
            (p) =>
            {
                if(LocalUser.SubscribedModIds.Contains(p.id))
                {
                    CacheClient.SaveModProfile(p, null);
                }

                if(onSuccess != null)
                {
                    onSuccess.Invoke(p);
                }
            },
            (e) =>
            {
                CacheClient.LoadModProfile(modId,
                (cachedProfile) =>
                {
                    if(cachedProfile != null)
                    {
                        if(onSuccess != null)
                        {
                            onSuccess.Invoke(cachedProfile);
                        }
                    }
                    else
                    {
                        if(onError != null)
                        {
                            onError.Invoke(e);
                        }
                    }
                });
            });
        }

        /// <summary>Fetches a list of mod profiles checking the cache, storing if subscribed.</summary>
        public static void GetModProfiles(IList<int> orderedIdList,
                                          Action<ModProfile[]> onSuccess,
                                          Action<WebRequestError> onError)
        {
            // early out
            if(orderedIdList == null)
            {
                if(onSuccess != null)
                {
                    onSuccess.Invoke(null);
                }

                return;
            }

            // grab from request cache
            ModProfile[] modProfiles = new ModProfile[orderedIdList.Count];
            List<int> missingModIds = new List<int>(orderedIdList.Count);

            for(int i = 0; i < orderedIdList.Count; ++i)
            {
                int modId = orderedIdList[i];
                ModProfile profile;
                if(RequestCache.TryGetMod(PluginSettings.GAME_ID, modId, out profile))
                {
                    modProfiles[i] = profile;
                }
                else
                {
                    missingModIds.Add(modId);
                }
            }

            // all found - out
            if(missingModIds.Count == 0)
            {
                if(onSuccess != null)
                {
                    onSuccess.Invoke(modProfiles);
                }

                return;
            }

            // setup callbacks
            Action<WebRequestError> checkForMissingModsInCache = (error) =>
            {
                if(missingModIds.Count == 0)
                {
                    if(onSuccess != null)
                    {
                        onSuccess.Invoke(modProfiles);
                    }
                }

                CacheClient.RequestFilteredModProfiles(missingModIds,
                (cachedProfiles) =>
                {
                    foreach(var profile in cachedProfiles)
                    {
                        int index = orderedIdList.IndexOf(profile.id);
                        if(index >= 0)
                        {
                            modProfiles[index] = profile;
                        }

                        missingModIds.Remove(profile.id);
                    }

                    if(missingModIds.Count > 0 && error != null)
                    {
                        if(onError != null) { onError.Invoke(error); }
                    }
                    else
                    {
                        if(onSuccess != null) { onSuccess.Invoke(modProfiles); }
                    }
                });
            };

            Action<List<ModProfile>> onGetMods = (fetchedProfiles) =>
            {
                foreach(var profile in fetchedProfiles)
                {
                    int index = orderedIdList.IndexOf(profile.id);
                    if(index >= 0)
                    {
                        modProfiles[index] = profile;
                    }

                    missingModIds.Remove(profile.id);
                }

                checkForMissingModsInCache(null);
            };

            // setup filter
            RequestFilter modFilter = new RequestFilter();
            modFilter.sortFieldName = GetAllModsFilterFields.id;
            modFilter.AddFieldFilter(GetAllModsFilterFields.id, new InArrayFilter<int>()
            {
                filterArray = missingModIds.ToArray()
            });

            // - Get All Mods -
            ModManager.FetchAllResultsForQuery<ModProfile>((p,s,e) => APIClient.GetAllMods(modFilter, p, s, e),
                                                           onGetMods,
                                                           checkForMissingModsInCache);
        }

        /// <summary>Get a range of mod profiles.</summary>
        public static void GetRangeOfModProfiles(RequestFilter filter, int resultOffset, int profileCount,
                                                Action<RequestPage<ModProfile>> onSuccess,
                                                Action<WebRequestError> onError) {
            Debug.Assert(minimumFetchSize <= APIPaginationParameters.LIMIT_MAX);

            // early out if onSuccess == null
            if (onSuccess == null && onError == null) { return; }

            if (profileCount > APIPaginationParameters.LIMIT_MAX) {
                Debug.LogWarning("[mod.io] FetchModProfilePage has been called with a profileCount"
                                 + " larger than the APIPaginationParameters.LIMIT_MAX."
                                 + "\nAs such, results may not be as expected.");

                profileCount = APIPaginationParameters.LIMIT_MAX;
            }

            // ensure indicies are positive
            if (resultOffset < 0) { resultOffset = 0; }
            if (profileCount < 0) { profileCount = 0; }

            // setup request structures
            List<ModProfile> results = new List<ModProfile>(profileCount);

            // PaginationParameters
            APIPaginationParameters pagination = new APIPaginationParameters();

            int pageIndex = resultOffset / minimumFetchSize;
            pagination.offset = pageIndex * minimumFetchSize;
            pagination.limit = minimumFetchSize;

            APIClient.GetAllMods(filter, pagination,
            (r01) => {
                int pageOffset = resultOffset % minimumFetchSize;

                for (int i = pageOffset;
                    i < r01.items.Length
                    && i < pageOffset + profileCount;
                    ++i) {
                    results.Add(r01.items[i]);
                }

                if (pageOffset + profileCount > r01.size
                   && r01.items.Length == r01.size) {
                    pagination.offset += pagination.limit;
                    APIClient.GetAllMods(filter, pagination,
                    (r02) => {
                        for (int i = 0;
                            i < r02.items.Length
                            && i < pageOffset + profileCount - r02.size;
                            ++i) {
                            results.Add(r02.items[i]);
                            OnModsReceived(resultOffset, profileCount, r02.resultTotal,
                                           results, onSuccess);
                        }
                    }, onError);
                } else {
                    OnModsReceived(resultOffset, profileCount, r01.resultTotal,
                                   results, onSuccess);
                }
            }, onError);
        }

        /// <summary>Processes the received mod collection and generates a request page.</summary>
        private static void OnModsReceived(int resultOffset, int pageSize, int resultTotal,
                                    List<ModProfile> results,
                                    Action<RequestPage<ModProfile>> onSuccess) {
            if (onSuccess == null) { return; }

            RequestPage<ModProfile> page = new RequestPage<ModProfile>() {
                size = pageSize,
                resultOffset = resultOffset,
                resultTotal = resultTotal,
                items = results.ToArray(),
            };

            onSuccess.Invoke(page);
        }
        #endregion

        #region Mod Images
        //-------------------------------------//
        // ---------[ MOD IMAGES ]-------------
        //-------------------------------------//
        /// <summary>Fetches and caches a Mod Logo (if not already cached).</summary>
        public static void GetModLogo(ModProfile profile, LogoSize size,
                                      Action<Texture2D> onSuccess,
                                      Action<WebRequestError> onError)
        {
            Debug.Assert(profile != null);

            GetModLogo(profile.id, profile.logoLocator, size, onSuccess, onError);
        }

        /// <summary>Fetches and caches a Mod Logo (if not already cached).</summary>
        public static void GetModLogo(int modId, LogoImageLocator logoLocator,
                                      LogoSize size,
                                      Action<Texture2D> onSuccess,
                                      Action<WebRequestError> onError)
        {
            Debug.Assert(modId != ModProfile.NULL_ID);
            Debug.Assert(logoLocator != null);
            Debug.Assert(onSuccess != null);

            CacheClient.LoadModLogo(modId, logoLocator.fileName, size, (logoTexture) =>
            {
                if(logoTexture != null)
                {
                    if(onSuccess != null) { onSuccess(logoTexture); }
                }
                else
                {
                    var textureDownload = DownloadClient.DownloadImage(logoLocator.GetSizeURL(size));

                    textureDownload.succeeded += (d) =>
                    {
                        CacheClient.SaveModLogo(modId, logoLocator.GetFileName(), size, d.imageTexture, null);
                    };

                    textureDownload.succeeded += (d) => onSuccess(d.imageTexture);
                    textureDownload.failed += (d) => onError(d.error);
                }
            });
        }

        /// <summary>Fetches and caches a Mod Gallery Image (if not already cached).</summary>
        public static void GetModGalleryImage(ModProfile profile,
                                              string imageFileName,
                                              ModGalleryImageSize size,
                                              Action<Texture2D> onSuccess,
                                              Action<WebRequestError> onError)
        {
            Debug.Assert(profile != null);

            ModManager.GetModGalleryImage(profile.id, profile.media.GetGalleryImageWithFileName(imageFileName), size, onSuccess, onError);
        }

        /// <summary>Fetches and caches a Mod Gallery Image (if not already cached).</summary>
        public static void GetModGalleryImage(int modId,
                                              GalleryImageLocator imageLocator,
                                              ModGalleryImageSize size,
                                              Action<Texture2D> onSuccess,
                                              Action<WebRequestError> onError)
        {
            Debug.Assert(imageLocator != null, "[mod.io] imageLocator parameter cannot be null.");
            Debug.Assert(!String.IsNullOrEmpty(imageLocator.fileName), "[mod.io] imageFileName parameter needs to be not null or empty (used as identifier for gallery images)");

            CacheClient.LoadModGalleryImage(modId, imageLocator.fileName, size, (cachedImageTexture) =>
            {
                if(cachedImageTexture != null)
                {
                    if(onSuccess != null) { onSuccess.Invoke(cachedImageTexture); }
                }
                else
                {
                    // - Fetch from Server -
                    var download = DownloadClient.DownloadModGalleryImage(imageLocator,
                                                                          size);

                    download.succeeded += (d) =>
                    {
                        CacheClient.SaveModGalleryImage(modId,
                                                        imageLocator.fileName,
                                                        size,
                                                        d.imageTexture,
                                                        null);
                    };

                    download.succeeded += (d) => onSuccess(d.imageTexture);
                    download.failed += (d) => onError(d.error);
                }
            });
        }

        /// <summary>Fetches and caches a Mod YouTube Thumbnail (if not already cached).</summary>
        public static void GetModYouTubeThumbnail(int modId,
                                                  string youTubeVideoId,
                                                  Action<Texture2D> onSuccess,
                                                  Action<WebRequestError> onError)
        {
            Debug.Assert(!String.IsNullOrEmpty(youTubeVideoId),
                         "[mod.io] youTubeVideoId parameter must not be null or empty.");

            CacheClient.LoadModYouTubeThumbnail(modId, youTubeVideoId, (cachedYouTubeThumbnail) =>
            {
                if(cachedYouTubeThumbnail != null)
                {
                    if(onSuccess != null) { onSuccess.Invoke(cachedYouTubeThumbnail); }
                }
                else
                {
                    var download = DownloadClient.DownloadYouTubeThumbnail(youTubeVideoId);

                    download.succeeded += (d) =>
                    {
                        CacheClient.SaveModYouTubeThumbnail(modId, youTubeVideoId, d.imageTexture,
                                                            null);
                    };

                    download.succeeded += (d) => onSuccess(d.imageTexture);
                    download.failed += (d) => onError(d.error);
                }
            });
        }

        #endregion

        #region Mod Files
        //-------------------------------------//
        // -----------[ MODFILES ]--------------
        //-------------------------------------//
        /// <summary>Fetches and caches a Modfile (if not already cached).</summary>
        public static void GetModfile(int modId, int modfileId,
                                      Action<Modfile> onSuccess,
                                      Action<WebRequestError> onError)
        {
            CacheClient.LoadModfile(modId, modfileId, (cachedModfile) =>
            {
                if(cachedModfile != null)
                {
                    if(onSuccess != null) { onSuccess(cachedModfile); }
                }
                else
                {
                    // - Fetch from Server -
                    Action<Modfile> onGetModfile = (modfile) =>
                    {
                        CacheClient.SaveModfile(modfile, null);
                        if(onSuccess != null) { onSuccess(modfile); }
                    };

                    APIClient.GetModfile(modId, modfileId,
                                         onGetModfile,
                                         onError);
                }
            });
        }

        #endregion

        #region Mod Statistics
        //-------------------------------------//
        // ------------[ MOD STATS ]------------
        //-------------------------------------//
        /// <summary>Fetches and caches a Mod's Statistics (if not already cached or if expired).</summary>
        public static void GetModStatistics(int modId,
                                            Action<ModStatistics> onSuccess,
                                            Action<WebRequestError> onError)
        {
            CacheClient.LoadModStatistics(modId, (cachedStats) =>
            {
                if(cachedStats != null
                   && cachedStats.dateExpires > ServerTimeStamp.Now)
                {
                    if(onSuccess != null) { onSuccess(cachedStats); }
                }
                else
                {
                    // - Fetch from Server -
                    Action<ModStatistics> onGetStats = (stats) =>
                    {
                        CacheClient.SaveModStatistics(stats, null);
                        if(onSuccess != null) { onSuccess(stats); }
                    };

                    APIClient.GetModStats(modId,
                                          onGetStats,
                                          onError);
                }
            });
        }

        #endregion

        #region User Avatar
        //-------------------------------------//
        // --------------[ USERS ]-------------
        //-------------------------------------//
        public static void GetAuthenticatedUserProfile(Action<UserProfile> onSuccess,
                                                       Action<WebRequestError> onError)
        {
            if (LocalUser.Profile == null
               && LocalUser.AuthenticationState == AuthenticationState.ValidToken)
            {
                UserAccountManagement.UpdateUserProfile(onSuccess, onError);
            }
            else if (onSuccess != null)
            {
                onSuccess(LocalUser.Profile);
            }
        }

        /// <summary>Fetches all mods associated with the authenticated user.</summary>
        public static void FetchAuthenticatedUserMods(Action<List<ModProfile>> onSuccess,
                                                      Action<WebRequestError> onError)
        {
            RequestFilter userModsFilter = new RequestFilter();
            userModsFilter.AddFieldFilter(GetUserModFilterFields.gameId, new EqualToFilter<int>()
            {
                filterValue = PluginSettings.GAME_ID,
            });

            Action<List<ModProfile>> onGetMods = (modProfiles) =>
            {
                List<int> modIds = new List<int>(modProfiles.Count);
                foreach (ModProfile profile in modProfiles)
                {
                    modIds.Add(profile.id);
                }

                if (onSuccess != null) { onSuccess(modProfiles); }
            };

            // - Get All Events -
            ModManager.FetchAllResultsForQuery<ModProfile>((p, s, e) => APIClient.GetUserMods(userModsFilter, p, s, e),
                                                           onGetMods,
                                                           onError);
        }

        /// <summary>Fetches and caches a User Avatar (if not already cached).</summary>
        public static void GetUserAvatar(UserProfile profile,
                                         UserAvatarSize size,
                                         Action<Texture2D> onSuccess,
                                         Action<WebRequestError> onError)
        {
            Debug.Assert(profile != null, "[mod.io] User profile must not be null");

            ModManager.GetUserAvatar(profile.id, profile.avatarLocator, size,
                                     onSuccess, onError);
        }

        /// <summary>Fetches and caches a User Avatar (if not already cached).</summary>
        public static void GetUserAvatar(int userId,
                                         AvatarImageLocator avatarLocator,
                                         UserAvatarSize size,
                                         Action<Texture2D> onSuccess,
                                         Action<WebRequestError> onError)
        {
            Debug.Assert(avatarLocator != null);

            CacheClient.LoadUserAvatar(userId, size, (cachedAvatarTexture) =>
            {
                if(cachedAvatarTexture != null)
                {
                    if(onSuccess != null)
                    {
                        onSuccess.Invoke(cachedAvatarTexture);
                    }
                }
                else
                {
                    // - Fetch from Server -
                    var download = DownloadClient.DownloadImage(avatarLocator.GetSizeURL(size));

                    download.succeeded += (d) =>
                    {
                        CacheClient.SaveUserAvatar(userId, size, d.imageTexture, null);
                    };

                    download.succeeded += (d) => onSuccess(d.imageTexture);

                    if(onError != null)
                    {
                        download.failed += (d) => onError(d.error);
                    }
                }
            });
        }
        #endregion

        #region Events
        //-------------------------------------//
        // -------------[ EVENTS ]-------------
        //-------------------------------------//
        /// <summary>Fetches all mod events for the game.</summary>
        public static void FetchAllModEvents(int fromTimeStamp,
                                             int untilTimeStamp,
                                             Action<List<ModEvent>> onSuccess,
                                             Action<WebRequestError> onError)
        {
            ModManager.FetchModEvents(null, fromTimeStamp, untilTimeStamp,
                                      onSuccess, onError);
        }

        /// <summary>Fetches all mod events for the given mod ids.</summary>
        public static void FetchModEvents(IEnumerable<int> modIdFilter,
                                          int fromTimeStamp,
                                          int untilTimeStamp,
                                          Action<List<ModEvent>> onSuccess,
                                          Action<WebRequestError> onError)
        {
            // init
            int[] modIdFilterArray = null;
            if(modIdFilter != null)
            {
                modIdFilterArray = modIdFilter.ToArray();
            }

            // early out
            if(modIdFilterArray != null
               && modIdFilterArray.Length == 0)
            {
                if(onSuccess != null)
                {
                    onSuccess.Invoke(new List<ModEvent>());
                }

                return;
            }

            // - Filter -
            RequestFilter modEventFilter = new RequestFilter();
            modEventFilter.sortFieldName = GetAllModEventsFilterFields.id;
            modEventFilter.isSortAscending = false;

            modEventFilter.AddFieldFilter(GetAllModEventsFilterFields.dateAdded, new MinimumFilter<int>()
            {
                minimum = fromTimeStamp,
                isInclusive = false,
            });
            modEventFilter.AddFieldFilter(GetAllModEventsFilterFields.dateAdded, new MaximumFilter<int>()
            {
                maximum = untilTimeStamp,
                isInclusive = true,
            });

            if(modIdFilterArray != null)
            {
                modEventFilter.AddFieldFilter(GetAllModEventsFilterFields.modId, new InArrayFilter<int>()
                {
                    filterArray = modIdFilterArray,
                });
            }

            var pagination = new APIPaginationParameters()
            {
                limit = APIPaginationParameters.LIMIT_MAX,
                offset = 0,
            };

            // - Get All Events -
            APIClient.GetAllModEvents(modEventFilter, pagination,
                                      (r) => ModManager._OnModEventSuccess(r, onSuccess),
                                      onError);
        }

        /// <summary>Fetches all mod events after the given event id.</summary>
        public static void FetchModEventsAfterId(int eventId,
                                                 IEnumerable<int> modIdFilter,
                                                 Action<List<ModEvent>> onSuccess,
                                                 Action<WebRequestError> onError)
        {
            // init
            int[] modIdFilterArray = null;
            if(modIdFilter != null)
            {
                modIdFilterArray = modIdFilter.ToArray();
            }

            // early out
            if(modIdFilterArray != null
               && modIdFilterArray.Length == 0)
            {
                if(onSuccess != null)
                {
                    onSuccess.Invoke(new List<ModEvent>());
                }

                return;
            }

            // - Filter -
            RequestFilter modEventFilter = new RequestFilter();
            modEventFilter.sortFieldName = GetAllModEventsFilterFields.id;
            modEventFilter.isSortAscending = false;

            modEventFilter.AddFieldFilter(GetAllModEventsFilterFields.id, new MinimumFilter<int>()
            {
                minimum = eventId,
                isInclusive = false,
            });

            if(modIdFilterArray != null)
            {
                modEventFilter.AddFieldFilter(GetAllModEventsFilterFields.modId, new InArrayFilter<int>()
                {
                    filterArray = modIdFilterArray,
                });
            }

            var pagination = new APIPaginationParameters()
            {
                limit = APIPaginationParameters.LIMIT_MAX,
                offset = 0,
            };

            // - Get All Events -
            APIClient.GetAllModEvents(modEventFilter, pagination,
                                      (r) => ModManager._OnModEventSuccess(r, onSuccess),
                                      onError);
        }

        /// <summary>Processes results from the events fetch results.</summary>
        private static void _OnModEventSuccess(RequestPage<ModEvent> r,
                                               Action<List<ModEvent>> onSuccess)
        {
            if(onSuccess != null)
            {
                List<ModEvent> eventList = new List<ModEvent>();
                if(r != null
                   && r.items != null
                   && r.items.Length > 0)
                {
                    eventList = new List<ModEvent>(r.items);
                }

                onSuccess.Invoke(eventList);
            }
        }

        /// <summary>Fetches all user events for the authenticated user.</summary>
        public static void FetchAllUserEvents(int fromTimeStamp,
                                              int untilTimeStamp,
                                              Action<List<UserEvent>> onSuccess,
                                              Action<WebRequestError> onError)
        {
            // - Filter -
            RequestFilter userEventFilter = new RequestFilter();
            userEventFilter.sortFieldName = GetUserEventsFilterFields.id;
            userEventFilter.isSortAscending = false;

            userEventFilter.AddFieldFilter(GetUserEventsFilterFields.dateAdded, new MinimumFilter<int>()
            {
                minimum = fromTimeStamp,
                isInclusive = false,
            });
            userEventFilter.AddFieldFilter(GetUserEventsFilterFields.dateAdded, new MaximumFilter<int>()
            {
                maximum = untilTimeStamp,
                isInclusive = true,
            });

            userEventFilter.AddFieldFilter(GetUserEventsFilterFields.gameId, new EqualToFilter<int>()
            {
                filterValue = PluginSettings.GAME_ID,
            });

            var pagination = new APIPaginationParameters()
            {
                limit = APIPaginationParameters.LIMIT_MAX,
                offset = 0,
            };

            // - Get All Events -
            APIClient.GetUserEvents(userEventFilter, pagination,
                                    (r) => ModManager._OnUserEventsFetched(r, onSuccess),
                                    onError);
        }

        /// <summary>Fetches all user events for the authenticated user.</summary>
        public static void FetchUserEventsAfterId(int eventId,
                                                  Action<List<UserEvent>> onSuccess,
                                                  Action<WebRequestError> onError)
        {
            // - Filter -
            RequestFilter userEventFilter = new RequestFilter();
            userEventFilter.sortFieldName = GetUserEventsFilterFields.id;
            userEventFilter.isSortAscending = false;

            userEventFilter.AddFieldFilter(GetUserEventsFilterFields.id, new MinimumFilter<int>()
            {
                minimum = eventId,
                isInclusive = false,
            });

            userEventFilter.AddFieldFilter(GetUserEventsFilterFields.gameId, new EqualToFilter<int>()
            {
                filterValue = PluginSettings.GAME_ID,
            });

            var pagination = new APIPaginationParameters()
            {
                limit = APIPaginationParameters.LIMIT_MAX,
                offset = 0,
            };

            // - Get All Events -
            APIClient.GetUserEvents(userEventFilter, pagination,
                                    (r) => ModManager._OnUserEventsFetched(r, onSuccess),
                                    onError);
        }

        /// <summary>Processes results from the events fetch results.</summary>
        private static void _OnUserEventsFetched(RequestPage<UserEvent> r,
                                                 Action<List<UserEvent>> onSuccess)
        {
            if(onSuccess != null)
            {
                List<UserEvent> eventList = new List<UserEvent>();
                if(r != null
                   && r.items != null
                   && r.items.Length > 0)
                {
                    eventList = new List<UserEvent>(r.items);
                }

                onSuccess.Invoke(eventList);
            }
        }
        #endregion

        #region Uploading Mods
        //-------------------------------------//
        // ---------[ UPLOADING ]---------------
        //-------------------------------------//
        /// <summary>Submits a new mod to the server.</summary>
        public static void SubmitNewMod(EditableModProfile newModProfile,
                                        Action<ModProfile> onSuccess,
                                        Action<WebRequestError> onError)
        {
            ModManager_SubmitModOperation op = new ModManager_SubmitModOperation();
            op.onSuccess = onSuccess;
            op.onError = onError;

            op.SubmitNewMod(newModProfile);
        }

        /// <summary>Submits changes to a mod to the server.</summary>
        public static void SubmitModChanges(int modId,
                                            EditableModProfile modEdits,
                                            Action<ModProfile> onSuccess,
                                            Action<WebRequestError> onError)
        {
            ModManager_SubmitModOperation op = new ModManager_SubmitModOperation();
            op.onSuccess = onSuccess;
            op.onError = onError;

            op.SubmitModChanges(modId, modEdits);
        }

        /// <summary>Zips and uploads a mod data directory as a new build to the servers.</summary>
        public static void UploadModBinaryDirectory(int modId,
                                                    EditableModfile modfileValues,
                                                    string binaryDirectory,
                                                    bool setActiveBuild,
                                                    Action<Modfile> onSuccess,
                                                    Action<WebRequestError> onError)
        {
            DataStorage.GetDirectoryExists(binaryDirectory, (dir_path, dir_exists) =>
            {
                if(dir_exists)
                {
                    if(IOUtilities.PathEndsWithDirectorySeparator(binaryDirectory))
                    {
                        binaryDirectory = binaryDirectory.Remove(binaryDirectory.Length - 1);
                    }

                    // - Zip Directory -
                    DataStorage.GetFiles(binaryDirectory, null, true, (path, success, fileList) =>
                    {
                        ModManager.UploadModBinaryFileList(modId, modfileValues, binaryDirectory,
                                                           fileList, setActiveBuild,
                                                           onSuccess, onError);
                    });
                }
                else
                {
                    if(onError != null)
                    {
                        onError(WebRequestError.GenerateLocal("Mod Binary directory [" + binaryDirectory + "] doesn't exist"));
                    }
                }
            });
        }

        /// <summary>Zips and uploads a collection of files as a new build to the servers.</summary>
        public static void UploadModBinaryFileList(int modId,
                                                   EditableModfile modfileValues,
                                                   string rootDirectory,
                                                   IList<string> fileList,
                                                   bool setActiveBuild,
                                                   Action<Modfile> onSuccess,
                                                   Action<WebRequestError> onError)
        {
            Debug.Assert(modId != ModProfile.NULL_ID);
            Debug.Assert(modfileValues != null);

            if(string.IsNullOrEmpty(rootDirectory))
            {
                if(onError != null)
                {
                    var error = WebRequestError.GenerateLocal("Unable to upload mod binary file list as the"
                                                              + " root directory was NULL or empty.");
                    onError.Invoke(error);
                }
            }
            else if(fileList == null || fileList.Count == 0)
            {
                if(onError != null)
                {
                    var error = WebRequestError.GenerateLocal("Unable to upload mod binary file list as the"
                                                              + " file list was NULL or empty.");
                    onError.Invoke(error);
                }
            }
            else if(modfileValues == null)
            {
                if(onError != null)
                {
                    var error = WebRequestError.GenerateLocal("Unable to upload mod binary file list as the"
                                                              + " modfile data was NULL.");
                    onError.Invoke(error);
                }
            }
            else
            {
                int rootDirectoryLength = rootDirectory.Length;
                if(!IOUtilities.PathEndsWithDirectorySeparator(rootDirectory))
                {
                    ++rootDirectoryLength;
                }

                string archiveDirPath = IOUtilities.CombinePath(Application.temporaryCachePath, "modio");
                string archiveFilePath = IOUtilities.CombinePath(archiveDirPath,
                                                                 DateTime.Now.ToFileTime() + "_" + modId.ToString() + ".zip");

                DataStorage.CreateDirectory(archiveDirPath, (path, success) =>
                {
                    if(success)
                    {
                        success = CompressionModule.CompressFileCollection(rootDirectory, fileList, archiveFilePath);

                        if(success)
                        {
                            UploadModBinary_Zipped(modId, modfileValues, archiveFilePath, setActiveBuild, onSuccess, onError);
                        }
                    }

                    if(!success)
                    {
                        if(onError != null)
                        {
                            WebRequestError error = WebRequestError.GenerateLocal("Unable to zip mod binary prior to uploading");
                            onError.Invoke(error);
                        }
                    }
                });
            }
        }

        /// <summary>Zips and uploads a mod data file as a new build to the servers.</summary>
        public static void UploadModBinary_Unzipped(int modId,
                                                    EditableModfile modfileValues,
                                                    string unzippedBinaryLocation,
                                                    bool setActiveBuild,
                                                    Action<Modfile> onSuccess,
                                                    Action<WebRequestError> onError)
        {
            string binaryZipLocation = IOUtilities.CombinePath(Application.temporaryCachePath,
                                                               "modio",
                                                               Path.GetFileNameWithoutExtension(unzippedBinaryLocation) + "_" + DateTime.Now.ToFileTime() + ".zip");
            bool zipSucceeded = false;

            DataStorage.CreateDirectory(Path.GetDirectoryName(binaryZipLocation), (path, success) =>
            {
                zipSucceeded = CompressionModule.CompressFile(unzippedBinaryLocation, binaryZipLocation);

                if(zipSucceeded)
                {
                    UploadModBinary_Zipped(modId, modfileValues, binaryZipLocation, setActiveBuild, onSuccess, onError);
                }
                else
                {
                    if(onError != null)
                    {
                        WebRequestError error = WebRequestError.GenerateLocal("Unable to zip mod binary prior to uploading");
                        onError.Invoke(error);
                    }
                }
            });
        }

        /// <summary>Uploads a zipped mod binary as a new build to the servers.</summary>
        public static void UploadModBinary_Zipped(int modId,
                                                  EditableModfile modfileValues,
                                                  string binaryZipLocation,
                                                  bool setActiveBuild,
                                                  Action<Modfile> onSuccess,
                                                  Action<WebRequestError> onError)
        {
            DataStorage.ReadFile(binaryZipLocation, (rf_path, rf_success, rf_data) =>
            {
                string buildFilename = Path.GetFileName(binaryZipLocation);
                var parameters = new AddModfileParameters();
                parameters.zippedBinaryData = BinaryUpload.Create(buildFilename, rf_data);
                if(modfileValues.version.isDirty)
                {
                    parameters.version = modfileValues.version.value;
                }
                if(modfileValues.changelog.isDirty)
                {
                    parameters.changelog = modfileValues.changelog.value;
                }
                if(modfileValues.metadataBlob.isDirty)
                {
                    parameters.metadataBlob = modfileValues.metadataBlob.value;
                }

                parameters.isActiveBuild = setActiveBuild;

                // - Generate Hash -
                DataStorage.GetFileSizeAndHash(binaryZipLocation, (fi_path, fi_success, fi_fileSize, fi_hash) =>
                {
                    parameters.fileHash = fi_hash;
                    APIClient.AddModfile(modId, parameters, onSuccess, onError);
                });

            });
        }
        #endregion

        #region Fetching Query Results
        //-------------------------------------//
        // -----[ FETCH ALL RESULTS HELPER ]-----
        //-------------------------------------//
        /// <summary>Parameter definition for FetchAllResultsForQuery.</summary>
        private delegate void GetAllObjectsQuery<T>(APIPaginationParameters pagination,
                                                    Action<RequestPage<T>> onSuccess,
                                                    Action<WebRequestError> onError);

        /// <summary>Fetches all the results for a paged API query.</summary>
        private static void FetchAllResultsForQuery<T>(GetAllObjectsQuery<T> query,
                                                       Action<List<T>> onSuccess,
                                                       Action<WebRequestError> onError)
        {
            var pagination = new APIPaginationParameters()
            {
                limit = APIPaginationParameters.LIMIT_MAX,
                offset = 0,
            };

            var results = new List<T>();
            int requestCount = 0;

            query(pagination,
                  (r) => FetchQueryResultsRecursively(query,
                                                      r,
                                                      pagination,
                                                      results,
                                                      requestCount,
                                                      onSuccess,
                                                      onError),
                  onError);
        }

        /// <summary>Fetches all the results for a paged API query.</summary>
        private static void FetchQueryResultsRecursively<T>(GetAllObjectsQuery<T> query,
                                                            RequestPage<T> queryResult,
                                                            APIPaginationParameters pagination,
                                                            List<T> culmativeResults,
                                                            int requestCount,
                                                            Action<List<T>> onSuccess,
                                                            Action<WebRequestError> onError)
        {
            Debug.Assert(pagination.limit > 0);

            culmativeResults.AddRange(queryResult.items);
            ++requestCount;

            if(queryResult.items.Length < queryResult.size
               || requestCount > 10)
            {
                onSuccess(culmativeResults);
            }
            else
            {
                pagination.offset += pagination.limit;

                query(pagination,
                      (r) => FetchQueryResultsRecursively(query,
                                                          r,
                                                          pagination,
                                                          culmativeResults,
                                                          requestCount,
                                                          onSuccess,
                                                          onError),
                      onError);
            }
        }
        #endregion

        // ---------[ OBSOLETE ]---------
        #pragma warning disable 0067
        /// <summary>[Obsolete] Install directory used by the ModManager.</summary>
        [Obsolete("Use DataStorage.INSTALLATION_DIRECTORY instead")]
        public static string installationDirectory
        {
            get { return DataStorage.INSTALLATION_DIRECTORY; }
        }

        /// <summary>[Obsolete] An event that notifies listeners that a mod has been uninstalled.</summary>
        [Obsolete("Use ModManager.onModBinariesUninstalled instead.", false)]
        public static event Action<ModfileIdPair> onModBinaryUninstalled;

        /// <summary>[Obsolete] Fetches the list of mods associated with the authenticated user.</summary>
        [Obsolete("Use ModManager.FetchAuthenticatedUserMods() instead.")]
        public static void GetAuthenticatedUserMods(Action<List<ModProfile>> onSuccess,
                                                    Action<WebRequestError> onError)
        {
            ModManager.FetchAuthenticatedUserMods(onSuccess, onError);
        }


        /// <summary>[Obsolete] Downloads and installs all installed mods.</summary>
        [Obsolete("Use ModManager.DownloadAndUpdateSubscribedMods_Coroutine() instead.")]
        public static System.Collections.IEnumerator UpdateAllInstalledMods_Coroutine()
        {
            List<ModfileIdPair> installedModVersions = ModManager.GetInstalledModVersions(false);

            List<int> modIds = new List<int>(installedModVersions.Count);
            foreach(ModfileIdPair pair in installedModVersions)
            {
                modIds.Add(pair.modId);
            }

            return ModManager.DownloadAndUpdateMods_Coroutine(modIds, null);
        }

        /// <summary>[Obsolete] Fetches and caches a User Profile (if not already cached).</summary>
        [Obsolete("No longer supported by the mod.io API.")]
        public static void GetUserProfile(int userId,
                                          Action<UserProfile> onSuccess,
                                          Action<WebRequestError> onError)
        {
            Debug.Assert(userId != UserProfile.NULL_ID);

            if(UserAuthenticationData.instance.userId == userId)
            {
                var cachedProfile = CacheClient.LoadUserProfile(userId);

                if(cachedProfile != null)
                {
                    if(onSuccess != null) { onSuccess(cachedProfile); }
                }
                else
                {
                    // - Fetch from Server -
                    Action<UserProfile> onGetUser = (profile) =>
                    {
                        CacheClient.SaveUserProfile(profile);
                        if(onSuccess != null) { onSuccess(profile); }
                    };

                    APIClient.GetAuthenticatedUser(onGetUser, onError);
                }
            }
            else if(onError != null)
            {
                onError(WebRequestError.GenerateLocal("Non-authenticated user profiles can no-longer be fetched."));
            }
        }

        /// <summary>[Obsolete] Returns the enabled mods.</summary>
        [Obsolete("Refer to LocalUser.EnabledModIds instead.")]
        public static List<int> GetEnabledModIds()
        {
            return LocalUser.EnabledModIds;
        }
        /// <summary>[Obsolete] Sets the enabled mods and writes the data to disk.</summary>
        [Obsolete("Refer to LocalUser.EnabledModIds instead.")]
        public static void SetEnabledModIds(IEnumerable<int> modIds)
        {
            if(modIds == null) { modIds = new int[0]; }
            LocalUser.EnabledModIds = new List<int>(modIds);
            LocalUser.Save();
        }

        /// <summary>[Obsolete]Returns the subscribed mods.</summary>
        [Obsolete("Refer to LocalUser.SubscribedModIds instead.")]
        public static List<int> GetSubscribedModIds()
        {
            return LocalUser.SubscribedModIds;
        }
        /// <summary>[Obsolete]Sets the subscribed mods and writes the data to disk.</summary>
        [Obsolete("Refer to LocalUser.SubscribedModIds instead.")]
        public static void SetSubscribedModIds(IEnumerable<int> modIds)
        {
            if(modIds == null) { modIds = new int[0]; }
            LocalUser.SubscribedModIds = new List<int>(modIds);
            LocalUser.Save();
        }

        /// <summary>[Obsolete] Removes all versions of a mod from the installs folder.</summary>
        [Obsolete("Use UninstallMod() instead.")]
        public static bool TryUninstallAllModVersions(int modId)
        {
            bool succeeded = false;

            ModManager.UninstallMod(modId, (s) => succeeded = s);

            return succeeded;
        }

        /// <summary>[Obsolete] Extracts a mod archive to the installs folder and removes other installed versions.</summary>
        [Obsolete("Use TryInstallMod(int, int, Action<bool>) instead.")]
        public static bool TryInstallMod(int modId, int modfileId, bool removeArchiveOnSuccess)
        {
            bool result = false;

            ModManager.TryInstallMod(modId, modfileId, (b) => result = b);
            return result;
        }

        /// <summary>[Obsolete] Removes a specific version of a mod from the installs folder.</summary>
        [Obsolete("Use TryUninstallModVersion(int, int, Action<bool>) instead.")]
        public static bool TryUninstallModVersion(int modId, int modfileId)
        {
            bool result = false;

            ModManager.TryUninstallModVersion(modId, modfileId, (b) => result = b);
            return result;
        }

        /// <summary>[Obsolete] Returns all of the mod directories of installed mods.</summary>
        [Obsolete("Use QueryInstalledModDirectories(bool, Action<List<string>>) instead.")]
        public static List<string> GetInstalledModDirectories(bool excludeDisabledMods)
        {
            List<string> result = null;

            ModManager.QueryInstalledModDirectories(excludeDisabledMods, (r) => result = r);
            return result;
        }

        /// <summary>[Obsolete] Returns all of the mod version info of installed mods.</summary>
        [Obsolete("Use QueryInstalledModVersions(bool, Action<List<ModfileIdPair>>) instead.")]
        public static List<ModfileIdPair> GetInstalledModVersions(bool excludeDisabledMods)
        {
            List<ModfileIdPair> result = null;

            ModManager.QueryInstalledModVersions(excludeDisabledMods, (r) => result = r);
            return result;
        }

        /// <summary>[Obsolete] Returns the data of all the mods installed.</summary>
        [Obsolete("Use QueryInstalledMods(bool, Action<IList<ModfileIdPair, string>>) instead.")]
        public static IEnumerable<KeyValuePair<ModfileIdPair, string>> IterateInstalledMods(IList<int> modIdFilter)
        {
            IEnumerable<KeyValuePair<ModfileIdPair, string>> result = null;

            ModManager.QueryInstalledMods(modIdFilter, (r) => result = r);
            return result;
        }


        /// <summary>[Obsolete] Fetches a list of mod profiles checking the cache, storing if subscribed.</summary>
        [Obsolete("Use GetModProfiles(IList<int>, Action<ModProfile[]>, Action<WebRequestError>) instead.")]
        public static void GetModProfiles(IList<int> orderedIdList,
                                          Action<List<ModProfile>> onSuccess,
                                          Action<WebRequestError> onError)
        {
            ModManager.GetModProfiles(orderedIdList,
            (ModProfile[] result) =>
            {
                if(onSuccess != null)
                {
                    List<ModProfile> profiles = null;
                    if(result != null)
                    {
                        profiles = new List<ModProfile>(result);
                    }

                    onSuccess.Invoke(profiles);
                }
            }, onError);
        }

        #pragma warning restore 0067
    }
}
