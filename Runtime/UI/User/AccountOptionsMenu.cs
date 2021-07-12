using UnityEngine;
using UnityEngine.UI;

namespace ModIO.UI
{
    /// <summary>The component to control the options menu UI for the mod.io plugin.</summary>
    public class AccountOptionsMenu : MonoBehaviour
    {
        // ---------[ FIELDS ]---------
        [Header("UI Elements")]
        /// <summary>The UI Element containing the menu elements.</summary>
        public RectTransform dropdown = null;
        /// <summary>Display for the logged in user.</summary>
        public UserView loggedUser = null;
        /// <summary>The menu option for viewing the player's profile on the mod.io website.</summary>
        public Button viewProfileButton = null;
        /// <summary>The menu option that lets the player log out.</summary>
        public Button logoutButton = null;
        /// <summary>The menu option that lets the player log in.</summary>
        public Button loginButton = null;

        // ---------[ INITIALIZATION ]---------
        private void Start()
        {
            this.logoutButton.onClick.AddListener(HideMenu);
            this.loginButton.onClick.AddListener(HideMenu);
        }

        private void OnEnable()
        {
            this.dropdown.gameObject.SetActive(false);
        }

        // ---------[ EVENTS ]---------
        /// <summary>Shows the menu.</summary>
        public void ShowMenu()
        {
            bool loggedIn = (LocalUser.AuthenticationState == AuthenticationState.ValidToken);

            this.loggedUser.gameObject.SetActive(loggedIn);
            this.logoutButton.gameObject.SetActive(loggedIn);
            this.loginButton.gameObject.SetActive(!loggedIn);
            this.viewProfileButton.gameObject.SetActive(loggedIn);

            this.dropdown.gameObject.SetActive(true);
        }

        /// <summary>Hides the menu.</summary>
        public void HideMenu()
        {
            this.dropdown.gameObject.SetActive(false);
        }

        /// <summary>Toggles the menu between show/hide.</summary>
        public void ToggleMenu()
        {
            bool isActive = this.dropdown.gameObject.activeSelf;
            if(!isActive)
            {
                ShowMenu();
            }
            else
            {
                HideMenu();
            }
        }

        // ---------[ MENU OPTIONS ]---------
        /// <summary>Opens the user's menu profile in a web browser.</summary>
        public void OpenProfileInBrowser()
        {
            UserProfile profile = LocalUser.Profile;
            if(profile != null)
            {
                this.viewProfileButton.interactable = false;

                string urlLoginPostfix = string.Empty;

                switch(LocalUser.ExternalAuthentication.portal)
                {
                    case UserPortal.Steam:
                    {
                        urlLoginPostfix = "?ref=steam";
                    }
                    break;

                    case UserPortal.GOG:
                    {
                        urlLoginPostfix = "?ref=gog";
                    }
                    break;
                }

                string profileURL = profile.profileURL + @"/edit" + urlLoginPostfix;
                Application.OpenURL(profileURL);

                this.viewProfileButton.interactable = true;
            }
        }
    }
}
