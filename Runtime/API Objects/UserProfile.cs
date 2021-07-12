using Newtonsoft.Json;

namespace ModIO
{
    [System.Serializable]
    public class UserProfile
    {
        // ---------[ CONSTANTS ]---------
        /// <summary>An id value indicating an invalid profile.</summary>
        public const int NULL_ID = -1;

        /// <summary>Maximum length for the username supported.</summary>
        public const int USERNAME_MAXLENGTH = 20;

        // ---------[ FIELDS ]---------
        /// <summary>Unique id for the user.</summary>
        [JsonProperty("id")]
        public int id;

        /// <summary>Unique text identifier for the user.</summary>
        [JsonProperty("name_id")]
        public string nameId;

        /// <summary>Username of the user.</summary>
        [JsonProperty("username")]
        public string username;

        /// <summary>Locator for the user's avatar.</summary>
        [JsonProperty("avatar")]
        public AvatarImageLocator avatarLocator;

        /// <summary>Unix timestamp of when the user was last online.</summary>
        [JsonProperty("date_online")]
        public int lastOnline;

        /// <summary>Timezone of the user.</summary>
        [JsonProperty("timezone")]
        public string timezone;

        /// <summary>User's language preference.</summary>
        [JsonProperty("language")]
        public string language;

        /// <summary>URL to the user's mod.io profile.</summary>
        [JsonProperty("profile_url")]
        public string profileURL;

        /// <summary>Display name of the user for the provided platform.</summary>
        [JsonProperty("display_name_portal")]
        public string usernamePlatform;
    }
}
