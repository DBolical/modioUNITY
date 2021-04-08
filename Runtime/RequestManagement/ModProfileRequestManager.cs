using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModIO.UI
{
    /// <summary>Manages requests made for ModProfiles.</summary>
    public class ModProfileRequestManager : MonoBehaviour, IModSubscriptionsUpdateReceiver
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

        // ---------[ NESTED DATA-TYPES ]--------
        public struct RequestPageData
        {
            public int resultOffset;
            public int resultTotal;
            public int[] modIds;

            /// <summary>Appends a collection of ids to a RequestPageData.</summary>
            public static RequestPageData Append(RequestPageData pageData,
                                                 int appendCollectionOffset,
                                                 int[] appendCollection)
            {
                if(appendCollection == null
                   || appendCollection.Length == 0)
                {
                    return pageData;
                }

                // asserts
                Debug.Assert(appendCollectionOffset >= 0);
                Debug.Assert(appendCollectionOffset + appendCollection.Length <= pageData.resultTotal);

                // calc last indicies
                int newOffset = (appendCollectionOffset < pageData.resultOffset
                                 ? appendCollectionOffset
                                 : pageData.resultOffset);

                int oldLastIndex = pageData.modIds.Length + pageData.resultOffset - 1;
                int appendingLastIndex = appendCollection.Length + appendCollectionOffset - 1;

                int newLastIndex = (appendingLastIndex > oldLastIndex
                                    ? appendingLastIndex
                                    : oldLastIndex);

                // fill array
                int[] newArray = new int[newLastIndex - newOffset + 1];
                for(int i = 0; i < newArray.Length; ++i)
                {
                    newArray[i] = ModProfile.NULL_ID;
                }

                Array.Copy(pageData.modIds, 0,
                           newArray, pageData.resultOffset - newOffset,
                           pageData.modIds.Length);
                Array.Copy(appendCollection, 0,
                           newArray, appendCollectionOffset - newOffset,
                           appendCollection.Length);

                // Create appended page data
                RequestPageData retData = new RequestPageData()
                {
                    resultOffset = newOffset,
                    resultTotal = pageData.resultTotal,
                    modIds = newArray,
                };

                return retData;
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
            Debug.Assert(onSuccess != null);
            Debug.Assert(this.minimumFetchSize <= APIPaginationParameters.LIMIT_MAX);

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

            // PaginationParameters
            APIPaginationParameters pagination = new APIPaginationParameters();
            pagination.offset = resultOffset;
            pagination.limit = profileCount;
            if(profileCount < this.minimumFetchSize)
            {
                pagination.limit = this.minimumFetchSize;
            }

            // Send Request
            APIClient.GetAllMods(filter, pagination,
            (r) =>
            {
                if(this != null)
                {
                    this.CacheRequestPage(filter, r);
                }

                if(onSuccess != null)
                {
                    if(pagination.limit != profileCount)
                    {
                        var subPage = ModProfileRequestManager.CreatePageSubset(r,
                                                                                resultOffset,
                                                                                profileCount);
                        onSuccess(subPage);
                    }
                    else
                    {
                        onSuccess(r);
                    }
                }
            }, onError);
        }

        /// <summary>Updates the cache - both on disk and in this object.</summary>
        public virtual void CacheModProfiles(IEnumerable<ModProfile> modProfiles)
        {
            if(modProfiles == null) { return; }

            // store
                IList<int> subMods = LocalUser.SubscribedModIds;
                foreach(ModProfile profile in modProfiles)
                {
                    if(profile != null
                       && subMods.Contains(profile.id))
                    {
                        CacheClient.SaveModProfile(profile, null);
                    }
                }
        }

        /// <summary>Append the response page to the cached data.</summary>
        public virtual void CacheRequestPage(RequestFilter filter, RequestPage<ModProfile> page)
        {
            // asserts
            Debug.Assert(filter != null);
            Debug.Assert(page != null);

            // cache profiles
            this.CacheModProfiles(page.items);
        }

        /// <summary>Requests an individual ModProfile by id.</summary>
        public virtual void RequestModProfile(int id,
                                              Action<ModProfile> onSuccess, Action<WebRequestError> onError)
        {
            Debug.Assert(onSuccess != null);

            CacheClient.LoadModProfile(id, (cachedProfile) =>
            {
                if(cachedProfile != null)
                {
                    onSuccess(cachedProfile);
                }
                else
                {
                    APIClient.GetMod(id, (p) =>
                    {
                        if(this != null)
                        {
                            if(LocalUser.SubscribedModIds.Contains(p.id))
                            {
                                CacheClient.SaveModProfile(p, null);
                            }
                        }

                        onSuccess(p);
                    },
                    onError);
                }
            });
        }

        /// <summary>Requests a collection of ModProfiles by id.</summary>
        public virtual void RequestModProfiles(IList<int> orderedIdList,
                                               Action<ModProfile[]> onSuccess,
                                               Action<WebRequestError> onError)
        {
            Debug.Assert(orderedIdList != null);
            Debug.Assert(onSuccess != null);

            ModProfile[] results = new ModProfile[orderedIdList.Count];
            List<int> missingIds = new List<int>(orderedIdList.Count);

            // grab from cache
            for(int i = 0; i < orderedIdList.Count; ++i)
            {
                int modId = orderedIdList[i];
                ModProfile profile = null;

                if(profile == null)
                {
                    missingIds.Add(modId);
                }
            }

            CacheClient.RequestFilteredModProfiles(missingIds, (cachedProfiles) =>
            {
                // check disk for any missing profiles
                foreach(ModProfile profile in cachedProfiles)
                {
                    int index = orderedIdList.IndexOf(profile.id);
                    if(index >= 0 && results[index] == null)
                    {
                        results[index] = profile;
                    }

                    missingIds.Remove(profile.id);
                }

                // if no missing profiles, early out
                if(missingIds.Count == 0)
                {
                    onSuccess(results);
                    return;
                }

                // fetch missing profiles
                Action<List<ModProfile>> onFetchProfiles = (modProfiles) =>
                {
                    if(this != null)
                    {
                        this.CacheModProfiles(modProfiles);
                    }

                    foreach(ModProfile profile in modProfiles)
                    {
                        int i = orderedIdList.IndexOf(profile.id);
                        if(i >= 0)
                        {
                            results[i] = profile;
                        }
                    }

                    onSuccess(results);
                };

                this.StartCoroutine(this.FetchAllModProfiles(missingIds.ToArray(),
                                                             onFetchProfiles,
                                                             onError));
            });
        }

        // ---------[ UTILITY ]---------
        /// <summary>Recursively fetches all of the mod profiles in the array.</summary>
        protected System.Collections.IEnumerator FetchAllModProfiles(int[] modIds,
                                                                     Action<List<ModProfile>> onSuccess,
                                                                     Action<WebRequestError> onError)
        {
            List<ModProfile> modProfiles = new List<ModProfile>();

            // pagination
            APIPaginationParameters pagination = new APIPaginationParameters()
            {
                limit = APIPaginationParameters.LIMIT_MAX,
                offset = 0,
            };

            // filter
            RequestFilter filter = new RequestFilter();
            filter.AddFieldFilter(API.GetAllModsFilterFields.id,
                new InArrayFilter<int>() { filterArray = modIds, });

            bool isDone = false;

            while(!isDone)
            {
                RequestPage<ModProfile> page = null;
                WebRequestError error = null;

                APIClient.GetAllMods(filter, pagination,
                                     (r) => page = r,
                                     (e) => error = e);

                while(page == null && error == null) { yield return null;}

                if(error != null)
                {
                    if(onError != null)
                    {
                        onError(error);
                    }

                    modProfiles = null;
                    isDone = true;
                }
                else
                {
                    modProfiles.AddRange(page.items);

                    if(page.resultTotal <= (page.resultOffset + page.size))
                    {
                        isDone = true;
                    }
                    else
                    {
                        pagination.offset = page.resultOffset + page.size;
                    }
                }
            }

            if(isDone && modProfiles != null)
            {
                onSuccess(modProfiles);
            }
        }

        protected static RequestPage<ModProfile> CreatePageSubset(RequestPage<ModProfile> sourcePage,
                                                                  int resultOffset,
                                                                  int profileCount)
        {
            Debug.Assert(sourcePage != null);

            RequestPage<ModProfile> subPage = new RequestPage<ModProfile>()
            {
                size = profileCount,
                resultOffset = resultOffset,
                resultTotal = sourcePage.resultTotal,
            };

            if(subPage.resultOffset < sourcePage.resultOffset)
            {
                int difference = sourcePage.resultOffset = subPage.resultOffset;

                subPage.resultOffset = sourcePage.resultOffset;
                subPage.size -= difference;
            }

            // early out for 0
            if(subPage.size <= 0)
            {
                subPage.size = 0;
                subPage.items = new ModProfile[0];
                return subPage;
            }

            int sourcePageOffset = subPage.resultOffset - sourcePage.resultOffset;

            int subPageItemCount = subPage.size;
            if(sourcePageOffset + subPageItemCount > sourcePage.items.Length)
            {
                subPageItemCount = sourcePage.items.Length - sourcePageOffset;
            }

            subPage.items = new ModProfile[subPageItemCount];

            Array.Copy(sourcePage.items, sourcePageOffset,
                       subPage.items, 0,
                       subPage.items.Length);

            return subPage;
        }

        protected ModProfile[] PullProfilesFromCache(IList<int> modIds)
        {
            Debug.Assert(modIds != null);

            ModProfile[] result = new ModProfile[modIds.Count];

            for(int i = 0; i < result.Length; ++i)
            {
                ModProfile profile = null;
                result[i] = profile;
            }

            return result;
        }

        // ---------[ EVENTS ]---------
        /// <summary>Stores any cached profiles when the mod subscriptions are updated.</summary>
        public void OnModSubscriptionsUpdated(IList<int> addedSubscriptions,
                                              IList<int> removedSubscriptions)
        {
            if(addedSubscriptions.Count > 0)
            {
                foreach(int modId in addedSubscriptions)
                {
                }
            }
        }
    }
}
