using System;
using System.Collections.Generic;

using Debug = UnityEngine.Debug;

namespace ModIO
{
    /// <summary>Main functional wrapper for the LocalUser structure.</summary>
    public static class UserAccountManagement
    {
        // ---------[ MOD COLLECTION MANAGEMENT ]---------
        /// <summary>Add a mod to the subscribed list and modifies the queued actions accordingly.</summary>
        public static void SubscribeToMod(int modId)
        {
            // add sub to list
            if(!LocalUser.SubscribedModIds.Contains(modId))
            {
                LocalUser.SubscribedModIds.Add(modId);
            }

            // check queues
            bool unsubQueued =  LocalUser.QueuedUnsubscribes.Contains(modId);
            bool subQueued =    LocalUser.QueuedSubscribes.Contains(modId);

            // add to/remove from queues
            if(unsubQueued)
            {
                LocalUser.QueuedUnsubscribes.Remove(modId);
            }
            else if(!subQueued)
            {
                LocalUser.QueuedSubscribes.Add(modId);
            }

            // save
            LocalUser.Save();
        }

        /// <summary>Removes a mod from the subscribed list and modifies the queued actions accordingly.</summary>
        public static void UnsubscribeFromMod(int modId)
        {
            // remove sub from list
            LocalUser.SubscribedModIds.Remove(modId);

            // check queues
            bool unsubQueued =  LocalUser.QueuedUnsubscribes.Contains(modId);
            bool subQueued =    LocalUser.QueuedSubscribes.Contains(modId);

            // add to/remove from queues
            if(subQueued)
            {
                LocalUser.QueuedSubscribes.Remove(modId);
            }
            else if(!unsubQueued)
            {
                LocalUser.QueuedUnsubscribes.Add(modId);
            }

            // save
            LocalUser.Save();
        }

        /// <summary>Pushes queued subscribe actions to the server.</summary>
        public static void PushSubscriptionChanges(Action onCompletedNoErrors,
                                                   Action<List<WebRequestError>> onCompletedWithErrors)
        {
            int responsesPending = (LocalUser.QueuedSubscribes.Count
                                    + LocalUser.QueuedUnsubscribes.Count);

            // early outs
            if(LocalUser.AuthenticationState == AuthenticationState.NoToken
               || responsesPending == 0)
            {
                if(onCompletedNoErrors != null)
                {
                    onCompletedNoErrors();
                }

                return;
            }

            // set up vars
            string userToken = LocalUser.OAuthToken;
            List<WebRequestError> errors = new List<WebRequestError>();

            List<int> subscribesPushed
                = new List<int>(LocalUser.QueuedSubscribes.Count);
            List<int> unsubscribesPushed
                = new List<int>(LocalUser.QueuedUnsubscribes.Count);

            // callback
            Action onRequestCompleted = () =>
            {
                if(responsesPending <= 0)
                {
                    if(userToken == LocalUser.OAuthToken)
                    {
                        foreach(int modId in subscribesPushed)
                        {
                            LocalUser.QueuedSubscribes.Remove(modId);
                        }
                        foreach(int modId in unsubscribesPushed)
                        {
                            LocalUser.QueuedUnsubscribes.Remove(modId);
                        }

                        LocalUser.Save();
                    }

                    if(errors.Count == 0
                       && onCompletedNoErrors != null)
                    {
                        onCompletedNoErrors();
                    }
                    else if(errors.Count > 0
                            && onCompletedWithErrors != null)
                    {
                        onCompletedWithErrors(errors);
                    }
                }
            };

            // - push -
            foreach(int modId in LocalUser.QueuedSubscribes)
            {
                APIClient.SubscribeToMod(modId,
                (p) =>
                {
                    subscribesPushed.Add(modId);

                    --responsesPending;
                    onRequestCompleted();
                },
                (e) =>
                {
                    // Error for "Mod is already subscribed"
                    if(e.webRequest.responseCode == 400)
                    {
                        subscribesPushed.Add(modId);
                    }
                    // Error for "Mod is unavailable"
                    else if(e.webRequest.responseCode == 404)
                    {
                        subscribesPushed.Add(modId);
                    }
                    // Error for real
                    else
                    {
                        errors.Add(e);
                    }

                    --responsesPending;
                    onRequestCompleted();
                });
            }
            foreach(int modId in LocalUser.QueuedUnsubscribes)
            {
                APIClient.UnsubscribeFromMod(modId,
                () =>
                {
                    --responsesPending;
                    unsubscribesPushed.Remove(modId);

                    onRequestCompleted();
                },
                (e) =>
                {

                    // Error for "Mod is already subscribed"
                    if(e.webRequest.responseCode == 400)
                    {
                        unsubscribesPushed.Remove(modId);
                    }
                    // Error for "Mod is unavailable"
                    else if(e.webRequest.responseCode == 404)
                    {
                        unsubscribesPushed.Remove(modId);
                    }
                    // Error for real
                    else
                    {
                        errors.Add(e);
                    }

                    --responsesPending;
                    onRequestCompleted();
                });
            }
        }

        /// <summary>Pulls the subscriptions from the server and stores the changes.</summary>
        public static void PullSubscriptionChanges(Action<List<ModProfile>> onSuccess,
                                                   Action<WebRequestError> onError)
        {
            // early out
            if(LocalUser.AuthenticationState == AuthenticationState.NoToken)
            {
                if(onSuccess != null)
                {
                    onSuccess(new List<ModProfile>(0));
                }
                return;
            }

            // holding vars
            string userToken = LocalUser.OAuthToken;
            List<ModProfile> remoteOnlySubscriptions = new List<ModProfile>();

            // set filter and initial pagination
            RequestFilter subscriptionFilter = new RequestFilter();
            subscriptionFilter.AddFieldFilter(ModIO.API.GetUserSubscriptionsFilterFields.gameId,
                                              new EqualToFilter<int>(PluginSettings.GAME_ID));

            APIPaginationParameters pagination = new APIPaginationParameters()
            {
                limit = APIPaginationParameters.LIMIT_MAX,
                offset = 0,
            };

            // define actions
            Action getNextPage = null;
            Action<RequestPage<ModProfile>> onPageReceived = null;
            Action onAllPagesReceived = null;

            getNextPage = () =>
            {
                APIClient.GetUserSubscriptions(subscriptionFilter, pagination,
                (response) =>
                {
                    onPageReceived(response);

                    // check if all pages received
                    if(response != null
                       && response.items != null
                       && response.items.Length > 0
                       && response.resultTotal > response.size + response.resultOffset)
                    {
                        pagination.offset = response.resultOffset + response.size;

                        getNextPage();
                    }
                    else
                    {
                        onAllPagesReceived();

                        if(onSuccess != null)
                        {
                            onSuccess(remoteOnlySubscriptions);
                        }
                    }
                },
                (e) =>
                {
                    if(onError != null)
                    {
                        onError(e);
                    }
                });
            };


            onPageReceived = (r) =>
            {
                foreach(ModProfile profile in r.items)
                {
                    if(profile != null)
                    {
                        remoteOnlySubscriptions.Add(profile);
                    }
                }
            };

            onAllPagesReceived = () =>
            {
                if(userToken != LocalUser.OAuthToken)
                {
                    return;
                }

                List<int> localOnlySubs
                = new List<int>(LocalUser.SubscribedModIds);

                // NOTE(@jackson): Unsub actions *should not* be found in activeUser.subscribedModIds
                foreach(int modId in LocalUser.QueuedUnsubscribes)
                {
                    #if DEBUG
                    if(localOnlySubs.Contains(modId))
                    {
                        Debug.LogWarning("[mod.io] A locally subscribed mod was found in the"
                                         + " queuedUnsubscribes. This should not occur - please"
                                         + " ensure that any mod ids added to"
                                         + " activeUser.queuedUnsubscribes are removed from"
                                         + " activeUser.subscribedModIds or use"
                                         + " UserAccountManagement.UnsubscribeFromMod() to handle"
                                         + " this automatically.");
                    }
                    #endif

                    localOnlySubs.Remove(modId);
                }

                List<int> newSubs = new List<int>();

                // build new subs list
                for(int i = 0; i < remoteOnlySubscriptions.Count; ++i)
                {
                    ModProfile profile = remoteOnlySubscriptions[i];

                    // remove if in queued subs
                    LocalUser.QueuedSubscribes.Remove(profile.id);

                    // if in unsub queue
                    if(LocalUser.QueuedUnsubscribes.Contains(profile.id))
                    {
                        remoteOnlySubscriptions.RemoveAt(i);
                        --i;
                    }
                    // if locally subbed
                    else if(localOnlySubs.Remove(profile.id))
                    {
                        remoteOnlySubscriptions.RemoveAt(i);
                        --i;
                    }
                    // if not locally subbed && if not in unsub queue
                    else
                    {
                        newSubs.Add(profile.id);
                    }
                }

                // -- update locally --
                // remove new unsubs
                foreach(int modId in localOnlySubs)
                {
                    // if not in sub queue
                    if(!LocalUser.QueuedSubscribes.Contains(modId))
                    {
                        LocalUser.SubscribedModIds.Remove(modId);
                    }
                }

                LocalUser.SubscribedModIds.AddRange(newSubs);

                // save
                LocalUser.Save();
            };

            // get pages
            getNextPage();
        }

        // ---------[ AUTHENTICATION ]---------
        /// <summary>Pulls any changes to the User Profile from the mod.io servers.</summary>
        public static void UpdateUserProfile(Action<UserProfile> onSuccess,
                                             Action<WebRequestError> onError)
        {
            if(LocalUser.AuthenticationState != AuthenticationState.NoToken)
            {
                APIClient.GetAuthenticatedUser((p) =>
                {
                    LocalUser.Profile = p;
                    LocalUser.Save();

                    if(onSuccess != null)
                    {
                        onSuccess(p);
                    }
                },
                onError);
            }
            else if(onSuccess != null)
            {
                onSuccess(null);
            }
        }

        /// <summary>Begins the authentication process using a mod.io Security Code.</summary>
        public static void AuthenticateWithSecurityCode(string securityCode,
                                                        Action<UserProfile> onSuccess,
                                                        Action<WebRequestError> onError)
        {
            APIClient.GetOAuthToken(securityCode, (t) =>
            {
                LocalUser.OAuthToken = t;
                LocalUser.WasTokenRejected = false;
                LocalUser.Save();

                UserAccountManagement.UpdateUserProfile(onSuccess, onError);
            },
            onError);
        }

        /// <summary>Attempts to authenticate a user using a Steam Encrypted App Ticket.</summary>
        /// <remarks>This version is designed to match the Steamworks.NET implementation by
        /// @rlabrecque at https://github.com/rlabrecque/Steamworks.NET</remarks>
        public static void AuthenticateWithSteamEncryptedAppTicket(byte[] pTicket, uint pcbTicket,
                                                                   bool hasUserAcceptedTerms,
                                                                   Action<UserProfile> onSuccess,
                                                                   Action<WebRequestError> onError)
        {
            string encodedTicket = Utility.EncodeEncryptedAppTicket(pTicket, pcbTicket);
            UserAccountManagement.AuthenticateWithSteamEncryptedAppTicket(encodedTicket,
                                                                          hasUserAcceptedTerms,
                                                                          onSuccess, onError);
        }

        /// <summary>Attempts to authenticate a user using a Steam Encrypted App Ticket.</summary>
        /// <remarks>This version is designed to match the FacePunch.SteamWorks implementation by
        /// @garrynewman at https://github.com/Facepunch/Facepunch.Steamworks</remarks>
        public static void AuthenticateWithSteamEncryptedAppTicket(byte[] authTicketData,
                                                                   bool hasUserAcceptedTerms,
                                                                   Action<UserProfile> onSuccess,
                                                                   Action<WebRequestError> onError)
        {
            string encodedTicket = Utility.EncodeEncryptedAppTicket(authTicketData, (uint)authTicketData.Length);
            UserAccountManagement.AuthenticateWithSteamEncryptedAppTicket(encodedTicket,
                                                                          hasUserAcceptedTerms,
                                                                          onSuccess, onError);
        }


        /// <summary>Attempts to authenticate a user using a Steam Encrypted App Ticket.</summary>
        public static void AuthenticateWithSteamEncryptedAppTicket(string encodedTicket,
                                                                   bool hasUserAcceptedTerms,
                                                                   Action<UserProfile> onSuccess,
                                                                   Action<WebRequestError> onError)
        {
            LocalUser.ExternalAuthentication = new ExternalAuthenticationData()
            {
                ticket = encodedTicket,
                portal = UserPortal.Steam,
            };

            APIClient.RequestSteamAuthentication(encodedTicket, hasUserAcceptedTerms, (t) =>
            {
                LocalUser.OAuthToken = t;
                LocalUser.WasTokenRejected = false;
                LocalUser.Save();

                UserAccountManagement.UpdateUserProfile(onSuccess, onError);
            },
            onError);
        }

        /// <summary>Attempts to authenticate a user using a GOG Encrypted App Ticket.</summary>
        public static void AuthenticateWithGOGEncryptedAppTicket(byte[] data, uint dataSize,
                                                                 bool hasUserAcceptedTerms,
                                                                 Action<UserProfile> onSuccess,
                                                                 Action<WebRequestError> onError)
        {
            string encodedTicket = Utility.EncodeEncryptedAppTicket(data, dataSize);
            UserAccountManagement.AuthenticateWithGOGEncryptedAppTicket(encodedTicket,
                                                                        hasUserAcceptedTerms,
                                                                        onSuccess, onError);
        }

        /// <summary>Attempts to authenticate a user using a GOG Encrypted App Ticket.</summary>
        public static void AuthenticateWithGOGEncryptedAppTicket(string encodedTicket,
                                                                 bool hasUserAcceptedTerms,
                                                                 Action<UserProfile> onSuccess,
                                                                 Action<WebRequestError> onError)
        {
            LocalUser.ExternalAuthentication = new ExternalAuthenticationData()
            {
                ticket = encodedTicket,
                portal = UserPortal.Steam,
            };

            APIClient.RequestGOGAuthentication(encodedTicket, hasUserAcceptedTerms,
            (t) =>
            {
                LocalUser.OAuthToken = t;
                LocalUser.WasTokenRejected = false;
                LocalUser.Save();

                UserAccountManagement.UpdateUserProfile(onSuccess, onError);
            },
            onError);
        }

        /// <summary>Attempts to authenticate a user using an itch.io JWT Token.</summary>
        public static void AuthenticateWithItchIOToken(string jwtToken,
                                                       bool hasUserAcceptedTerms,
                                                       Action<UserProfile> onSuccess,
                                                       Action<WebRequestError> onError)
        {
            LocalUser.ExternalAuthentication = new ExternalAuthenticationData()
            {
                ticket = jwtToken,
                portal = UserPortal.itchio,
            };

            APIClient.RequestItchIOAuthentication(jwtToken, hasUserAcceptedTerms,
            (t) =>
            {
                LocalUser.OAuthToken = t;
                LocalUser.WasTokenRejected = false;
                LocalUser.Save();

                UserAccountManagement.UpdateUserProfile(onSuccess, onError);
            },
            onError);
        }

        /// <summary>Attempts to authenticate a user using Oculus Rift user data.</summary>
        public static void AuthenticateWithOculusRiftUserData(string oculusUserNonce,
                                                              int oculusUserId,
                                                              string oculusUserAccessToken,
                                                              bool hasUserAcceptedTerms,
                                                              Action<UserProfile> onSuccess,
                                                              Action<WebRequestError> onError)
        {
            LocalUser.ExternalAuthentication = new ExternalAuthenticationData()
            {
                portal = UserPortal.Oculus,
                ticket = oculusUserAccessToken,
                additionalData = new Dictionary<string, string>()
                {
                    { ExternalAuthenticationData.OculusRiftKeys.NONCE, oculusUserNonce },
                    { ExternalAuthenticationData.OculusRiftKeys.USER_ID, oculusUserId.ToString() },
                },
            };

            APIClient.RequestOculusRiftAuthentication(oculusUserNonce, oculusUserId, oculusUserAccessToken, hasUserAcceptedTerms,
            (t) =>
            {
                LocalUser.OAuthToken = t;
                LocalUser.WasTokenRejected = false;
                LocalUser.Save();

                UserAccountManagement.UpdateUserProfile(onSuccess, onError);
            },
            onError);
        }

        /// <summary>Attempts to authenticate a user using Xbox Live credentials.</summary>
        public static void AuthenticateWithXboxLiveToken(string xboxLiveUserToken,
                                                         bool hasUserAcceptedTerms,
                                                         Action<UserProfile> onSuccess,
                                                         Action<WebRequestError> onError)
        {
            LocalUser.ExternalAuthentication = new ExternalAuthenticationData()
            {
                ticket = xboxLiveUserToken,
                portal = UserPortal.XboxLive,
            };

            APIClient.RequestXboxLiveAuthentication(xboxLiveUserToken, hasUserAcceptedTerms,
            (t) =>
            {
                LocalUser.OAuthToken = t;
                LocalUser.WasTokenRejected = false;
                LocalUser.Save();

                UserAccountManagement.UpdateUserProfile(onSuccess, onError);
            },
            onError);
        }

        /// <summary>Attempts to reauthenticate using the stored external auth ticket.</summary>
        public static void ReauthenticateWithStoredExternalAuthData(bool hasUserAcceptedTerms,
                                                                    Action<UserProfile> onSuccess,
                                                                    Action<WebRequestError> onError)
        {
            ExternalAuthenticationData authData = LocalUser.ExternalAuthentication;

            Debug.Assert(!string.IsNullOrEmpty(authData.ticket));
            Debug.Assert(authData.portal != UserPortal.None);

            Action<string> onSuccessWrapper = (t) =>
            {
                LocalUser.OAuthToken = t;
                LocalUser.WasTokenRejected = false;
                LocalUser.Save();

                if(onSuccess != null)
                {
                    UserAccountManagement.UpdateUserProfile(onSuccess, onError);
                }
            };

            switch(LocalUser.ExternalAuthentication.portal)
            {
                case UserPortal.Steam:
                {
                    APIClient.RequestSteamAuthentication(authData.ticket,
                                                         hasUserAcceptedTerms,
                                                         onSuccessWrapper,
                                                         onError);
                }
                break;

                case UserPortal.GOG:
                {
                    APIClient.RequestGOGAuthentication(authData.ticket,
                                                       hasUserAcceptedTerms,
                                                       onSuccessWrapper,
                                                       onError);
                }
                break;

                case UserPortal.itchio:
                {
                    APIClient.RequestItchIOAuthentication(authData.ticket,
                                                          hasUserAcceptedTerms,
                                                          onSuccessWrapper,
                                                          onError);
                }
                break;

                case UserPortal.Oculus:
                {
                    string token = authData.ticket;
                    string nonce = null;
                    string userIdString = null;
                    int userId = -1;
                    string errorMessage = null;

                    if(authData.additionalData == null)
                    {
                        errorMessage = "The user id and nonce are missing.";
                    }
                    else if(!authData.additionalData.TryGetValue(ExternalAuthenticationData.OculusRiftKeys.NONCE, out nonce)
                            || string.IsNullOrEmpty(nonce))
                    {
                        errorMessage = "The nonce is missing.";
                    }
                    else if(!authData.additionalData.TryGetValue(ExternalAuthenticationData.OculusRiftKeys.USER_ID, out userIdString)
                            || string.IsNullOrEmpty(userIdString))
                    {
                        errorMessage = "The user id is missing.";
                    }
                    else if(!int.TryParse(userIdString, out userId))
                    {
                        errorMessage = "The user id is not parseable as an integer.";
                    }

                    if(errorMessage != null)
                    {
                        Debug.LogWarning("[mod.io] Unable to authenticate using stored Oculus Rift user data.\n"
                                         + errorMessage);

                        if(onError != null)
                        {
                            var error = WebRequestError.GenerateLocal(errorMessage);
                            onError(error);
                        }

                        return;
                    }
                    else
                    {
                        APIClient.RequestOculusRiftAuthentication(nonce, userId, token,
                                                                  hasUserAcceptedTerms,
                                                                  onSuccessWrapper,
                                                                  onError);
                    }
                }
                break;

                case UserPortal.XboxLive:
                {
                    APIClient.RequestXboxLiveAuthentication(authData.ticket,
                                                            hasUserAcceptedTerms,
                                                            onSuccessWrapper,
                                                            onError);
                }
                break;

                default:
                {
                    throw new System.NotImplementedException();
                }
            }
        }

        // ---------[ Obsolete ]---------
        /// <summary>[Obsolete] Attempts to authenticate a user using a Steam Encrypted App Ticket.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void AuthenticateWithSteamEncryptedAppTicket(byte[] pTicket, uint pcbTicket,
                                                                   Action<UserProfile> onSuccess,
                                                                   Action<WebRequestError> onError)
        {
            UserAccountManagement.AuthenticateWithSteamEncryptedAppTicket(pTicket, pcbTicket,
                                                                          false,
                                                                          onSuccess, onError);
        }
        /// <summary>[Obsolete] Attempts to authenticate a user using a Steam Encrypted App Ticket.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void AuthenticateWithSteamEncryptedAppTicket(byte[] authTicketData,
                                                                   Action<UserProfile> onSuccess,
                                                                   Action<WebRequestError> onError)
        {
            UserAccountManagement.AuthenticateWithSteamEncryptedAppTicket(authTicketData,
                                                                          false,
                                                                          onSuccess, onError);
        }
        /// <summary>[Obsolete] Attempts to authenticate a user using a Steam Encrypted App Ticket.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void AuthenticateWithSteamEncryptedAppTicket(string encodedTicket,
                                                                   Action<UserProfile> onSuccess,
                                                                   Action<WebRequestError> onError)
        {
            UserAccountManagement.AuthenticateWithSteamEncryptedAppTicket(encodedTicket,
                                                                          false,
                                                                          onSuccess, onError);
        }

        /// <summary>[Obsolete] Attempts to authenticate a user using a GOG Encrypted App Ticket.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void AuthenticateWithGOGEncryptedAppTicket(byte[] data, uint dataSize,
                                                                 Action<UserProfile> onSuccess,
                                                                 Action<WebRequestError> onError)
        {
            UserAccountManagement.AuthenticateWithGOGEncryptedAppTicket(data, dataSize, false,
                                                                        onSuccess, onError);
        }

        /// <summary>[Obsolete] Attempts to authenticate a user using a GOG Encrypted App Ticket.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void AuthenticateWithGOGEncryptedAppTicket(string encodedTicket,
                                                                 Action<UserProfile> onSuccess,
                                                                 Action<WebRequestError> onError)
        {
            UserAccountManagement.AuthenticateWithGOGEncryptedAppTicket(encodedTicket, false,
                                                                        onSuccess, onError);
        }

        /// <summary>[Obsolete] Attempts to authenticate a user using an itch.io JWT Token.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void AuthenticateWithItchIOToken(string jwtToken,
                                                       Action<UserProfile> onSuccess,
                                                       Action<WebRequestError> onError)
        {
            UserAccountManagement.AuthenticateWithItchIOToken(jwtToken, false,
                                                              onSuccess, onError);
        }

        /// <summary>[Obsolete] Attempts to authenticate a user using Oculus Rift user data.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void AuthenticateWithOculusRiftUserData(string oculusUserNonce,
                                                              int oculusUserId,
                                                              string oculusUserAccessToken,
                                                              Action<UserProfile> onSuccess,
                                                              Action<WebRequestError> onError)
        {
            UserAccountManagement.AuthenticateWithOculusRiftUserData(oculusUserNonce, oculusUserId,
                                                                     oculusUserAccessToken,
                                                                     false,
                                                                     onSuccess, onError);
        }

        /// <summary>[Obsolete] Attempts to authenticate a user using Xbox Live credentials.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void AuthenticateWithXboxLiveToken(string xboxLiveUserToken,
                                                         Action<UserProfile> onSuccess,
                                                         Action<WebRequestError> onError)
        {
            UserAccountManagement.AuthenticateWithXboxLiveToken(xboxLiveUserToken, false,
                                                                onSuccess, onError);
        }

        /// <summary>[Obsolete] Attempts to reauthenticate using the stored external auth ticket.</summary>
        [Obsolete("Now requires the hasUserAcceptedTerms flag to be provided.")]
        public static void ReauthenticateWithStoredExternalAuthData(Action<UserProfile> onSuccess,
                                                                    Action<WebRequestError> onError)
        {
            UserAccountManagement.ReauthenticateWithStoredExternalAuthData(false,
                                                                           onSuccess, onError);
        }
    }
}
