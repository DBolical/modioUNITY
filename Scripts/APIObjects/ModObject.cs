using System;

namespace ModIO.API
{
    [Serializable]
    public struct ModObject : IEquatable<ModObject>
    {
        // - Fields -
        public int id;  // Unique mod id.
        public int game_id; // Unique game id.
        public int status;  // Status of the mod (see status and visibility for details)
        public int visible; // Visibility of the mod (see status and visibility for details)
        public UserObject submitted_by; // Contains user data.
        public int date_added;  // Unix timestamp of date mod was registered.
        public int date_updated;    // Unix timestamp of date mod was updated.
        public int date_live;   // Unix timestamp of date mod was set live.
        public LogoObject logo; // Contains logo data.
        public string homepage; // Official homepage of the mod.
        public string name; // Name of the mod.
        public string name_id; // Path for the mod on mod.io. For example: https://gamename.mod.io/mod-name-id-here
        public string summary; // Summary of the mod.
        public string description; // Detailed description of the mod which allows HTML.
        public string metadata_blob; // Metadata stored by the game developer. Metadata can also be stored as searchable key value pairs, and to individual mod files.
        public string profile_url; // URL to the mod's mod.io profile.
        public ModfileObject modfile; // Contains modfile data.
        public ModMediaObject media; // Contains mod media data.
        public RatingSummaryObject rating_summary; // Contains ratings summary.
        public ModTagObject[] tags; // Contains mod tag data.

        // - Equality Operators -
        public override int GetHashCode()
        {
            return this.id;
        }

        public override bool Equals(object obj)
        {
            return (obj is ModObject
                    && this.Equals((ModObject)obj));
        }

        public bool Equals(ModObject other)
        {
            return(this.id.Equals(other.id)
                   && this.game_id.Equals(other.game_id)
                   && this.status.Equals(other.status)
                   && this.visible.Equals(other.visible)
                   && this.submitted_by.Equals(other.submitted_by)
                   && this.date_added.Equals(other.date_added)
                   && this.date_updated.Equals(other.date_updated)
                   && this.date_live.Equals(other.date_live)
                   && this.logo.Equals(other.logo)
                   && this.homepage.Equals(other.homepage)
                   && this.name.Equals(other.name)
                   && this.name_id.Equals(other.name_id)
                   && this.summary.Equals(other.summary)
                   && this.description.Equals(other.description)
                   && this.metadata_blob.Equals(other.metadata_blob)
                   && this.profile_url.Equals(other.profile_url)
                   && this.modfile.Equals(other.modfile)
                   && this.media.Equals(other.media)
                   && this.rating_summary.Equals(other.rating_summary)
                   && this.tags.GetHashCode().Equals(other.tags.GetHashCode()));
        }
    }
}