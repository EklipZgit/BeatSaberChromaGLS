using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ChromaGLS.Updates
{
    /// <summary>
    /// Represents a GitHub release that qualifies for the ChromaGLS update banner.
    /// Validates release metadata, plugin versions, and published Beat Saber ZIP targets against the installed versions, including the Debug override.
    /// </summary>
    internal sealed class AvailablePluginUpdate
    {
        private const string TagNameKey = "tag_name";
        private const string ReleaseUrlKey = "html_url";
        private const string DraftKey = "draft";
        private const string PrereleaseKey = "prerelease";
        private const string AssetsKey = "assets";
        private const string AssetNameKey = "name";
        private const string ReleasePagePrefix = "https://github.com/EklipZgit/BeatSaberChromaGLS/releases/tag/";

        private static readonly Regex ReleaseTag = new(@"^v?(?:\d{4}-\d{2}-\d{2}-)?(?<version>\d+\.\d+\.\d+)$");
        private static readonly Regex GameArchiveName = new(@"^ChromaGLS-.+-bs(?<version>\d+\.\d+\.\d+)(?:-[0-9a-fA-F]+)?\.zip$");
        private static readonly Regex VersionPrefix = new(@"^(?<version>\d+\.\d+\.\d+)(?<prerelease>-[0-9A-Za-z.-]+)?(?:[+_].*)?$");

        private AvailablePluginUpdate(string installedVersion, string version, string releaseUrl)
        {
            InstalledVersion = installedVersion;
            Version = version;
            ReleaseUrl = releaseUrl;
        }

        public string InstalledVersion { get; }

        public string Version { get; }

        public string ReleaseUrl { get; }

        public static AvailablePluginUpdate? Evaluate(
            JObject release,
            string installedVersion,
            string gameVersion,
            bool debugBuild)
        {
            if (release.Value<bool>(DraftKey) || release.Value<bool>(PrereleaseKey))
                return null;

            string releaseUrl = release.Value<string>(ReleaseUrlKey) ?? string.Empty;
            Match tag = ReleaseTag.Match(release.Value<string>(TagNameKey) ?? string.Empty);
            Match installed = VersionPrefix.Match(installedVersion);
            Match game = VersionPrefix.Match(gameVersion);
            if (!tag.Success || !installed.Success || !game.Success ||
                !releaseUrl.StartsWith(ReleasePagePrefix, StringComparison.Ordinal))
            {
                return null;
            }

            if (!System.Version.TryParse(tag.Groups["version"].Value, out System.Version latestVersion) ||
                !System.Version.TryParse(installed.Groups["version"].Value, out System.Version currentVersion) ||
                !System.Version.TryParse(game.Groups["version"].Value, out System.Version currentGameVersion))
            {
                return null;
            }

            int comparison = latestVersion.CompareTo(currentVersion);
            if (!debugBuild && (comparison < 0 || (comparison == 0 && !installed.Groups["prerelease"].Success)))
                return null;

            System.Version? oldestGameVersion = null;
            if (release[AssetsKey] is JArray assets)
            {
                // Release ZIPs identify the game targets. Source archives and release prose do not establish compatibility.
                foreach (JToken asset in assets)
                {
                    Match archive = GameArchiveName.Match(asset.Value<string>(AssetNameKey) ?? string.Empty);
                    if (!archive.Success || !System.Version.TryParse(archive.Groups["version"].Value, out System.Version assetGameVersion))
                        continue;

                    if (oldestGameVersion == null || assetGameVersion < oldestGameVersion)
                        oldestGameVersion = assetGameVersion;
                }
            }

            if (oldestGameVersion == null || currentGameVersion < oldestGameVersion)
                return null;

            return new AvailablePluginUpdate(installed.Groups["version"].Value + installed.Groups["prerelease"].Value, tag.Groups["version"].Value, releaseUrl);
        }
    }
}
