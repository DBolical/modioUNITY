#if UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
    #define MODIO_ENABLE_VARIABLE_PATHS
#endif

using Path = System.IO.Path;

using UnityEngine;

namespace ModIO
{
    /// <summary>Stores the settings used by various classes that are unique to the game/app.</summary>
    public class PluginSettings : ScriptableObject, ISerializationCallbackReceiver
    {
        // ---------[ NESTED CLASSES ]---------
        /// <summary>Attribute for denoting a field as containing directory variables.</summary>
        public class VariableDirectoryAttribute : PropertyAttribute {}

        /// <summary>Request logging options.</summary>
        [System.Serializable]
        public struct RequestLoggingOptions
        {
            [Tooltip("Should failed requests be logged as warnings")]
            public bool errorsAsWarnings;

            [Tooltip("Log all web request responses made received")]
            public bool logAllResponses;

            [Tooltip("Should the sending of a request be logged separately")]
            public bool logOnSend;
        }

        /// <summary>Data struct that is wrapped by the ScriptableObject.</summary>
        [System.Serializable]
        public struct Data
        {
            // ---------[ Versioning ]---------
            internal const int VERSION = 2;

            [HideInInspector]
            [VersionedData(VERSION, VERSION)]
            public int version;

            // ---------[ Fields ]---------
            [Header("API Settings")]
            [Tooltip("API URL to use when making requests")]
            [VersionedData(0, "")]
            public string apiURL;

            [Tooltip("Game Id assigned to your game profile")]
            [VersionedData(0, GameProfile.NULL_ID)]
            public int gameId;

            [Tooltip("API Key assigned to your game profile")]
            [VersionedData(0, "")]
            public string gameAPIKey;

            [Tooltip("User Portal that this build of the game will be launching through.")]
            [VersionedData(2, UserPortal.None)]
            public UserPortal userPortal;

            [Tooltip("Amount of memory the request cache is permitted to grow to (KB)."
                     + "\nA negative value indicates an unlimited cache size.")]
            [VersionedData(1, (int)-1)]
            public int requestCacheSizeKB;

            /// <summary>Request logging options.</summary>
            public RequestLoggingOptions requestLogging;

            [Header("Standalone Directories")]
            [Tooltip("Directory to use for mod installations")]
            [VersionedData(0, @"$DATA_PATH$/mod.io/mods")]
            [VariableDirectory]
            public string installationDirectory;

            [Tooltip("Directory to use for cached server data")]
            [VersionedData(0, @"$DATA_PATH$/mod.io/cache")]
            [VariableDirectory]
            public string cacheDirectory;

            [Tooltip("Directory to use for user data")]
            [VersionedData(0, @"$PERSISTENT_DATA_PATH$/mod.io-$GAME_ID$")]
            [VariableDirectory]
            public string userDirectory;

            [Header("Editor Directories")]
            [Tooltip("Directory to use for mod installations")]
            [VersionedData(0, @"$CURRENT_DIRECTORY$/mod.io/editor/$GAME_ID$/mods")]
            [VariableDirectory]
            public string installationDirectoryEditor;

            [Tooltip("Directory to use for cached server data")]
            [VersionedData(0, @"$CURRENT_DIRECTORY$/mod.io/editor/$GAME_ID$/cache")]
            [VariableDirectory]
            public string cacheDirectoryEditor;

            [Tooltip("Directory to use for user data")]
            [VersionedData(0, @"$CURRENT_DIRECTORY$/mod.io/editor/$GAME_ID$/user")]
            [VariableDirectory]
            public string userDirectoryEditor;

            // ---------[ Obsolete ]---------
            [System.Obsolete("Use requestLogging.logAllResponses instead.")]
            public bool logAllRequests
            {
                get { return this.requestLogging.logAllResponses; }
                set { this.requestLogging.logAllResponses = value; }
            }
        }

        // ---------[ CONSTANTS & STATICS ]---------
        /// <summary>Location of the settings file.</summary>
        public static readonly string FILE_PATH = "modio_settings";

        /// <summary>Has the asset been loaded.</summary>
        private static bool _loaded = false;

        /// <summary>Singleton instance.</summary>
        private static Data _dataInstance;

        /// <summary>The values that the plugin should use.</summary>
        public static Data data
        {
            get
            {
                if(!PluginSettings._loaded)
                {
                    PluginSettings._dataInstance = PluginSettings.LoadDataFromAsset(PluginSettings.FILE_PATH);

                    #if UNITY_EDITOR
                    {
                        // If Application isn't playing, we reload every time
                        PluginSettings._loaded = Application.isPlaying;
                    }
                    #else
                        PluginSettings._loaded = true;
                    #endif // UNITY_EDITOR

                    #if DEBUG
                    if(Application.isPlaying)
                    {
                        string errorMessage = null;

                        // check config
                        if(string.IsNullOrEmpty(PluginSettings._dataInstance.apiURL))
                        {
                            errorMessage = ("[mod.io] API URL is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        else if(PluginSettings._dataInstance.gameId == GameProfile.NULL_ID)
                        {
                            errorMessage = ("[mod.io] Game ID is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        else if(string.IsNullOrEmpty(PluginSettings._dataInstance.gameAPIKey))
                        {
                            errorMessage = ("[mod.io] Game API Key is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        else if(string.IsNullOrEmpty(PluginSettings._dataInstance.installationDirectory))
                        {
                            errorMessage = ("[mod.io] Installation Directory is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        else if(string.IsNullOrEmpty(PluginSettings._dataInstance.cacheDirectory))
                        {
                            errorMessage = ("[mod.io] Cache Directory is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        else if(string.IsNullOrEmpty(PluginSettings._dataInstance.userDirectory))
                        {
                            errorMessage = ("[mod.io] User Directory is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        #if UNITY_EDITOR
                        else if(string.IsNullOrEmpty(PluginSettings._dataInstance.installationDirectoryEditor))
                        {
                            errorMessage = ("[mod.io] Installation Directory (Editor) is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        else if(string.IsNullOrEmpty(PluginSettings._dataInstance.cacheDirectoryEditor))
                        {
                            errorMessage = ("[mod.io] Cache Directory (Editor) is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        else if(string.IsNullOrEmpty(PluginSettings._dataInstance.userDirectoryEditor))
                        {
                            errorMessage = ("[mod.io] User Directory (Editor) is missing from the Plugin Settings.\n"
                                           + "This must be configured by selecting the mod.io > Edit Settings menu"
                                           + " item before the mod.io Unity Plugin can be used.");
                        }
                        #endif

                        if(errorMessage != null)
                        {
                            #if UNITY_EDITOR
                                PluginSettings.FocusAsset();
                            #endif // UNITY_EDITOR

                            Debug.LogError(errorMessage);
                        }
                    }
                    #endif // DEBUG
                }

                return PluginSettings._dataInstance;
            }
        }

        // ---------[ FIELDS ]---------
        /// <summary>Settings data.</summary>
        [SerializeField]
        #pragma warning disable 0649
        private Data m_data;
        #pragma warning restore 0649

        // --- Accessors ---
        public static string API_URL
        {
            get { return PluginSettings.data.apiURL; }
        }
        public static int GAME_ID
        {
            get { return PluginSettings.data.gameId; }
        }
        public static string GAME_API_KEY
        {
            get { return PluginSettings.data.gameAPIKey; }
        }
        public static RequestLoggingOptions REQUEST_LOGGING
        {
            get { return PluginSettings.data.requestLogging; }
        }
        public static UserPortal USER_PORTAL
        {
            get { return PluginSettings.data.userPortal; }
        }
        public static uint CACHE_SIZE_BYTES
        {
            get
            {
                if(PluginSettings.data.requestCacheSizeKB < 0)
                {
                    return uint.MaxValue;
                }
                else
                {
                    return (uint)PluginSettings.data.requestCacheSizeKB * 1024;
                }
            }
        }

        // ---------[ FUNCTIONALITY ]---------
        /// <summary>Loads the data from a PluginSettings asset.</summary>
        public static PluginSettings.Data LoadDataFromAsset(string assetPath)
        {
            PluginSettings wrapper = Resources.Load<PluginSettings>(assetPath);
            PluginSettings.Data settings;

            if(wrapper == null)
            {
                settings = new Data();
            }
            else
            {
                settings = wrapper.m_data;

                // - Path variable replacement -
                #if MODIO_ENABLE_VARIABLE_PATHS && !UNITY_EDITOR

                    // cachedir
                    if(settings.cacheDirectory != null)
                    {
                        settings.cacheDirectory = ReplaceDirectoryVariables(settings.cacheDirectory,
                                                                            settings.gameId);
                    }

                    // installdir
                    if(settings.installationDirectory != null)
                    {
                        settings.installationDirectory = ReplaceDirectoryVariables(settings.installationDirectory,
                                                                                   settings.gameId);
                    }

                    // userdir
                    if(settings.userDirectory != null)
                    {
                        settings.userDirectory = ReplaceDirectoryVariables(settings.userDirectory,
                                                                           settings.gameId);
                    }

                    Debug.Log("[mod.io] PluginSettings variable directories resolved to:"
                              + "\n.cacheDirectory=" + settings.cacheDirectory
                              + "\n.installationDirectory=" + settings.installationDirectory
                              + "\n.userDirectory=" + settings.userDirectory);

                #endif // MODIO_ENABLE_VARIABLE_PATHS && !UNITY_EDITOR

                #if UNITY_EDITOR

                    // cachedir
                    if(settings.cacheDirectoryEditor != null)
                    {
                        settings.cacheDirectoryEditor = ReplaceDirectoryVariables(settings.cacheDirectoryEditor,
                                                                                  settings.gameId);
                    }

                    // installdir
                    if(settings.installationDirectoryEditor != null)
                    {
                        settings.installationDirectoryEditor = ReplaceDirectoryVariables(settings.installationDirectoryEditor,
                                                                                         settings.gameId);
                    }

                    // userdir
                    if(settings.userDirectoryEditor != null)
                    {
                        settings.userDirectoryEditor = ReplaceDirectoryVariables(settings.userDirectoryEditor,
                                                                                 settings.gameId);
                    }

                    Debug.Log("[mod.io] PluginSettings variable directories resolved to:"
                              + "\n.cacheDirectoryEditor=" + settings.cacheDirectoryEditor
                              + "\n.installationDirectoryEditor=" + settings.installationDirectoryEditor
                              + "\n.userDirectoryEditor=" + settings.userDirectoryEditor);

                #endif // UNITY_EDITOR

            }

            return settings;
        }

        /// <summary>Replaces variables in the directory values.</summary>
        public static string ReplaceDirectoryVariables(string directory, int gameId)
        {
            // remove any trailing DSCs from Application paths
            string app_persistentDataPath = Application.persistentDataPath;
            if(IOUtilities.PathEndsWithDirectorySeparator(app_persistentDataPath))
            {
                app_persistentDataPath = app_persistentDataPath.Remove(app_persistentDataPath.Length-1);
            }
            string app_dataPath = Application.dataPath;
            if(IOUtilities.PathEndsWithDirectorySeparator(app_dataPath))
            {
                app_dataPath = app_dataPath.Remove(app_dataPath.Length-1);
            }
            string app_temporaryCachePath = Application.temporaryCachePath;
            if(IOUtilities.PathEndsWithDirectorySeparator(app_temporaryCachePath))
            {
                app_temporaryCachePath = app_temporaryCachePath.Remove(app_temporaryCachePath.Length-1);
            }

            // straight replaces
            directory = (directory
                         .Replace("$PERSISTENT_DATA_PATH$", app_persistentDataPath)
                         .Replace("$DATA_PATH$", app_dataPath)
                         .Replace("$TEMPORARY_CACHE_PATH$", app_temporaryCachePath)
                         .Replace("$BUILD_GUID$", Application.buildGUID)
                         .Replace("$COMPANY_NAME$", Application.companyName)
                         .Replace("$PRODUCT_NAME$", Application.productName)
                         .Replace("$APPLICATION_IDENTIFIER", Application.identifier)
                         .Replace("$GAME_ID$", gameId.ToString())
                         .Replace("$CURRENT_DIRECTORY$", System.IO.Directory.GetCurrentDirectory())
                         );

            return directory;
        }

        /// <summary>Creates an updated version of passed PluginSettings.Data.</summary>
        public static PluginSettings.Data UpdateVersionedValues(int dataVersion,
                                                                PluginSettings.Data dataValues)
        {
            // early out
            if(dataVersion >= PluginSettings.Data.VERSION)
            {
                return dataValues;
            }
            else
            {
                return VersionedDataAttribute.UpdateStructFields(dataVersion, dataValues);
            }
        }

        // ---------[ ISerializationCallbackReceiver ]---------
        /// <summary>Implement this method to receive a callback after Unity deserializes your object.</summary>
        public void OnAfterDeserialize()
        {
            this.m_data = PluginSettings.UpdateVersionedValues(this.m_data.version, this.m_data);
        }

        /// <summary>Implement this method to receive a callback before Unity serializes your object.</summary>
        public void OnBeforeSerialize() {}

        // ---------[ EDITOR CODE ]---------
        #if UNITY_EDITOR
        /// <summary>Locates the PluginSettings asset used at runtime.</summary>
        [UnityEditor.MenuItem("Tools/mod.io/Edit Settings", false)]
        public static void FocusAsset()
        {
            PluginSettings settings = Resources.Load<PluginSettings>(PluginSettings.FILE_PATH);

            if(settings == null)
            {
                PluginSettings.Data defaultData = PluginSettings.GenerateDefaultData();
                settings = PluginSettings.SetRuntimeData(defaultData);
            }

            UnityEditor.EditorGUIUtility.PingObject(settings);
            UnityEditor.Selection.activeObject = settings;
        }

        /// <summary>Generates a PluginSettings.Data instance with runtime defaults.</summary>
        public static PluginSettings.Data GenerateDefaultData()
        {
            PluginSettings.Data data = new PluginSettings.Data();
            data = PluginSettings.UpdateVersionedValues(-1, data);

            // non-constant defaults
            data.apiURL = APIClient.API_URL_PRODUCTIONSERVER + APIClient.API_VERSION;
            data.requestLogging = new RequestLoggingOptions()
            {
                errorsAsWarnings = true,
                logAllResponses = false,
                logOnSend = false,
            };

            return data;
        }

        /// <summary>Stores the given values to the Runtime asset.</summary>
        public static PluginSettings SetRuntimeData(PluginSettings.Data data)
        {
            return PluginSettings.SaveToAsset(PluginSettings.FILE_PATH, data);
        }

        /// <summary>Sets/saves the settings for the runtime instance.</summary>
        public static PluginSettings SaveToAsset(string path,
                                                 PluginSettings.Data data)
        {
            string assetPath = IOUtilities.CombinePath("Assets", "Resources", path + ".asset");

            // creates the containing folder
            string assetFolder = Path.GetDirectoryName(assetPath);
            System.IO.Directory.CreateDirectory(assetFolder);

            // create asset
            PluginSettings settings = ScriptableObject.CreateInstance<PluginSettings>();
            settings.m_data = data;

            // save
            UnityEditor.AssetDatabase.CreateAsset(settings, assetPath);
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();

            return settings;
        }

        /// <summary>[Obsolete] Sets the values of the Plugin Settings.</summary>
        [System.Obsolete("Use PluginSettings.SetRuntimeData() instead.")]
        public static PluginSettings SetGlobalValues(PluginSettings.Data data)
        {
            return PluginSettings.SetRuntimeData(data);
        }

        /// <summary>[Obsolete] Creates the asset instance that the plugin will use.</summary>
        [System.Obsolete("Use PluginSettings.GenerateDefaultData() and PluginSettings.SetRuntimeData() instead.")]
        private static PluginSettings InitializeAsset()
        {
            PluginSettings.Data data = PluginSettings.GenerateDefaultData();
            return PluginSettings.SetRuntimeData(data);
        }

        #endif // UNITY_EDITOR

        // ---------[ Obsolete ]---------
        [System.Obsolete("Use DataStorage.INSTALLATION_DIRECTORY instead.")]
        public static string INSTALLATION_DIRECTORY
        {
            get { return DataStorage.INSTALLATION_DIRECTORY; }
        }

        [System.Obsolete("Use DataStorage.CACHE_DIRECTORY instead.")]
        public static string CACHE_DIRECTORY
        {
            get { return DataStorage.CACHE_DIRECTORY; }
        }

        [System.Obsolete("Use UserDataStorage.USER_DIRECTORY instead.")]
        public static string USER_DIRECTORY
        {
            get { return UserDataStorage.USER_DIRECTORY; }
        }
    }
}
