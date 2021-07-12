using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace ModIO.UI
{
    /// <summary>A view for allowing a player to report a mod.</summary>
    public class ReportDialog : MonoBehaviour, IBrowserView
    {
        // ---------[ Constants ]---------
        /// <summary>This component is designed for the reporting of mods and no other resource type.</summary>
        public const ReportedResourceType RESOURCE_TYPE = ReportedResourceType.Mod;

        // ---------[ Fields ]---------
        /// <summary>Dropdown for providing the report type.</summary>
        public ReportTypeDropdown dropdown = null;

        /// <summary>Input field for providing the report details.</summary>
        public InputField detailsField = null;

        /// <summary>Input field for providing a contact name.</summary>
        public InputField contactNameField = null;

        /// <summary>Input field for providing a contact email.</summary>
        public InputField contactEmailField = null;

        /// <summary>Mod id of the mod being reported.</summary>
        private int m_modId = ModProfile.NULL_ID;

        // --- Accessors ---
        /// <summary>Gets the canvas group attached to this gameObject.</summary>
        CanvasGroup IBrowserView.canvasGroup { get { return this.gameObject.GetComponent<CanvasGroup>(); } }

        /// <summary>Reset selection on hide.</summary>
        bool IBrowserView.resetSelectionOnHide { get { return true; } }

        /// <summary>Is the view a root view or window view?</summary>
        bool IBrowserView.isRootView { get { return false; } }

        /// <summary>The priority to focus the selectables.</summary>
        private List<Selectable> m_onFocusPriority = null;

        /// <summary>The priority to focus the selectables.</summary>
        List<Selectable> IBrowserView.onFocusPriority { get { return this.m_onFocusPriority; } }

        // ---------[ Initialization ]---------
        /// <summary>Build the prioritization list.</summary>
        private void Awake()
        {
            this.m_onFocusPriority = new List<Selectable>()
            {
                this.dropdown.GetComponent<Dropdown>(),
                this.detailsField,
            };
        }

        // ---------[ UI Control ]---------
        /// <summary>Sets the mod id for the mod being reported.</summary>
        public void SetModId(int modId)
        {
            if(this.m_modId != modId)
            {
                this.m_modId = modId;

                if(modId == ModProfile.NULL_ID)
                {
                    this.SetModProfile(null);
                }
                else
                {
                    ModManager.GetModProfile(modId, this.SetModProfile, (e) => this.m_modId = ModProfile.NULL_ID);
                }
            }
        }

        /// <summary>Sets the mod profile for the mod being reported.</summary>
        public void SetModProfile(ModProfile profile)
        {
            if(profile == null)
            {
                this.m_modId = ModProfile.NULL_ID;
            }
            else
            {
                this.m_modId = profile.id;
            }

            ModView view = this.GetComponent<ModView>();
            if(view != null)
            {
                view.profile = profile;
            }
        }

        /// <summary>Closes the dialog window.</summary>
        public void Close()
        {
            ViewManager.instance.CloseWindowedView(this);
        }

        /// <summary>Creates a report using information in the input fields and submits.</summary>
        public void SubmitReport()
        {
            var reportParams = new ModIO.API.SubmitReportParameters();
            var messageData = new MessageDialog.Data();

            reportParams.resource = EditableReport.ResourceTypeToAPIString(ReportDialog.RESOURCE_TYPE);

            // check mod id
            if(this.m_modId <= 0)
            {
                messageData.header = "Error Submitting Report";
                messageData.message = "The submission process encountered an error.\n[Error: Invalid mod id]";
                messageData.standardButtonCallback = () =>
                {
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.reportDialog);
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
                };
                messageData.standardButtonText = "Back";
                messageData.onClose = () =>
                {
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.reportDialog);
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
                };

                return;
            }
            reportParams.id = this.m_modId;

            // check report type
            ReportType type;
            if(!this.dropdown.TryGetSelectedValue(out type))
            {
                messageData.header = "Error Submitting Report";
                messageData.message = "A report type needs to be selected from the dropdown options.";
                messageData.standardButtonCallback = () => ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
                messageData.standardButtonText = "Back";

                ViewManager.instance.ShowMessageDialog(messageData);

                return;
            }
            reportParams.type = type;

            // check email
            string email = this.contactEmailField.text;
            if(!string.IsNullOrEmpty(email)
               && !Utility.IsEmail(email))
            {
                messageData.header = "Error Submitting Report";
                messageData.message = "Please enter a valid email address or leave the field empty.";
                messageData.standardButtonCallback = () => ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
                messageData.standardButtonText = "Back";

                ViewManager.instance.ShowMessageDialog(messageData);

                return;
            }
            reportParams.contact = email;

            // get simple inputs
            reportParams.summary = this.detailsField.text;
            reportParams.name = this.contactNameField.text;

            // Create sending message dialog
            messageData.header = "Report Submission Status";
            messageData.message = "Please wait while we submit your report.";
            messageData.onClose = () =>
            {
                ViewManager.instance.CloseWindowedView(ViewManager.instance.reportDialog);
                ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
            };
            messageData.standardButtonText = "...";
            ViewManager.instance.ShowMessageDialog(messageData);

            // Submit
            APIClient.SubmitReport(reportParams,
                                   this.OnReportSuccessful,
                                   this.OnReportFailed);
        }

        /// <summary>Callback for a successful report submission.</summary>
        private void OnReportSuccessful(APIMessage response)
        {
            var messageData = new MessageDialog.Data()
            {
                header = "Report Submission Status",
                message = "Report submission successful.\n" + response.message,
                standardButtonCallback = () =>
                {
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.reportDialog);
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
                },
                standardButtonText = "Done",
                onClose = () =>
                {
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.reportDialog);
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
                },
            };

            ViewManager.instance.ShowMessageDialog(messageData);
        }

        /// <summary>Callback for a failed report submission.</summary>
        private void OnReportFailed(WebRequestError error)
        {
            var messageData = new MessageDialog.Data()
            {
                header = "Report Submission Status",
                message = ("Report submission failed.\n"
                           + error.displayMessage
                           + "\n[Error Code: " + error.webRequest.responseCode.ToString() + "]"),
                standardButtonCallback = () =>
                {
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.reportDialog);
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
                },
                standardButtonText = "Done",
                onClose = () =>
                {
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.reportDialog);
                    ViewManager.instance.CloseWindowedView(ViewManager.instance.messageDialog);
                },
            };

            ViewManager.instance.ShowMessageDialog(messageData);
        }

    }
}
