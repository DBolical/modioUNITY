using System.Collections.Generic;

namespace ModIO
{
    /// <summary>Data for an external authentication ticket.</summary>
    public struct ExternalAuthenticationData
    {
        /// <summary>Portal that the auth data is provided by.</summary>
        public UserPortal portal;

        /// <summary>Base64 encoded ticket value.</summary>
        public string ticket;

        /// <summary>Additional data.</summary>
        public Dictionary<string, string> additionalData;

        // ---------[ ADDITIONAL DATA KEYS ]---------
        /// <summary>Dictionary keys for additional Oculus Rift Data.</summary>
        public static class OculusRiftKeys
        {
            public const string NONCE = "oculusRiftNonce";
            public const string USER_ID = "oculusRiftId";
        }

        // ---------[ Obsolete ]---------
        /// <summary>[Obsolete] Provider of the ticket.</summary>
        [System.Obsolete("Use ExternalAuthenticationData.portal instead.")]
        public ExternalAuthenticationProvider provider
        {
            get { return (ExternalAuthenticationProvider)this.portal; }
            set
            {
                this.portal = ExternalAuthenticationProviderEnum.ToUserPortalEnum(value);
            }
        }
    }
}
