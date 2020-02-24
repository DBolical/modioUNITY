namespace ModIO
{
    /// <summary>Describes the mod.io UnityPlugin version.</summary>
    [System.Serializable]
    public struct ModIOVersion : System.IComparable<ModIOVersion>
    {
        // ---------[ Singleton ]---------
        /// <summary>Singleton instance for current version.</summary>
        public static readonly ModIOVersion Current = new ModIOVersion(2, 2, 0);

        // ---------[ Fields ]---------
        /// <summary>Major version number.</summary>
        public int major;

        /// <summary>Minor version number.</summary>
        public int minor;

        /// <summary>Patch number for the current version.</summary>
        public int patch;

        // ---------[ Initialization ]---------
        /// <summary>Constructs an object with the given version values.</summary>
        public ModIOVersion(int majorVersion = 0, int minorVersion = 0, int patchNumber = 0)
        {
            this.major = majorVersion;
            this.minor = minorVersion;
            this.patch = patchNumber;
        }

        // ---------[ IComparable Interface ]---------
        /// <summary>Compares the current instance with another ModIOVersion.</summary>
        public int CompareTo(ModIOVersion other)
        {
            int result = this.major.CompareTo(other.major);

            if(result == 0)
            {
                result = this.minor.CompareTo(other.minor);

                if(result == 0)
                {
                    result = this.patch.CompareTo(other.patch);
                }
            }

            return result;
        }

        // ---------[ Operator Overloads ]---------
        public static bool operator >  (ModIOVersion a, ModIOVersion b)
        {
           return a.CompareTo(b) == 1;
        }

        public static bool operator <  (ModIOVersion a, ModIOVersion b)
        {
           return a.CompareTo(b) == -1;
        }

        public static bool operator >=  (ModIOVersion a, ModIOVersion b)
        {
           return a.CompareTo(b) >= 0;
        }

        public static bool operator <=  (ModIOVersion a, ModIOVersion b)
        {
           return a.CompareTo(b) <= 0;
        }
    }
}