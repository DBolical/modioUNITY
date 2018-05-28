﻿using System.Collections.Generic;

using Newtonsoft.Json;

namespace ModIO
{
    [System.Serializable]
    public class ModProfile
    {
        // ---------[ FIELDS ]---------
        /// <summary> Unique mod id. </summary>
        [JsonProperty("id")]
        public int id;

        /// <summary> Unique game id. </summary>
        [JsonProperty("game_id")]
        public int gameId;

        /// <summary> Status of the mod.
        /// <a href="https://docs.mod.io/#status-amp-visibility">Status and Visibility Documentation</a>
        /// </summary>
        [JsonProperty("status")]
        public ModStatus status;

        /// <summary> Visibility of the mod.
        /// <a href="https://docs.mod.io/#status-amp-visibility">Status and Visibility Documentation</a>
        /// </summary>
        [JsonProperty("visible")]
        public ModVisibility visibility;

        /// <summary> Contains user data. </summary>
        [JsonProperty("submitted_by")]
        public UserProfileStub submittedBy;

        /// <summary> Unix timestamp of date mod was registered. </summary>
        [JsonProperty("date_added")]
        public int dateAdded;

        /// <summary> Unix timestamp of date mod was updated. </summary>
        [JsonProperty("date_updated")]
        public int dateUpdated;

        /// <summary> Unix timestamp of date mod was set live. </summary>
        [JsonProperty("date_live")]
        public int dateLive;

        /// <summary> Contains logo data. </summary>
        [JsonProperty("logo")]
        public LogoImageLocator logoLocator;

        /// <summary> Official homepage of the mod. </summary>
        [JsonProperty("homepage_url")]
        public string homepageURL;

        /// <summary> Name of the mod. </summary>
        [JsonProperty("name")]
        public string name;

        /// <summary> Path for the mod on mod.io.
        /// For example: https://gamename.mod.io/mod-name-id-here
        /// </summary>
        [JsonProperty("name_id")]
        public string nameId;

        /// <summary> Summary of the mod. </summary>
        [JsonProperty("summary")]
        public string summary;

        /// <summary> Detailed description of the mod which allows HTML. </summary>
        [JsonProperty("description")]
        public string description;

        /// <summary> Metadata stored by the game developer.
        /// Metadata can also be stored as searchable key value pairs,
        /// and to individual mod files.
        /// </summary>
        [JsonProperty("metadata_blob")]
        public string metadataBlob;

        /// <summary> URL to the mod's mod.io profile. </summary>
        [JsonProperty("profile_url")]
        public string profileURL;

        /// <summary> Contains modfile data. </summary>
        [JsonProperty("modfile")]
        public ModfileStub activeBuild;

        /// <summary> Contains mod media data. </summary>
        [JsonProperty("media")]
        public ModMediaCollection media;

        /// <summary> Contains ratings summary. </summary>
        [JsonProperty("rating_summary")]
        public RatingSummary ratingSummary;

        /// <summary> Contains key-value metadata. </summary>
        [JsonProperty("metadata_kvp")]
        public MetadataKVP[] metadataKVPs;

        /// <summary> Contains mod tag data. </summary>
        [JsonProperty("tags")]
        public ModTag[] tags;

        // ---------[ ACCESSORS ]---------
        [JsonIgnore]
        public IEnumerable<string> tagNames
        {
            get
            {
                if(tags != null)
                {
                    foreach(ModTag tag in tags)
                    {
                        yield return tag.name;
                    }
                }
            }
        }
    }
}
