using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModIO.UI
{
    /// <summary>[Obsolete] Manages requests made for ModProfiles.</summary>
    [System.Obsolete("Functionality now available through ModManager.GetRangeOfModProfiles()")]
    public class ModProfileRequestManager : MonoBehaviour
    {
        // ---------[ SINGLETON ]---------
        /// <summary>Singleton instance.</summary>
        private static ModProfileRequestManager _instance = null;
        /// <summary>Singleton instance.</summary>
        public static ModProfileRequestManager instance
        {
            get
            {
                if(ModProfileRequestManager._instance == null)
                {
                    ModProfileRequestManager._instance = UIUtilities.FindComponentInAllScenes<ModProfileRequestManager>(true);

                    if(ModProfileRequestManager._instance == null)
                    {
                        GameObject go = new GameObject("Mod Profile Request Manager");
                        ModProfileRequestManager._instance = go.AddComponent<ModProfileRequestManager>();
                    }
                }

                return ModProfileRequestManager._instance;
            }
        }

        // ---------[ FIELDS ]---------
        /// <summary>Minimum profile count to request from the API.</summary>
        public int minimumFetchSize = APIPaginationParameters.LIMIT_MAX;

        // ---------[ INITIALIZATION ]---------
        protected virtual void Awake()
        {
            if(ModProfileRequestManager._instance == null)
            {
                ModProfileRequestManager._instance = this;
            }
            #if DEBUG
            else if(ModProfileRequestManager._instance != this)
            {
                Debug.LogWarning("[mod.io] Second instance of a ModProfileRequestManager"
                                 + " component enabled simultaneously."
                                 + " Only one instance of a ModProfileRequestManager"
                                 + " component should be active at a time.");
                this.enabled = false;
            }
            #endif
        }

        // ---------[ FUNCTIONALITY ]---------
        /// <summary>Fetches page of ModProfiles grabbing from the cache where possible.</summary>
        public virtual void FetchModProfilePage(RequestFilter filter, int resultOffset, int profileCount,
                                                Action<RequestPage<ModProfile>> onSuccess,
                                                Action<WebRequestError> onError)
        {
            Debug.Assert(this.minimumFetchSize <= APIPaginationParameters.LIMIT_MAX);

            // early out if onSuccess == null
            if(onSuccess == null && onError == null) { return; }

            if(profileCount > APIPaginationParameters.LIMIT_MAX)
            {
                Debug.LogWarning("[mod.io] FetchModProfilePage has been called with a profileCount"
                                 + " larger than the APIPaginationParameters.LIMIT_MAX."
                                 + "\nAs such, results may not be as expected.");

                profileCount = APIPaginationParameters.LIMIT_MAX;
            }

            // ensure indicies are positive
            if(resultOffset < 0) { resultOffset = 0; }
            if(profileCount < 0) { profileCount = 0; }

            // setup request structures
            List<ModProfile> results = new List<ModProfile>(profileCount);

            // PaginationParameters
            APIPaginationParameters pagination = new APIPaginationParameters();

            int pageIndex = resultOffset / this.minimumFetchSize;
            pagination.offset = pageIndex * this.minimumFetchSize;
            pagination.limit = this.minimumFetchSize;

            APIClient.GetAllMods(filter, pagination,
            (r01) =>
            {
                int pageOffset = resultOffset % this.minimumFetchSize;

                for(int i = pageOffset;
                    i < r01.items.Length
                    && i < pageOffset + profileCount;
                    ++i)
                {
                    results.Add(r01.items[i]);
                }

                if(pageOffset + profileCount > r01.size
                   && r01.items.Length == r01.size)
                {
                    pagination.offset += pagination.limit;
                    APIClient.GetAllMods(filter, pagination,
                    (r02) =>
                    {
                        for(int i = 0;
                            i < r02.items.Length
                            && i < pageOffset + profileCount - r02.size;
                            ++i)
                        {
                            results.Add(r02.items[i]);
                            OnModsReceived(resultOffset, profileCount, r02.resultTotal,
                                           results, onSuccess);
                        }
                    }, onError);
                }
                else
                {
                    OnModsReceived(resultOffset, profileCount, r01.resultTotal,
                                   results, onSuccess);
                }
            }, onError);
        }

        /// <summary>Processes the received mod collection and generates a request page.</summary>
        private void OnModsReceived(int resultOffset, int pageSize, int resultTotal,
                                    List<ModProfile> results,
                                    Action<RequestPage<ModProfile>> onSuccess)
        {
            if(onSuccess == null) { return; }

            RequestPage<ModProfile> page = new RequestPage<ModProfile>()
            {
                size = pageSize,
                resultOffset = resultOffset,
                resultTotal = resultTotal,
                items = results.ToArray(),
            };

            onSuccess.Invoke(page);
        }

        /// <summary>Requests an individual ModProfile by id.</summary>
        public virtual void RequestModProfile(int id,
                                              Action<ModProfile> onSuccess,
                                              Action<WebRequestError> onError)
        {
            ModManager.GetModProfile(id, onSuccess, onError);
        }

        /// <summary>Requests a collection of ModProfiles by id.</summary>
        public virtual void RequestModProfiles(IList<int> orderedIdList,
                                               Action<ModProfile[]> onSuccess,
                                               Action<WebRequestError> onError)
        {
            ModManager.GetModProfiles(orderedIdList, onSuccess, onError);
        }
    }
}
