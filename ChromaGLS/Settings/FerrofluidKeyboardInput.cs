#if FERROFLUID_TWEAKS
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal static class FerrofluidKeyboardInput
    {
        internal static bool IsReservedKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.T:
                case KeyCode.F:
                case KeyCode.G:
                case KeyCode.H:
                case KeyCode.I:
                case KeyCode.J:
                case KeyCode.K:
                case KeyCode.L:
                case KeyCode.Z:
                case KeyCode.X:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsReservedKeyName(string name)
            => name.Length == 1 && IsReservedKey((KeyCode)char.ToLowerInvariant(name[0]));

        internal static bool GetKey(KeyCode key) => Input.GetKeyInt(key);
    }
}
#endif
