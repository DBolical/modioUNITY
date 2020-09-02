﻿namespace ModIO
{
    /// <summary>Editor Platform User definition</summary>
    public class PlatformUser_Editor : PlatformUser<object, object>
    {
        // ---------[ External Authentication ]---------
        /// <summary>URL for the external authentication endpoint.</summary>
        protected internal override string ExternalAuthenticationEndpoint
        {
            get { return null; }
        }

        /// <summary>Generates the headers for an external authentication request.</summary>
        protected internal override System.Collections.Generic.Dictionary<string, string> GenerateAuthenticationHeaders()
        {
            return null;
        }
    }
}