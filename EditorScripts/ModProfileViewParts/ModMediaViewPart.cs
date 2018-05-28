﻿#if UNITY_EDITOR

using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace ModIO
{
    public class ModMediaViewPart : IModProfileViewPart
    {
        // ------[ CONSTANTS ]------
        private const ModGalleryImageSize IMAGE_PREVIEW_SIZE = ModGalleryImageSize.Thumbnail_320x180;
        private static readonly string[] IMAGE_FILE_FILTER = new string[] { "JPEG Image Format", "jpeg,jpg", "PNG Image Format", "png", "GIF Image Format", "gif" };

        // ------[ EDITOR CACHING ]------
        private bool isRepaintRequired = false;

        // - Serialized Property -
        private ModProfile profile;
        private SerializedProperty youtubeURLsProp;
        private SerializedProperty sketchfabURLsProp;
        private SerializedProperty galleryImagesProp;

        private string GetGalleryImageFileName(int index)
        {
            return (galleryImagesProp
                    .FindPropertyRelative("value")
                    .GetArrayElementAtIndex(index)
                    .FindPropertyRelative("fileName")
                    .stringValue);
        }

        private string GetGalleryImageSource(int index)
        {
            return (galleryImagesProp
                    .FindPropertyRelative("value")
                    .GetArrayElementAtIndex(index)
                    .FindPropertyRelative("url")
                    .stringValue);
        }

        private string GenerateUniqueFileName(string path)
        {
            string fileNameNoExtension = System.IO.Path.GetFileNameWithoutExtension(path);
            string fileExtension = System.IO.Path.GetExtension(path);
            int numberToAppend = 0;
            string regexPattern = fileNameNoExtension + "\\d*\\" + fileExtension;

            foreach(SerializedProperty elementProperty in galleryImagesProp.FindPropertyRelative("value"))
            {
                var elementFileName = elementProperty.FindPropertyRelative("fileName").stringValue;
                if(System.Text.RegularExpressions.Regex.IsMatch(elementFileName, regexPattern))
                {
                    string numberString = elementFileName.Substring(fileNameNoExtension.Length);
                    numberString = numberString.Substring(0, numberString.Length - fileExtension.Length);
                    int number;
                    if(!Int32.TryParse(numberString, out number))
                    {
                        number = 0;
                    }

                    if(numberToAppend <= number)
                    {
                        numberToAppend = number + 1;
                    }
                }
            }

            if(numberToAppend > 0)
            {
                fileNameNoExtension += numberToAppend.ToString();
            }

            return fileNameNoExtension + fileExtension;
        }

        // - Foldouts -
        private bool isYouTubeExpanded;
        private bool isSketchFabExpanded;
        private bool isImagesExpanded;

        private Dictionary<string, Texture2D> textureCache;


        // ------[ INITIALIZATION ]------
        public void OnEnable(SerializedProperty serializedEditableModProfile, ModProfile baseProfile, UserProfile user)
        {
            this.profile = baseProfile;
            this.youtubeURLsProp = serializedEditableModProfile.FindPropertyRelative("youtubeURLs");
            this.sketchfabURLsProp = serializedEditableModProfile.FindPropertyRelative("sketchfabURLs");
            this.galleryImagesProp = serializedEditableModProfile.FindPropertyRelative("galleryImageLocators");

            this.isYouTubeExpanded = false;
            this.isSketchFabExpanded = false;
            this.isImagesExpanded = false;

            // Initialize textureCache
            this.textureCache = new Dictionary<string, Texture2D>(galleryImagesProp.FindPropertyRelative("value").arraySize);
            for (int i = 0;
                 i < galleryImagesProp.FindPropertyRelative("value").arraySize;
                 ++i)
            {
                string imageFileName = GetGalleryImageFileName(i);
                string imageURL = GetGalleryImageSource(i);

                if(!String.IsNullOrEmpty(imageFileName)
                   && !String.IsNullOrEmpty(imageURL))
                {
                    this.textureCache[imageFileName] = ApplicationImages.LoadingPlaceholder;

                    Texture2D texture = CacheClient.ReadImageFile(imageURL);

                    if(texture != null)
                    {
                        this.textureCache[imageFileName] = texture;
                    }
                    else
                    {
                        ModManager.GetModGalleryImage(baseProfile,
                                                      imageFileName,
                                                      IMAGE_PREVIEW_SIZE,
                                                      (t) => { this.textureCache[imageFileName] = t; isRepaintRequired = true; },
                                                      WebRequestError.LogAsWarning);
                    }
                }
            }
        }

        public void OnDisable()
        {
        }

        // ------[ UPDATES ]------
        public void OnUpdate() {}

        protected virtual void OnModGalleryImageUpdated(int modId,
                                                        string imageFileName,
                                                        ModGalleryImageSize size,
                                                        Texture2D texture)
        {
            if(profile != null
               && profile.id == modId
               && size == IMAGE_PREVIEW_SIZE
               && textureCache.ContainsKey(imageFileName))
            {
                textureCache[imageFileName] = texture;
                isRepaintRequired = true;
            }
        }

        // ------[ GUI ]------
        public void OnGUI()
        {
            isRepaintRequired = false;

            EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Media");
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(profile == null))
                {
                    if(EditorGUILayoutExtensions.UndoButton())
                    {
                        ResetModMedia();
                    }
                }
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.IndentLevelScope())
            {
                // - YouTube -
                EditorGUI.BeginChangeCheck();
                EditorGUILayoutExtensions.ArrayPropertyField(youtubeURLsProp.FindPropertyRelative("value"),
                                                             "YouTube Links", ref isYouTubeExpanded);
                youtubeURLsProp.FindPropertyRelative("isDirty").boolValue |= EditorGUI.EndChangeCheck();
                // - SketchFab -
                EditorGUI.BeginChangeCheck();
                EditorGUILayoutExtensions.ArrayPropertyField(sketchfabURLsProp.FindPropertyRelative("value"),
                                                             "SketchFab Links", ref isSketchFabExpanded);
                sketchfabURLsProp.FindPropertyRelative("isDirty").boolValue |= EditorGUI.EndChangeCheck();
                // - Gallery Images -
                EditorGUI.BeginChangeCheck();
                EditorGUILayoutExtensions.CustomLayoutArrayPropertyField(galleryImagesProp.FindPropertyRelative("value"),
                                                                         "Gallery Images Links",
                                                                         ref isImagesExpanded,
                                                                         LayoutGalleryImageProperty);
                galleryImagesProp.FindPropertyRelative("isDirty").boolValue |= EditorGUI.EndChangeCheck();
            }
        }

        public bool IsRepaintRequired()
        {
            return this.isRepaintRequired;
        }

        // - Image Locator Layouting -
        private void LayoutGalleryImageProperty(int elementIndex,
                                                SerializedProperty elementProperty)
        {
            bool doBrowse = false;
            bool doClear = false;
            string imageFileName = elementProperty.FindPropertyRelative("fileName").stringValue;
            string imageSource = elementProperty.FindPropertyRelative("url").stringValue;

            // - Browse Field -
            EditorGUILayout.BeginHorizontal();
                doBrowse |= EditorGUILayoutExtensions.BrowseButton(imageSource,
                                                                   new GUIContent("Image " + elementIndex));
                doClear = EditorGUILayoutExtensions.ClearButton();
            EditorGUILayout.EndHorizontal();

            // - Draw Texture -
            Texture2D imageTexture = GetGalleryImageTexture(imageFileName, imageSource);

            if(imageTexture != null)
            {
                EditorGUI.indentLevel += 2;
                EditorGUILayout.LabelField("File Name", imageFileName);
                Rect imageRect = EditorGUILayout.GetControlRect(false, 180.0f, null);
                imageRect = EditorGUI.IndentedRect(imageRect);
                EditorGUI.DrawPreviewTexture(new Rect(imageRect.x,
                                                      imageRect.y,
                                                      320.0f,
                                                      imageRect.height),
                                             imageTexture, null, ScaleMode.ScaleAndCrop);
                doBrowse |= GUI.Button(imageRect, "", GUI.skin.label);
                EditorGUI.indentLevel -= 2;
            }

            if(doBrowse)
            {
                EditorApplication.delayCall += () =>
                {
                    string path = EditorUtility.OpenFilePanelWithFilters("Select Gallery Image",
                                                                         "",
                                                                         ModMediaViewPart.IMAGE_FILE_FILTER);
                    Texture2D newTexture = CacheClient.ReadImageFile(path);

                    if(newTexture != null)
                    {
                        string fileName = GenerateUniqueFileName(path);

                        elementProperty.FindPropertyRelative("url").stringValue = path;
                        elementProperty.FindPropertyRelative("fileName").stringValue = fileName;

                        galleryImagesProp.FindPropertyRelative("isDirty").boolValue = true;
                        galleryImagesProp.serializedObject.ApplyModifiedProperties();

                        textureCache.Add(fileName, newTexture);
                    }
                };
            }
            if(doClear)
            {
                elementProperty.FindPropertyRelative("url").stringValue = string.Empty;
                elementProperty.FindPropertyRelative("fileName").stringValue = string.Empty;

                galleryImagesProp.FindPropertyRelative("isDirty").boolValue = true;
                galleryImagesProp.serializedObject.ApplyModifiedProperties();
            }
        }

        private Texture2D GetGalleryImageTexture(string imageFileName,
                                                 string imageSource)
        {
            if(String.IsNullOrEmpty(imageFileName))
            {
                return null;
            }

            Texture2D texture;
            // - Get -
            if(this.textureCache.TryGetValue(imageFileName, out texture))
            {
                return texture;
            }
            // - Load -
            else if((texture = CacheClient.ReadImageFile(imageSource)) != null)
            {
                this.textureCache.Add(imageFileName, texture);
                return texture;
            }
            // - LoadOrDownload -
            else if(profile != null)
            {
                this.textureCache.Add(imageFileName, ApplicationImages.LoadingPlaceholder);

                ModManager.GetModGalleryImage(profile,
                                              imageFileName,
                                              IMAGE_PREVIEW_SIZE,
                                              (t) => { this.textureCache[imageFileName] = t; isRepaintRequired = true; },
                                              null);
                return this.textureCache[imageFileName];
            }

            return null;
        }

        // - Misc Functionality -
        private void ResetModMedia()
        {
            EditorUtilityExtensions.SetSerializedPropertyStringArray(youtubeURLsProp.FindPropertyRelative("value"),
                                                                     profile.media.youtubeURLs);
            youtubeURLsProp.FindPropertyRelative("isDirty").boolValue = false;

            EditorUtilityExtensions.SetSerializedPropertyStringArray(sketchfabURLsProp.FindPropertyRelative("value"),
                                                                     profile.media.sketchfabURLs);
            sketchfabURLsProp.FindPropertyRelative("isDirty").boolValue = false;

            // - Images -
            SerializedProperty galleryImagesArray = galleryImagesProp.FindPropertyRelative("value");

            galleryImagesArray.arraySize = profile.media.galleryImageLocators.Length;
            for(int i = 0; i < profile.media.galleryImageLocators.Length; ++i)
            {
                galleryImagesArray.GetArrayElementAtIndex(i).FindPropertyRelative("fileName").stringValue = profile.media.galleryImageLocators[i].GetFileName();
                galleryImagesArray.GetArrayElementAtIndex(i).FindPropertyRelative("url").stringValue = profile.media.galleryImageLocators[i].GetURL();
            }

            galleryImagesProp.FindPropertyRelative("isDirty").boolValue = false;
        }
    }
}

#endif
