using System.Collections.Generic;
using Newtonsoft.Json;

using DateTime = System.DateTime;
using Debug = UnityEngine.Debug;
using DownloadHandlerFile = UnityEngine.Networking.DownloadHandlerFile;
using UnityWebRequest = UnityEngine.Networking.UnityWebRequest;

namespace ModIO
{
    public class WebRequestError
    {
        // ---------[ CONSTANTS ]---------
        public const int MODIOERROR_USERNOTAGREED = 11051;

        // ---------[ NESTED CLASSES ]---------
        [System.Serializable]
        private class APIWrapper
        {
            [System.Serializable]
            public class APIError
            {
                [JsonProperty("error_ref")]
                public int errorReference = -1;

                [JsonProperty("message")]
                public string message = null;

                [JsonProperty("errors")]
                public Dictionary<string, string> errors = null;
            }

            public APIError error = null;
        }

        // ---------[ FIELDS ]---------
        /// <summary>UnityWebRequest that generated the data for the error.</summary>
        public UnityWebRequest webRequest;

        /// <summary>The ServerTimeStamp at which the request was received.</summary>
        public int timeStamp;

        /// <summary>The mod.io error reference code.</summary>
        public int errorReference;

        /// <summary>The message returned by the API explaining the error.</summary>
        public string errorMessage;

        /// <summary>Errors pertaining to specific POST data fields.</summary>
        public IDictionary<string, string> fieldValidationMessages;

        // - Interpreted Values -
        /// <summary>Indicates whether the provided authentication data was rejected.</summary>
        public bool isAuthenticationInvalid;

        /// <summary>Indicates that the user attempts to authenticate has not yet accepted the mod.io terms.</summary>
        public bool isUserTermsAgreementRequired;

        /// <summary>Indicates whether the mod.io servers a unreachable (for whatever reason).</summary>
        public bool isServerUnreachable;

        /// <summary>Indicates whether this request will always fail for the provided data.</summary>
        public bool isRequestUnresolvable;

        /// <summary>Indicates whether the request triggered the Rate Limiter and when to retry.</summary>
        public int limitedUntilTimeStamp;

        /// <summary>A player/user-friendly message to display on the UI.</summary>
        public string displayMessage;

        // ---------[ INITIALIZATION ]---------
        public static WebRequestError GenerateFromWebRequest(UnityWebRequest webRequest)
        {
            UnityEngine.Debug.Assert(webRequest != null);

            if(webRequest == null)
            {
                Debug.LogWarning("[mod.io] WebRequestError.GenerateFromWebRequest(webRequest) parameter was null.");
                return WebRequestError.GenerateLocal("An unknown error occurred.");
            }
            else
            {
                WebRequestError error = new WebRequestError();

                error.webRequest = webRequest;

                error.timeStamp = ParseDateHeaderAsTimeStamp(webRequest);

                error.ApplyAPIErrorValues();
                error.ApplyInterpretedValues();

                return error;
            }
        }

        public static WebRequestError GenerateLocal(string errorMessage)
        {
            WebRequestError error = new WebRequestError()
            {
                webRequest = null,
                timeStamp = ServerTimeStamp.Now,
                errorReference = 0,
                errorMessage = errorMessage,
                displayMessage = errorMessage,

                isAuthenticationInvalid = false,
                isUserTermsAgreementRequired = false,
                isServerUnreachable = false,
                isRequestUnresolvable = false,
                limitedUntilTimeStamp = -1,
            };

            return error;
        }

        // ---------[ VALUE INTERPRETATION AND APPLICATION ]---------
        private static int ParseDateHeaderAsTimeStamp(UnityWebRequest webRequest)
        {
            var dateHeaderValue = webRequest.GetResponseHeader("Date");

            // Examples:
            //  Thu, 28 Feb 2019 07:04:38 GMT
            //  Fri, 01 Mar 2019 01:16:49 GMT
            string timeFormat = "ddd, dd MMM yyyy HH:mm:ss 'GMT'";
            DateTime time;

            if(!string.IsNullOrEmpty(dateHeaderValue)
               && DateTime.TryParseExact(dateHeaderValue,
                                         timeFormat,
                                         System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat,
                                         System.Globalization.DateTimeStyles.AssumeUniversal,
                                         out time))
            {
                // NOTE(@jackson): For some reason, System.Globalization.DateTimeStyles.AssumeUniversal
                //  is ignored(?) in TryParseExact, so it needs to be set as universal after the fact.
                time = DateTime.SpecifyKind(time, System.DateTimeKind.Utc);

                return ServerTimeStamp.FromUTCDateTime(time);
            }

            return ServerTimeStamp.Now;
        }

        private void ApplyAPIErrorValues()
        {
            this.errorMessage = null;
            this.fieldValidationMessages = null;

            // early out
            if(this.webRequest == null)
            {
                this.errorMessage = "An unknown error occurred. Please try again later.";
                return;
            }

            // null-ref and type-check
            if(this.webRequest.downloadHandler != null
               && !(this.webRequest.downloadHandler is DownloadHandlerFile))
            {
                string requestContent = null;

                try
                {
                    // get the request content
                    requestContent = this.webRequest.downloadHandler.text;
                }
                catch(System.Exception e)
                {
                    Debug.LogWarning("[mod.io] Error reading webRequest.downloadHandler text body:\n"
                                     + e.Message);
                }

                if(!string.IsNullOrEmpty(requestContent))
                {
                    WebRequestError.APIWrapper errorWrapper = null;

                    // Parse Cloudflare error
                    if(requestContent.StartsWith(@"<!DOCTYPE html>"))
                    {
                        int readIndex = requestContent.IndexOf("what-happened-section");
                        int messageEnd = -1;

                        if(readIndex > 0)
                        {
                            readIndex = requestContent.IndexOf(@"<p>", readIndex);

                            if(readIndex > 0)
                            {
                                readIndex += 3;
                                messageEnd = requestContent.IndexOf(@"</p>", readIndex);
                            }
                        }

                        if(messageEnd > 0)
                        {
                            this.errorMessage = ("A Cloudflare error has occurred: "
                                                 + requestContent.Substring(readIndex, messageEnd - readIndex));
                        }
                    }
                    else
                    {
                        try
                        {
                            // deserialize into an APIError
                            errorWrapper = JsonConvert.DeserializeObject<APIWrapper>(requestContent);
                        }
                        catch(System.Exception e)
                        {
                            Debug.LogWarning("[mod.io] Error parsing error object from response:\n"
                                             + e.Message);

                        }

                        if(errorWrapper != null
                           && errorWrapper.error != null)
                        {
                            // extract values
                            this.errorReference = errorWrapper.error.errorReference;
                            this.errorMessage = errorWrapper.error.message;
                            this.fieldValidationMessages = errorWrapper.error.errors;
                        }
                    }
                }
            }

            if(this.errorMessage == null)
            {
                this.errorMessage = this.webRequest.error;
            }
        }

        private void ApplyInterpretedValues()
        {
            this.isAuthenticationInvalid = false;
            this.isUserTermsAgreementRequired = false;
            this.isServerUnreachable = false;
            this.isRequestUnresolvable = false;
            this.limitedUntilTimeStamp = -1;
            this.displayMessage = string.Empty;

            if(this.webRequest == null) { return; }

            // Interpret code
            switch(this.webRequest.responseCode)
            {
                // - Generic coding errors -
                // Bad Request
                case 400:
                // Method Not Allowed
                case 405:
                // Not Acceptable
                case 406:
                // Unsupported Media Type
                case 415:
                {
                    if(string.IsNullOrEmpty(this.errorMessage))
                    {
                        this.displayMessage = ("Error synchronizing with the mod.io servers. [Error Code: "
                                               + this.webRequest.responseCode + "]");
                    }

                    this.isRequestUnresolvable = true;
                }
                break;

                // Bad authorization
                case 401:
                {
                    if(string.IsNullOrEmpty(this.errorMessage))
                    {
                        this.displayMessage = ("Your mod.io authentication details have changed."
                                               + "\nTry logging in again.");
                    }

                    this.isAuthenticationInvalid = true;
                    this.isRequestUnresolvable = true;
                }
                break;

                // Forbidden
                case 403:
                {
                    if(this.errorReference == WebRequestError.MODIOERROR_USERNOTAGREED)
                    {
                        this.isUserTermsAgreementRequired = true;

                        this.displayMessage = ("You have not yet agreed to the mod.io terms of service.");
                    }
                    else if(string.IsNullOrEmpty(this.errorMessage))
                    {
                        this.displayMessage = ("Your account does not have the required permissions.");
                    }

                    this.isRequestUnresolvable = true;
                }
                break;

                // Not found
                case 404:
                // Gone
                case 410:
                {
                    if(string.IsNullOrEmpty(this.errorMessage))
                    {
                        this.displayMessage = ("A networking error occurred.");
                    }

                    this.isRequestUnresolvable = true;
                }
                break;

                // case 405: Handled Above
                // case 406: Handled Above

                // Timeout
                case 408:
                {
                    if(string.IsNullOrEmpty(this.errorMessage))
                    {
                        this.displayMessage = ("The mod.io servers could not be reached."
                                           + "\nPlease check your internet connection.");
                    }

                    this.isServerUnreachable = true;
                }
                break;

                // case 410: Handled Above

                // Unprocessable Entity
                case 422:
                {
                    var displayString = new System.Text.StringBuilder();
                    displayString.AppendLine("The submitted data contained error(s).");

                    if(this.fieldValidationMessages != null
                       && this.fieldValidationMessages.Count > 0)
                    {
                        foreach(var kvp in fieldValidationMessages)
                        {
                            displayString.AppendLine("- [" + kvp.Key + "] " + kvp.Value);
                        }
                    }

                    if(displayString.Length > 0
                       && displayString[displayString.Length - 1] == '\n')
                    {
                        --displayString.Length;
                    }

                    this.displayMessage = displayString.ToString();

                    this.isRequestUnresolvable = true;
                }
                break;

                // Too Many Requests
                case 429:
                {
                    string retryAfterString;
                    int retryAfterSeconds;

                    var responseHeaders = this.webRequest.GetResponseHeaders();
                    if(!(responseHeaders.TryGetValue("X-Ratelimit-RetryAfter", out retryAfterString)
                         && int.TryParse(retryAfterString, out retryAfterSeconds)))
                    {
                        retryAfterSeconds = 60;

                        Debug.LogWarning("[mod.io] Too many APIRequests have been made, however"
                                         + " no valid X-Ratelimit-RetryAfter header was detected."
                                         + "\nPlease report this to jackson@mod.io with the following information:"
                                         + "\n[" + this.webRequest.url
                                         + ":" + this.webRequest.method
                                         + "-" + this.errorMessage + "]");
                    }

                    if(string.IsNullOrEmpty(this.errorMessage))
                    {
                        this.displayMessage = ("Too many requests have been made to the mod.io servers."
                                             + "\nReconnecting in " + retryAfterSeconds.ToString() + " seconds.");
                    }

                    this.limitedUntilTimeStamp = this.timeStamp + retryAfterSeconds;
                }
                break;

                // Internal server error
                case 500:
                {
                    if(string.IsNullOrEmpty(this.errorMessage))
                    {
                        this.displayMessage = ("There was an error with the mod.io servers. Staff have been"
                                             + " notified, and will attempt to fix the issue as soon as possible.");
                    }

                    this.isRequestUnresolvable = true;
                }
                break;

                // Service Unavailable
                case 503:
                {
                    if(string.IsNullOrEmpty(this.errorMessage))
                    {
                        this.displayMessage = "The mod.io servers are currently offline.";
                    }

                    this.isServerUnreachable = true;
                }
                break;

                default:
                {
                    // Cannot connect resolve destination host, used by Unity
                    if(this.webRequest.responseCode <= 0)
                    {
                        this.displayMessage = ("The mod.io servers cannot be reached."
                                               + "\nPlease check your internet connection.");

                        this.isServerUnreachable = true;
                    }
                    else
                    {
                        this.displayMessage = ("Error synchronizing with the mod.io servers. [Error Code: "
                                               + this.webRequest.responseCode + "]");

                        this.isRequestUnresolvable = true;
                    }
                }
                break;
            }

            if(string.IsNullOrEmpty(this.displayMessage))
            {
                this.displayMessage = this.errorMessage;
            }
        }

        // ---------[ Obsolete ]---------
        [System.Obsolete("Use webRequest.responseCode instead")]
        public int responseCode
        {
            get { return (webRequest != null ? (int)webRequest.responseCode : -1); }
        }
        [System.Obsolete("Use webRequest.method instead")]
        public string method
        {
            get { return (webRequest != null ? webRequest.method : "LOCAL"); }
        }
        [System.Obsolete("Use webRequest.url instead")]
        public string url
        {
            get { return (webRequest != null ? webRequest.url : string.Empty); }
        }
        [System.Obsolete("Use webRequest.GetResponseHeaders() instead")]
        public Dictionary<string, string> responseHeaders
        {
            get { return (webRequest != null ? webRequest.GetResponseHeaders() : null); }
        }

        [System.Obsolete("Use webRequest.downloadHandler.text instead")]
        public string responseBody
        {
            get
            {
                if(webRequest != null
                   && webRequest.downloadHandler != null
                   && !(webRequest.downloadHandler is DownloadHandlerFile))
                {
                    return webRequest.downloadHandler.text;
                }
                return string.Empty;
            }
        }

        /// <summary>[Obsolete] The message returned by the API explaining the error.</summary>
        [System.Obsolete("Use WebRequestError.errorMessage instead")]
        public string message
        {
            get { return this.errorMessage; }
            set { this.errorMessage = value; }
        }

        [System.Obsolete("Set PluginSettings.requestLogging.errorsAsWarnings instead.")]
        public static void LogAsWarning(WebRequestError error)
        {
            Debug.LogWarning("[mod.io] Web Request Failed\n"
                             + error.ToUnityDebugString());
        }

        [System.Obsolete("Use DebugUtilities.GetResponseInfo() instead.")]
        public string ToUnityDebugString()
        {
            if(this.webRequest == null)
            {
                return ("Request failed prior to being sent.\n"
                        + this.errorMessage);
            }
            else
            {
                return DebugUtilities.GetResponseInfo(this.webRequest);
            }
        }
    }
}
