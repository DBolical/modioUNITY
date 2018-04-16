#if UNITY_EDITOR

using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModIO
{
    public class ModSubmissionToolWindow : EditorWindow
    {
        [MenuItem("mod.io/Mod Submission Tool")]
        public static void ShowWindow()
        {
            GetWindow<ModSubmissionToolWindow>("Submit Mod");
        }

        // ------[ WINDOW FIELDS ]---------
        // - Login -
        private bool isInputtingEmail;
        private string emailAddressInput;
        private string securityCodeInput;
        private bool isRequestSending;
        // - Submission -
        private ScriptableModProfile profile;
        private AssetBundle build;

        // ------[ INITIALIZATION ]------
        protected virtual void OnEnable()
        {
            ModManager.Initialize();

            isInputtingEmail = true;
            emailAddressInput = "";
            securityCodeInput = "";
            isRequestSending = false;
        }

        protected virtual void OnDisable()
        {
        }

        // ---------[ UPDATES ]---------
        protected virtual void OnInspectorUpdate()
        {
            // TODO(@jackson): Repaint once uploaded
            // if(isRepaintRequired
            //    || wasActiveViewDisabled != activeView.IsViewDisabled()
            //    || wasHeaderDisabled != GetEditorHeader().IsInteractionDisabled())
            // {
            //     Repaint();
            //     isRepaintRequired = false;
            // }
            // wasActiveViewDisabled = activeView.IsViewDisabled();
            // wasHeaderDisabled = GetEditorHeader().IsInteractionDisabled();
        }

        // ---------[ GUI ]---------
        protected virtual void OnGUI()
        {
            if(ModManager.GetActiveUser() == null)
            {
                LayoutLoginPrompt();
            }
            else
            {
                LayoutSubmissionFields();
            }
        }

        // ------[ LOGIN PROMPT ]------
        protected virtual void LayoutLoginPrompt()
        {
            // TODO(@jackson): Improve with deselection/reselection of text on submit
            EditorGUILayout.LabelField("LOG IN TO/REGISTER YOUR MOD.IO ACCOUNT");

            using (new EditorGUI.DisabledScope(isRequestSending))
            {
                EditorGUILayout.BeginHorizontal();
                {
                    using (new EditorGUI.DisabledScope(isInputtingEmail))
                    {
                        if(GUILayout.Button("Email"))
                        {
                            isInputtingEmail = true;
                        }
                    }
                    using (new EditorGUI.DisabledScope(!isInputtingEmail))
                    {
                        if(GUILayout.Button("Security Code"))
                        {
                            isInputtingEmail = false;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();


                if(isInputtingEmail)
                {
                    emailAddressInput = EditorGUILayout.TextField("Email Address", emailAddressInput);
                }
                else
                {
                    securityCodeInput = EditorGUILayout.TextField("Security Code", securityCodeInput);
                }

                EditorGUILayout.BeginHorizontal();
                {
                    if(GUILayout.Button("Submit"))
                    {
                        isRequestSending = true;

                        Action endRequestSendingAndInputEmail = () =>
                        {
                            isRequestSending = false;
                            isInputtingEmail = true;
                        };

                        Action endRequestSendingAndInputCode = () =>
                        {
                            isRequestSending = false;
                            isInputtingEmail = false;
                        };

                        if(isInputtingEmail)
                        {
                            securityCodeInput = "";

                            ModManager.RequestSecurityCode(emailAddressInput,
                                                           m => endRequestSendingAndInputCode(),
                                                           e => endRequestSendingAndInputEmail());
                        }
                        else
                        {
                            Action<string> onTokenReceived = (token) =>
                            {
                                ModManager.TryLogUserIn(token,
                                                        (u) => { isRequestSending = false; Repaint(); },
                                                        e => endRequestSendingAndInputCode());
                            };

                            ModManager.RequestOAuthToken(securityCodeInput,
                                                         onTokenReceived,
                                                         e => endRequestSendingAndInputCode());
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        protected virtual void LayoutSubmissionFields()
        {
            // - Account Header -
            string username = ModManager.GetActiveUser().username;

            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Logged in as:  " + username);
                GUILayout.FlexibleSpace();
                if(GUILayout.Button("Log Out"))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if(EditorDialogs.ConfirmLogOut(username))
                        {
                            ModManager.LogUserOut();

                            isInputtingEmail = true;
                            emailAddressInput = "";
                            securityCodeInput = "";
                            isRequestSending = false;
                            
                            Repaint();
                        }
                    };
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // - Submission Section -
            if(profile == null)
            {
                EditorGUILayout.HelpBox("Please select a mod profile as a the upload target.",
                                        MessageType.Info);
            }
            else if(profile.modId > 0)
            {
                EditorGUILayout.HelpBox(profile.editableModProfile.name.value
                                        + " will be updated as used as the upload target on the server.",
                                        MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(profile.editableModProfile.name.value
                                        + " will be created as a new profile on the server.",
                                        MessageType.Info);
            }
            EditorGUILayout.Space();


            // TODO(@jackson): Support mods that haven't been downloaded
            profile = EditorGUILayout.ObjectField("Mod Profile",
                                                  profile,
                                                  typeof(ScriptableModProfile),
                                                  false) as ScriptableModProfile;

            using(new EditorGUI.DisabledScope(profile == null))
            {
                build = EditorGUILayout.ObjectField("Add Build",
                                                    build,
                                                    typeof(AssetBundle),
                                                    false) as AssetBundle;

                // TODO(@jackson): if(profile) -> show build list?

                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Button("Upload to Server");
                    GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}

#endif