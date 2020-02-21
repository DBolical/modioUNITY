using System.Collections.Generic;

namespace ModIO
{
    /// <summary>Structure for storing data about a user specific to this device.</summary>
    public struct LocalUser
    {
        // ---------[ Singleton ]---------
        /// <summary>Singleton instance.</summary>
        public static LocalUser instance;

        /// <summary>Is the instance loaded?</summary>
        public static bool isLoaded;

        // ---------[ FIELDS ]---------
        /// <summary>mod.io User Profile.</summary>
        public UserProfile profile;

        /// <summary>User authentication token to send with API requests identifying the user.</summary>
        public string oAuthToken;

        /// <summary>A flag to indicate that the auth token has been rejected.</summary>
        public bool wasTokenRejected;

        /// <summary>Mods the user has enabled on this device.</summary>
        public List<int> enabledModIds;

        /// <summary>Mods the user is subscribed to.</summary>
        public List<int> subscribedModIds;

        /// <summary>Queued subscribe actions.</summary>
        public List<int> queuedSubscribes;

        /// <summary>Queued unsubscribe actions</summary>
        public List<int> queuedUnsubscribes;

        // ---------[ ACCESSORS ]---------
        /// <summary>Returns the summarised authentication state.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public AuthenticationState AuthenticationState
        {
            get
            {
                if(string.IsNullOrEmpty(this.oAuthToken))
                {
                    return AuthenticationState.NoToken;
                }
                else if(this.wasTokenRejected)
                {
                    return AuthenticationState.RejectedToken;
                }
                else
                {
                    return AuthenticationState.ValidToken;
                }
            }
        }

        // ---------[ Initialization ]---------
        /// <summary>Sets the initial Singleton values.</summary>
        static LocalUser()
        {
            LocalUser.isLoaded = false;
            LocalUser.instance = new LocalUser();
        }

        /// <summary>Loads the LocalUser instance.</summary>
        public static System.Collections.IEnumerator Load(System.Action callback)
        {
            bool isDone = false;

            LocalUser.isLoaded = false;

            UserDataStorage.TryReadJSONFile<LocalUser>(UserAccountManagement.USER_DATA_FILENAME, (success, fileData) =>
            {
                LocalUser.AssertListsNotNull(ref fileData);

                LocalUser.instance = fileData;
                LocalUser.isLoaded = success;

                if(callback != null) { callback.Invoke(); }
            });

            while(!isDone) { yield return null; }
        }

        /// <summary>Asserts that the list fields are not null.</summary>
        public static void AssertListsNotNull(ref LocalUser userData)
        {
            if(userData.enabledModIds == null
               || userData.subscribedModIds == null
               || userData.queuedSubscribes == null
               || userData.queuedUnsubscribes == null)
            {
                if(userData.enabledModIds == null)
                {
                    userData.enabledModIds = new List<int>();
                }
                if(userData.subscribedModIds == null)
                {
                    userData.subscribedModIds = new List<int>();
                }
                if(userData.queuedSubscribes == null)
                {
                    userData.queuedSubscribes = new List<int>();
                }
                if(userData.queuedUnsubscribes == null)
                {
                    userData.queuedUnsubscribes = new List<int>();
                }
            }
        }
    }
}
