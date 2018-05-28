namespace ModIO.API
{
    public class EditModfileParameters : RequestParameters
    {
        // ---------[ FIELDS ]---------
        // Version of the file release.
        public string version
        {
            set
            {
                this.SetStringValue("version", value);
            }
        }
        // Changelog of this release.
        public string changelog
        {
            set
            {
                this.SetStringValue("changelog", value);
            }
        }
        // Default value is true. Label this upload as the current release, this will change the modfile field on the parent mod to the id of this file after upload.
        public bool isActive
        {
            set
            {
                this.SetStringValue("active", value);
            }
        }
        // Metadata stored by the game developer which may include properties such as what version of the game this file is compatible with. Metadata can also be stored as searchable key value pairs, and to the mod object.
        public string metadata_blob
        {
            set
            {
                this.SetStringValue("metadata_blob", value);
            }
        }
    }
}
