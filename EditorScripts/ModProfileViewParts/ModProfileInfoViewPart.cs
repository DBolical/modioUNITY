﻿#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace ModIO
{
    public class ModProfileInfoViewPart : IModProfileViewPart
    {
        // ------[ CONSTANTS ]------
        private const LogoSize LOGO_PREVIEW_SIZE = LogoSize.Thumbnail_320x180;
        private const float LOGO_PREVIEW_WIDTH = 320;
        private const float LOGO_PREVIEW_HEIGHT = 180;
        private static readonly string[] IMAGE_FILE_FILTER = new string[] { "JPEG Image Format", "jpeg,jpg", "PNG Image Format", "png", "GIF Image Format", "gif" };

        // ------[ EDITOR CACHING ]------
        private SerializedProperty editableProfileProperty;
        private GameProfile gameProfile;
        private ModProfile profile;
        private bool isUndoEnabled = false;
        private bool isRepaintRequired = false;

        // - Logo -
        private SerializedProperty logoProperty;
        private Texture2D logoTexture;
        private string logoLocation;
        private DateTime lastLogoWriteTime;

        // - Tags -
        private bool isTagsExpanded;
        private bool isKVPsExpanded;

        // ------[ INITIALIZATION ]------
        public void OnEnable(SerializedProperty serializedEditableModProfile, ModProfile baseProfile, UserProfile user)
        {
            this.editableProfileProperty = serializedEditableModProfile;
            this.profile = baseProfile;
            this.isUndoEnabled = (baseProfile != null);

            isTagsExpanded = false;
            isKVPsExpanded = false;

            // - Game Profile -
            ModManager.GetGameProfile((g) => { this.gameProfile = g; isRepaintRequired = true; },
                                      null);

            // - Configure Properties -
            logoProperty = editableProfileProperty.FindPropertyRelative("logoLocator");

            // - Load Textures -
            if(logoProperty.FindPropertyRelative("isDirty").boolValue == true)
            {
                logoLocation = logoProperty.FindPropertyRelative("value.url").stringValue;
                logoTexture = CacheClient.ReadImageFile(logoLocation);
                if(logoTexture != null)
                {
                    lastLogoWriteTime = (new FileInfo(logoLocation)).LastWriteTime;
                }
            }
            else if(profile != null)
            {
                logoLocation = profile.logoLocator.GetSizeURL(LOGO_PREVIEW_SIZE);
                logoTexture = ApplicationImages.LoadingPlaceholder;

                ModManager.GetModLogo(profile, LOGO_PREVIEW_SIZE,
                                      (t) => { logoTexture = t; isRepaintRequired = true; },
                                      WebRequestError.LogAsWarning);
            }
            else
            {
                logoLocation = string.Empty;
                logoTexture = null;
            }
        }

        public void OnDisable() { }

        // ------[ UPDATE ]------
        public void OnUpdate()
        {
            if(File.Exists(logoLocation))
            {
                try
                {
                    FileInfo imageInfo = new FileInfo(logoLocation);
                    if(lastLogoWriteTime < imageInfo.LastWriteTime)
                    {
                        logoTexture = CacheClient.ReadImageFile(logoLocation);
                        lastLogoWriteTime = imageInfo.LastWriteTime;
                    }
                }
                catch(Exception e)
                {
                    Debug.LogWarning("[mod.io] Unable to read updates to the logo image file.\n\n"
                                     + Utility.GenerateExceptionDebugString(e));
                }
            }
        }

        // ------[ GUI ]------
        public virtual void OnGUI()
        {
            isRepaintRequired = false;

            LayoutNameField();
            LayoutNameIDField();
            LayoutLogoField();
            LayoutVisibilityField();
            LayoutTagsField();
            LayoutHomepageField();
            LayoutSummaryField();
            LayoutDescriptionField();
            LayoutMetadataBlobField();
            LayoutMetadataKVPField();
        }

        public virtual bool IsRepaintRequired()
        {
            return this.isRepaintRequired;
        }

        // ---------[ SIMPLE LAYOUT FUNCTIONS ]---------
        protected void LayoutEditablePropertySimple(string fieldName,
                                                    SerializedProperty fieldProperty)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(fieldProperty.FindPropertyRelative("value"),
                                          new GUIContent(fieldName));
            if(EditorGUI.EndChangeCheck())
            {
                fieldProperty.FindPropertyRelative("isDirty").boolValue = true;
            }
        }

        protected virtual void LayoutNameField()
        {
            EditorGUILayout.BeginHorizontal();
                LayoutEditablePropertySimple("Name", editableProfileProperty.FindPropertyRelative("name"));
                bool isUndoRequested = EditorGUILayoutExtensions.UndoButton(isUndoEnabled);
            EditorGUILayout.EndHorizontal();

            if(isUndoRequested)
            {
                editableProfileProperty.FindPropertyRelative("name.value").stringValue = profile.name;
                editableProfileProperty.FindPropertyRelative("name.isDirty").boolValue = false;
            }
        }

        protected virtual void LayoutVisibilityField()
        {
            EditorGUILayout.BeginHorizontal();
                LayoutEditablePropertySimple("Visibility", editableProfileProperty.FindPropertyRelative("visibility"));
                bool isUndoRequested = EditorGUILayoutExtensions.UndoButton(isUndoEnabled);
            EditorGUILayout.EndHorizontal();

            if(isUndoRequested)
            {
                editableProfileProperty.FindPropertyRelative("visibility.value").intValue = (int)profile.visibility;
                editableProfileProperty.FindPropertyRelative("visibility.isDirty").boolValue = false;
            }
        }

        protected virtual void LayoutHomepageField()
        {
            EditorGUILayout.BeginHorizontal();
                LayoutEditablePropertySimple("Homepage", editableProfileProperty.FindPropertyRelative("homepageURL"));
                bool isUndoRequested = EditorGUILayoutExtensions.UndoButton(isUndoEnabled);
            EditorGUILayout.EndHorizontal();

            if(isUndoRequested)
            {
                editableProfileProperty.FindPropertyRelative("homepageURL.value").stringValue = profile.homepageURL;
                editableProfileProperty.FindPropertyRelative("homepageURL.isDirty").boolValue = false;
            }
        }

        // ---------[ TEXT AREAS ]---------
        protected void LayoutEditablePropertyTextArea(string fieldName,
                                                      SerializedProperty fieldProperty,
                                                      int characterLimit,
                                                      out bool isUndoRequested)
        {
            SerializedProperty fieldValueProperty = fieldProperty.FindPropertyRelative("value");
            int charCount = fieldValueProperty.stringValue.Length;

            EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(fieldName);
                EditorGUILayout.LabelField("[" + (characterLimit - charCount).ToString()
                                           + " characters remaining]");
                isUndoRequested = EditorGUILayoutExtensions.UndoButton(isUndoEnabled);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
                fieldValueProperty.stringValue
                    = EditorGUILayoutExtensions.MultilineTextField(fieldValueProperty.stringValue);
            if(EditorGUI.EndChangeCheck())
            {
                if(fieldValueProperty.stringValue.Length > characterLimit)
                {
                    fieldValueProperty.stringValue
                        = fieldValueProperty.stringValue.Substring(0, characterLimit);
                }

                fieldProperty.FindPropertyRelative("isDirty").boolValue = true;
            }
        }

        protected virtual void LayoutSummaryField()
        {
            bool isUndoRequested;
            LayoutEditablePropertyTextArea("Summary",
                                           editableProfileProperty.FindPropertyRelative("summary"),
                                           API.EditModParameters.SUMMARY_CHAR_LIMIT,
                                           out isUndoRequested);
            if(isUndoRequested)
            {
                editableProfileProperty.FindPropertyRelative("summary.value").stringValue = profile.summary;
                editableProfileProperty.FindPropertyRelative("summary.isDirty").boolValue = false;
            }
        }

        protected virtual void LayoutDescriptionField()
        {
            bool isUndoRequested;
            LayoutEditablePropertyTextArea("Description",
                                           editableProfileProperty.FindPropertyRelative("description"),
                                           API.EditModParameters.DESCRIPTION_CHAR_LIMIT,
                                           out isUndoRequested);

            if(isUndoRequested)
            {
                editableProfileProperty.FindPropertyRelative("description.value").stringValue = profile.description;
                editableProfileProperty.FindPropertyRelative("description.isDirty").boolValue = false;
            }
        }

        protected virtual void LayoutMetadataBlobField()
        {
            bool isUndoRequested;
            LayoutEditablePropertyTextArea("Metadata",
                                           editableProfileProperty.FindPropertyRelative("metadataBlob"),
                                           API.EditModParameters.DESCRIPTION_CHAR_LIMIT,
                                           out isUndoRequested);

            if(isUndoRequested)
            {
                editableProfileProperty.FindPropertyRelative("metadataBlob.value").stringValue = profile.metadataBlob;
                editableProfileProperty.FindPropertyRelative("metadataBlob.isDirty").boolValue = false;
            }
        }

        // ---------[ SUPER JANKY ]---------
        protected virtual void LayoutNameIDField()
        {
            bool isDirty = false;
            EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Profile URL");
                EditorGUILayout.LabelField("@", GUILayout.Width(13));

                EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(editableProfileProperty.FindPropertyRelative("nameId.value"),
                                                  GUIContent.none);
                isDirty = EditorGUI.EndChangeCheck();

                bool isUndoRequested = EditorGUILayoutExtensions.UndoButton(isUndoEnabled);
            EditorGUILayout.EndHorizontal();

            editableProfileProperty.FindPropertyRelative("nameId.isDirty").boolValue |= isDirty;

            if(isUndoRequested)
            {
                editableProfileProperty.FindPropertyRelative("nameId.value").stringValue = profile.nameId;
                editableProfileProperty.FindPropertyRelative("nameId.isDirty").boolValue = false;
            }
        }

        protected virtual void LayoutLogoField()
        {
            bool doBrowse = false;

            // - Browse Field -
            EditorGUILayout.BeginHorizontal();
                doBrowse |= EditorGUILayoutExtensions.BrowseButton(logoLocation,
                                                                   new GUIContent("Logo"));
                bool isUndoRequested = EditorGUILayoutExtensions.UndoButton(isUndoEnabled);
            EditorGUILayout.EndHorizontal();

            // - Draw Texture -
            if(logoTexture != null)
            {
                Rect logoRect = EditorGUILayout.GetControlRect(false,
                                                               LOGO_PREVIEW_HEIGHT,
                                                               null);
                EditorGUI.DrawPreviewTexture(new Rect((logoRect.width - LOGO_PREVIEW_WIDTH) * 0.5f,
                                                      logoRect.y,
                                                      LOGO_PREVIEW_WIDTH,
                                                      logoRect.height),
                                             logoTexture,
                                             null,
                                             ScaleMode.ScaleAndCrop);
                doBrowse |= GUI.Button(logoRect, "", GUI.skin.label);
            }

            if(doBrowse)
            {
                EditorApplication.delayCall += () =>
                {
                    string path = EditorUtility.OpenFilePanelWithFilters("Select Mod Logo",
                                                                         "",
                                                                         IMAGE_FILE_FILTER);
                    Texture2D newLogoTexture = CacheClient.ReadImageFile(path);

                    if(newLogoTexture)
                    {
                        logoProperty.FindPropertyRelative("value.url").stringValue = path;
                        logoProperty.FindPropertyRelative("value.fileName").stringValue = Path.GetFileName(path);
                        logoProperty.FindPropertyRelative("isDirty").boolValue = true;
                        logoProperty.serializedObject.ApplyModifiedProperties();

                        logoTexture = newLogoTexture;
                        logoLocation = path;
                        lastLogoWriteTime = (new FileInfo(logoLocation)).LastWriteTime;
                    }
                };
            }

            if(isUndoRequested)
            {
                logoProperty.FindPropertyRelative("value.url").stringValue = profile.logoLocator.GetURL();
                logoProperty.FindPropertyRelative("value.fileName").stringValue = profile.logoLocator.GetFileName();
                logoProperty.FindPropertyRelative("isDirty").boolValue = false;

                logoLocation = profile.logoLocator.GetSizeURL(LOGO_PREVIEW_SIZE);
                logoTexture = ApplicationImages.LoadingPlaceholder;

                ModManager.GetModLogo(profile, LOGO_PREVIEW_SIZE,
                                      (t) => { logoTexture = t; isRepaintRequired = true; },
                                      WebRequestError.LogAsWarning);
            }
        }

        protected virtual void LayoutTagsField()
        {
            using(new EditorGUI.DisabledScope(this.gameProfile == null))
            {
                EditorGUILayout.BeginHorizontal();
                    this.isTagsExpanded = EditorGUILayout.Foldout(this.isTagsExpanded, "Tags", true);
                    GUILayout.FlexibleSpace();
                    bool isUndoRequested = EditorGUILayoutExtensions.UndoButton(isUndoEnabled);
                EditorGUILayout.EndHorizontal();

                if(this.isTagsExpanded)
                {
                    if(this.gameProfile == null)
                    {
                        EditorGUILayout.HelpBox("The Game's Profile is not yet loaded, and thus tags cannot be displayed. Please wait...", MessageType.Warning);
                    }
                    else if(this.gameProfile.taggingOptions.Length == 0)
                    {
                        EditorGUILayout.HelpBox("The developers of "
                                                + this.gameProfile.name
                                                + " have not designated any tagging options",
                                                MessageType.Info);
                    }
                    else
                    {
                        var tagsProperty = editableProfileProperty.FindPropertyRelative("tags.value");
                        var selectedTags = new List<string>(EditorUtilityExtensions.GetSerializedPropertyStringArray(tagsProperty));
                        bool isDirty = false;

                        ++EditorGUI.indentLevel;
                            foreach(ModTagCategory tagCategory in this.gameProfile.taggingOptions)
                            {
                                if(!tagCategory.isHidden)
                                {
                                    bool wasSelectionModified;
                                    LayoutTagCategoryField(tagCategory, ref selectedTags, out wasSelectionModified);
                                    isDirty |= wasSelectionModified;
                                }
                            }
                        --EditorGUI.indentLevel;

                        if(isDirty)
                        {
                            EditorUtilityExtensions.SetSerializedPropertyStringArray(tagsProperty, selectedTags.ToArray());
                            editableProfileProperty.FindPropertyRelative("tags.isDirty").boolValue = true;
                        }
                    }

                    if(isUndoRequested)
                    {
                        var tagsProperty = editableProfileProperty.FindPropertyRelative("tags.value");
                        EditorUtilityExtensions.SetSerializedPropertyStringArray(tagsProperty,
                                                                                 profile.tagNames.ToArray());
                        editableProfileProperty.FindPropertyRelative("tags.isDirty").boolValue = false;
                    }
                }
            }
        }

        protected virtual void LayoutTagCategoryField(ModTagCategory tagCategory,
                                                      ref List<string> selectedTags,
                                                      out bool wasSelectionModified)
        {
            wasSelectionModified = false;

            // EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(tagCategory.name);

            EditorGUILayout.BeginVertical();
                if(!tagCategory.isMultiTagCategory)
                {
                    string oldSelectedTag = string.Empty;
                    foreach(string tag in tagCategory.tags)
                    {
                        if(selectedTags.Contains(tag))
                        {
                            oldSelectedTag = tag;
                        }
                    }

                    string newSelectedTag = oldSelectedTag;
                    foreach(string tag in tagCategory.tags)
                    {
                        bool isSelected = (tag == oldSelectedTag);

                        using (var check = new EditorGUI.ChangeCheckScope())
                        {
                            isSelected = EditorGUILayout.Toggle(tag, isSelected, EditorStyles.radioButton);
                            if (check.changed)
                            {
                                if(isSelected)
                                {
                                    newSelectedTag = tag;
                                }
                                else
                                {
                                    newSelectedTag = string.Empty;
                                }
                            }
                        }
                    }

                    if(newSelectedTag != oldSelectedTag)
                    {
                        wasSelectionModified = true;

                        selectedTags.Remove(oldSelectedTag);

                        if(!System.String.IsNullOrEmpty(newSelectedTag))
                        {
                            selectedTags.Add(newSelectedTag);
                        }
                    }
                }
                else
                {
                    foreach(string tag in tagCategory.tags)
                    {
                        bool wasSelected = selectedTags.Contains(tag);
                        bool isSelected = EditorGUILayout.Toggle(tag, wasSelected);

                        if(wasSelected != isSelected)
                        {
                            wasSelectionModified = true;

                            if(isSelected)
                            {
                                selectedTags.Add(tag);
                            }
                            else
                            {
                                selectedTags.Remove(tag);
                            }
                        }
                    }
                }
            EditorGUILayout.EndVertical();
            // EditorGUILayout.EndHorizontal();
        }

        protected virtual void LayoutMetadataKVPField()
        {
            SerializedProperty mkvpsProp = editableProfileProperty.FindPropertyRelative("metadataKVPs.value");

            EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginVertical();
                    EditorGUILayoutExtensions.CustomLayoutArrayPropertyField(mkvpsProp,
                                                                             "Metadata KVPs",
                                                                             ref isKVPsExpanded,
                                                                             LayoutMetadataKVPEntryField);
                EditorGUILayout.EndVertical();

                bool isDirty = EditorGUI.EndChangeCheck();
                bool isUndoRequested = EditorGUILayoutExtensions.UndoButton(isUndoEnabled);
            EditorGUILayout.EndHorizontal();

            editableProfileProperty.FindPropertyRelative("metadataKVPs.isDirty").boolValue |= isDirty;

            if(isUndoRequested)
            {
                mkvpsProp.arraySize = profile.metadataKVPs.Length;
                for(int i = 0; i < profile.metadataKVPs.Length; ++i)
                {
                    mkvpsProp.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue = profile.metadataKVPs[i].key;
                    mkvpsProp.GetArrayElementAtIndex(i).FindPropertyRelative("value").stringValue = profile.metadataKVPs[i].value;
                }

                editableProfileProperty.FindPropertyRelative("metadataKVPs.isDirty").boolValue = false;
            }
        }

        protected virtual void LayoutMetadataKVPEntryField(int elementIndex,
                                                           SerializedProperty kvpProperty)
        {
            EditorGUILayout.PrefixLabel("Key Value Pair " + elementIndex.ToString());
            EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(kvpProperty.FindPropertyRelative("key"), GUIContent.none);
                EditorGUILayout.PropertyField(kvpProperty.FindPropertyRelative("value"), GUIContent.none);
            EditorGUILayout.EndHorizontal();
        }
    }
}

#endif
