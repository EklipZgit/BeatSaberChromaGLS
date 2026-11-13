using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidValidationArtifacts
    {
        internal static string CreateRunDirectory(string name)
        {
            string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            string path = Path.GetFullPath(Path.Combine(
                repository,
                "..",
                "Scratch",
                "ferro-adaptive-tracking",
                name + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
