using UnityEngine;
using UnityEngine.UI;

namespace ModIO.UI
{
    [RequireComponent(typeof(UserView))]
    [RequireComponent(typeof(SlideStateViewer))]
    public class LoggedUserSlideViewer : MonoBehaviour
    {
        private UserView view { get { return this.gameObject.GetComponent<UserView>(); } }
        private SlideStateViewer slider
        { get { return this.gameObject.GetComponent<SlideStateViewer>(); } }

        // ---------[ EVENTS ]---------
        public void OnUserClicked()
        {
            if(slider.isAnimating) { return; }

            if(view.data.profile.userId > 0)
            {
                slider.isToggled = true;
            }
            else
            {
                // TODO(@jackson): Call more directly?
                view.NotifyClicked();
            }
        }

        public void OnLogoutClicked()
        {
            if(slider.isAnimating) { return; }

            // TODO(@jackson): Call more directly?
            view.NotifyClicked();
            slider.isToggled = false;
        }
    }
}