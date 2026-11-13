#if FERROFLUID_TWEAKS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ChromaGLS.Settings
{
    // Timestamped capture directories under %TEMP%\CglsFerroSnapshots, pruned to the newest twenty.
    internal static class FerrofluidSnapshotStore
    {
        internal const int MaximumCaptureDirectories = 20;

        private const string StampFormat = "yyyy-MM-dd'T'HH-mm-ss.fffffff'Z'";

        internal static string FormatStamp(DateTime capturedUtc)
            => capturedUtc.ToUniversalTime().ToString(StampFormat, CultureInfo.InvariantCulture);

        internal static bool IsCaptureDirectoryName(string name)
            => TryParseCaptureName(name, out _, out _);

        // Stamp parses as UTC; a collision suffix ranks after the base name of the same stamp.
        internal static bool TryParseCaptureName(string name, out DateTime stamp, out int suffix)
        {
            stamp = default;
            suffix = 0;
            int suffixIndex = name.IndexOf('_');
            string stampText = suffixIndex >= 0 ? name.Substring(0, suffixIndex) : name;
            if (!DateTime.TryParseExact(stampText, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out stamp))
                return false;
            if (suffixIndex < 0)
                return true;
            return int.TryParse(name.Substring(suffixIndex + 1), NumberStyles.None, CultureInfo.InvariantCulture, out suffix);
        }

        internal static string CreateCaptureDirectory(string rootDirectory, DateTime capturedUtc)
        {
            Directory.CreateDirectory(rootDirectory);
            string stamp = FormatStamp(capturedUtc);
            for (int attempt = 0; ; attempt++)
            {
                string name = attempt == 0 ? stamp : $"{stamp}_{attempt + 1}";
                string path = Path.Combine(rootDirectory, name);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    return path;
                }
            }
        }

        // Sort by parsed stamp then numeric suffix: ordinal would rank _10 before _2 and prune the
        // newer collided captures first.
        internal static void PruneCaptureDirectories(string rootDirectory, int keep)
        {
            if (!Directory.Exists(rootDirectory))
                return;

            var captures = new List<(string Path, DateTime Stamp, int Suffix)>();
            foreach (string path in Directory.EnumerateDirectories(rootDirectory))
            {
                if (!TryParseCaptureName(Path.GetFileName(path), out DateTime stamp, out int suffix))
                    continue;
                if ((new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                captures.Add((path, stamp, suffix));
            }

            captures.Sort((a, b) =>
            {
                int order = a.Stamp.CompareTo(b.Stamp);
                return order != 0 ? order : a.Suffix.CompareTo(b.Suffix);
            });
            for (int index = 0; index < captures.Count - keep; index++)
                DeleteTree(captures[index].Path);
        }

        // Never recurse through reparse points: a junction inside a capture unlinks instead of
        // emptying the directory it points at.
        internal static void DeleteTree(string directory)
        {
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                var info = new DirectoryInfo(child);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    info.Delete(false);
                else
                    DeleteTree(child);
            }

            Directory.Delete(directory);
        }
    }
}
#endif
