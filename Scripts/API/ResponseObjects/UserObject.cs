using Newtonsoft.Json;

namespace ModIO.API
{
    [System.Serializable]
    public struct UserObject
    {
        // ---------[ FIELDS ]---------
        /// <summary>Unique id of the user.</summary>
        [JsonProperty("id")]
        public int id;
        
        /// <summary>Path for the user on mod.io.
        /// For example: https://mod.io/members/username-id-here
        /// Usually a simplified version of their username.</summary>
        [JsonProperty("name_id")]
        public string nameId;
        
        /// <summary>Username of the user.</summary>
        [JsonProperty("username")]
        public string username;
        
        /// <summary>Unix timestamp of date the user was last online.</summary>
        [JsonProperty("date_online")]
        public int dateOnline;
        
        /// <summary>Contains avatar data.</summary>
        [JsonProperty("avatar")]
        public AvatarObject avatar;
        
        /// <summary>Timezone of the user, format is country/city.</summary>
        [JsonProperty("timezone")]
        public string timezone;
        
        /// <summary>Users language preference. See localization for the
        /// supported languages.</summary>
        [JsonProperty("language")]
        public string language;
        
        /// <summary>URL to the user's mod.io profile.</summary>
        [JsonProperty("profile_url")]
        public string profileURL;
    }
}
