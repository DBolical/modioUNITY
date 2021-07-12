using UnityEngine;
using UnityEngine.UI;

namespace ModIO.UI
{
    /// <summary>Controller for the option menu popup button functionality.</summary>
    [RequireComponent(typeof(Button))]
    public class AccountReactiveButton : MonoBehaviour
    {
        // ---------[ FIELDS ]---------
        /// <summary>Actions to call if there is no logged in account.</summary>
        public Button.ButtonClickedEvent onLoggedOutClick;
        /// <summary>Actions to call if the logged account is mod.io authenticated.</summary>
        public Button.ButtonClickedEvent onModioAccountClick;
        /// <summary>Actions to call if the logged account is externally authenticated.</summary>
        public Button.ButtonClickedEvent onExternalAccountClick;

        /// <summary>The button to catch clicks for.</summary>
        private Button m_button;

        // ---------[ INITIALIZATION ]---------
        private void Start()
        {
            this.m_button = this.gameObject.GetComponent<Button>();
            this.m_button.onClick.AddListener(OnButtonClick);
        }

        // ---------[ EVENTS ]---------
        private void OnButtonClick()
        {
            if(LocalUser.AuthenticationState != AuthenticationState.ValidToken)
            {
                if(onLoggedOutClick != null)
                {
                    onLoggedOutClick.Invoke();
                }
            }
            else if(LocalUser.ExternalAuthentication.portal == UserPortal.None)
            {
                if(onModioAccountClick != null)
                {
                    onModioAccountClick.Invoke();
                }
            }
            else
            {
                if(onExternalAccountClick != null)
                {
                    onExternalAccountClick.Invoke();
                }
            }
        }
    }
}
