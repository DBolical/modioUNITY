using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModIO.UI
{
    // NOTE(@jackson): The functionality of this view makes the assumption that the number of items
    // to be displayed is low enough that it does not cause memory issues. Safeguards against this
    // will be made in a future update, but is currently not a priority.
    public class SubscriptionsView : MonoBehaviour, IModSubscriptionsUpdateReceiver, IBrowserView
    {
        // ---------[ NESTED DATA-TYPES ]---------
        /// <summary>Event for notifying listeners of a change to displayed mods.</summary>
        [Serializable]
        public class ModPageChanged : UnityEngine.Events.UnityEvent<RequestPage<ModProfile>> {}

        /// <summary>Event for notifying listeners of a change to title filter.</summary>
        [Serializable]
        public class FilterChanged : UnityEngine.Events.UnityEvent<string> {}

        /// <summary>Event for notifying listeners of a change to the sort delegate.</summary>
        [Serializable]
        public class SortChanged : UnityEngine.Events.UnityEvent<Comparison<ModProfile>> {}

        // ---------[ FIELDS ]---------
        /// <summary>The priority to focus the selectables.</summary>
        public List<Selectable> onFocusPriority = new List<Selectable>();

        [Header("UI Components")]
        /// <summary>Container used to display mods.</summary>
        public ModContainer modContainer = null;

        /// <summary>Object to display when there are no subscribed mods</summary>
        [Tooltip("Object to display when there are no subscribed mods")]
        public GameObject noSubscriptionsDisplay;

        /// <summary>Object to display when there are zero filtered results</summary>
        [Tooltip("Object to display when there are zero filtered results")]
        public GameObject noResultsDisplay;

        /// <summary>Object that should react to this view being enabled/disabled.</summary>
        public StateToggleDisplay isActiveIndicator;

        [Header("Events")]
        /// <summary>Event for notifying listeners of a change to displayed mods.</summary>
        public ModPageChanged onModPageChanged = null;

        /// <summary>Event for notifying listeners of a change to title filter.</summary>
        public FilterChanged onNameFieldFilterChanged = null;

        /// <summary>Event for notifying listeners of a change to sort delegate.</summary>
        public SortChanged onSortDelegateChanged = null;

        // --- Run-time Data ---
        /// <summary>RequestPage being displayed.</summary>
        private RequestPage<ModProfile> m_modPage = null;

        /// <summary>Name filter to apply to the subscribed mods.</summary>
        private string m_nameFieldFilter = string.Empty;

        /// <summary>Sort method for displaying subscribed mods.</summary>
        private Comparison<ModProfile> m_sortDelegate = null;

        // --- Accessors ---
        /// <summary>RequestPage being displayed.</summary>
        public RequestPage<ModProfile> modPage
        {
            get { return this.m_modPage; }
        }

        /// <summary>Name field filter currently applied to the SubscriptionsView.</summary>
        public string nameFieldFilter
        {
            get { return this.m_nameFieldFilter; }
        }

        /// <summary>Sort method for displaying subscribed mods.</summary>
        public Comparison<ModProfile> sortDelegate
        {
            get { return this.m_sortDelegate; }
        }

        // --- IBrowserView Implementation ---
        /// <summary>Canvas Group.</summary>
        public CanvasGroup canvasGroup
        { get { return this.gameObject.GetComponent<CanvasGroup>(); } }

        /// <summary>Reset selection on hide.</summary>
        bool IBrowserView.resetSelectionOnHide { get { return true; } }

        /// <summary>Is the view a root view or window view?</summary>
        bool IBrowserView.isRootView { get { return true; } }

        /// <summary>The priority to focus the selectables.</summary>
        List<Selectable> IBrowserView.onFocusPriority { get { return this.onFocusPriority; } }

        // ---------[ INITIALIZATION ]---------
        /// <summary>Collects and sets view on ISubscriptionsViewElements.</summary>
        protected virtual void Start()
        {
            #if UNITY_EDITOR
            SubscriptionsView[] nested = this.gameObject.GetComponentsInChildren<SubscriptionsView>(true);
            if(nested.Length > 1)
            {
                SubscriptionsView nestedView = nested[1];
                if(nestedView == this)
                {
                    nestedView = nested[0];
                }

                Debug.LogError("[mod.io] Nesting SubscriptionsViews is currently not supported due to the"
                               + " way ISubscriptionsViewElement component parenting works."
                               + "\nThe nested SubscriptionsViews must be removed to allow SubscriptionsView functionality."
                               + "\nthis=" + this.gameObject.name
                               + "\nnested=" + nestedView.gameObject.name,
                               this);
                return;
            }
            #endif

            // assign view elements to this
            var viewElementChildren = this.gameObject.GetComponentsInChildren<ISubscriptionsViewElement>(true);
            foreach(ISubscriptionsViewElement viewElement in viewElementChildren)
            {
                viewElement.SetSubscriptionsView(this);
            }

            // get page
            this.DisplayProfiles(null);
            this.Refresh();
        }

        private void OnEnable()
        {
            if(this.isActiveIndicator != null)
            {
                this.isActiveIndicator.isOn = true;
            }
        }
        private void OnDisable()
        {
            if(this.isActiveIndicator != null)
            {
                this.isActiveIndicator.isOn = false;
            }
        }

        public void Refresh()
        {
            // create null page
            this.m_modPage = null;
            if(this.onModPageChanged != null)
            {
                this.onModPageChanged.Invoke(this.m_modPage);
            }

            if(this.noSubscriptionsDisplay != null)
            {
                this.noSubscriptionsDisplay.gameObject.SetActive(true);
            }
            if(this.noResultsDisplay != null)
            {
                this.noResultsDisplay.gameObject.SetActive(false);
            }

            IList<int> subscribedModIds = LocalUser.SubscribedModIds;

            ModManager.GetModProfiles(subscribedModIds,
            (ModProfile[] profiles) =>
            {
                Refresh_OnGetModProfiles(profiles, this.m_nameFieldFilter, this.m_sortDelegate);
            },
            (WebRequestError requestError) =>
            {
                MessageSystem.QueueMessage(MessageDisplayData.Type.Warning,
                                           "Failed to get subscription data from mod.io servers.\n"
                                           + requestError.displayMessage);
            });
        }

        /// <summary>Handle the mods returned by the refresh request.</summary>
        protected virtual void Refresh_OnGetModProfiles(IList<ModProfile> modProfiles,
                                                        string requestedTitleFilter,
                                                        Comparison<ModProfile> requestedSortDelegate)
        {
            // check for early outs
            if(this == null
               || this.m_nameFieldFilter != requestedTitleFilter
               || this.m_sortDelegate != requestedSortDelegate)
            {
                return;
            }

            List<ModProfile> filteredList = null;

            // check for null list
            if(modProfiles == null
               || modProfiles.Count == 0)
            {
                filteredList = new List<ModProfile>(0);
            }
            else
            {
                // filter
                Func<ModProfile, bool> nameFieldFilterDelegate = (p) => true;
                if(!String.IsNullOrEmpty(requestedTitleFilter))
                {
                    // set initial value
                    string filterString = requestedTitleFilter.ToUpper();
                    nameFieldFilterDelegate = (p) =>
                    {
                        return p.name.ToUpper().Contains(filterString);
                    };
                }

                filteredList = new List<ModProfile>(modProfiles.Count);
                foreach(ModProfile profile in modProfiles)
                {
                    if(nameFieldFilterDelegate(profile))
                    {
                        filteredList.Add(profile);
                    }
                }

                // sort
                if(requestedSortDelegate == null)
                {
                    requestedSortDelegate = this.DefaultSortFunction;
                }

                filteredList.Sort(requestedSortDelegate);

            }

            // update displays
            this.DisplayProfiles(filteredList);

            if(this.noSubscriptionsDisplay != null)
            {
                this.noSubscriptionsDisplay.gameObject.SetActive(filteredList.Count == 0
                                                                 && string.IsNullOrEmpty(this.m_nameFieldFilter));
            }
            if(this.noResultsDisplay != null)
            {
                this.noResultsDisplay.gameObject.SetActive(filteredList.Count == 0
                                                           && !string.IsNullOrEmpty(this.m_nameFieldFilter));
            }

            // create request page
            this.m_modPage = new RequestPage<ModProfile>()
            {
                size = filteredList.Count,
                resultOffset = 0,
                resultTotal = filteredList.Count,
                items = filteredList.ToArray(),
            };
            if(this.onModPageChanged != null)
            {
                this.onModPageChanged.Invoke(this.m_modPage);
            }
        }

        // ---------[ UI FUNCTIONALITY ]------------
        /// <summary>Displays the profiles in the mod container.</summary>
        protected virtual void DisplayProfiles(IList<ModProfile> profiles)
        {
            Debug.Assert(this.modContainer != null);

            if(profiles == null)
            {
                profiles = new ModProfile[0];
            }

            // init vars
            int displayCount = profiles.Count;
            ModProfile[] displayProfiles = new ModProfile[displayCount];
            ModStatistics[] displayStats = new ModStatistics[displayCount];

            // build arrays
            for(int i = 0;
                i < displayCount;
                ++i)
            {
                ModProfile profile = profiles[i];
                ModStatistics stats = null;
                if(profile != null)
                {
                    stats = profile.statistics;
                }

                displayProfiles[i] = profile;
                displayStats[i] = stats;
            }

            // display
            this.modContainer.DisplayMods(displayProfiles, displayStats);
        }

        // ---------[ FILTER CONTROL ]---------
        /// <summary>Sets the title filter and refreshes the page.</summary>
        public void SetNameFieldFilter(string nameFieldFilter)
        {
            if(nameFieldFilter == null) { nameFieldFilter = string.Empty; }

            if(this.m_nameFieldFilter.ToUpper() != nameFieldFilter.ToUpper())
            {
                this.m_nameFieldFilter = nameFieldFilter;
                Refresh();

                if(this.onNameFieldFilterChanged != null)
                {
                    this.onNameFieldFilterChanged.Invoke(this.m_nameFieldFilter);
                }
            }
        }

        /// <summary>Gets the title filter string.</summary>
        public string GetTitleFilter() { return this.m_nameFieldFilter; }

        /// <summary>Sets the sort delegate and refreshes the page.</summary>
        public void SetSortDelegate(Comparison<ModProfile> sortDelegate)
        {
            if(this.m_sortDelegate != sortDelegate)
            {
                this.m_sortDelegate = sortDelegate;
                this.Refresh();
            }
        }

        /// <summary>Gets the sort delegate.</summary>
        public Comparison<ModProfile> GetSortDelegate() { return this.m_sortDelegate; }

        // ---------[ EVENTS ]---------
        public void OnModSubscriptionsUpdated(IList<int> addedSubscriptions,
                                              IList<int> removedSubscriptions)
        {
            this.Refresh();
        }

        // ---------[ UTILITY ]---------
        /// <summary>Provides a default sorting function for the subscription view.</summary>
        protected virtual int DefaultSortFunction(ModProfile a, ModProfile b)
        {
            if(a == null) { return 1; }
            if(b == null) { return -1; }
            return (a.id - b.id);
        }

        // ---------[ OBSOLETE ]---------
        [Obsolete("Use SubscriptionView.modContainer instead.")][HideInInspector]
        public GameObject itemPrefab = null;
        [Obsolete][HideInInspector]
        public ScrollRect scrollView;

        [Obsolete("Use ResultCountDisplay instead.")][HideInInspector]
        public Text resultCount;

        [Obsolete("No longer supported.")]
        public IEnumerable<ModView> modViews
        {
            get { return null; }
        }

        [Obsolete("No longer necessary. Initialization occurs in Start().")]
        public void Initialize() {}

        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> inspectRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifyInspectRequested(ModView view)
        {
            if(inspectRequested != null)
            {
                inspectRequested(view);
            }
        }
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> subscribeRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifySubscribeRequested(ModView view)
        {
            if(subscribeRequested != null)
            {
                subscribeRequested(view);
            }
        }
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> unsubscribeRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifyUnsubscribeRequested(ModView view)
        {
            if(unsubscribeRequested != null)
            {
                unsubscribeRequested(view);
            }
        }
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> enableModRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifyEnableRequested(ModView view)
        {
            if(enableModRequested != null)
            {
                enableModRequested(view);
            }
        }
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> disableModRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifyDisableRequested(ModView view)
        {
            if(disableModRequested != null)
            {
                disableModRequested(view);
            }
        }
    }
}
