using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChromaGLS.Updates
{
    // check github for a newer release than our current version, and show the user a link
    internal sealed class StartupUpdateCheck
    {
        public const bool IsDebugBuild =
#if DEBUG
            true;
#else
            false;
#endif

        private const string LatestReleaseApiUrl = "https://api.github.com/repos/EklipZgit/BeatSaberChromaGLS/releases/latest";

        private readonly Lazy<Task<AvailablePluginUpdate?>> _check;

        public StartupUpdateCheck(
            Func<Task<string>> fetchRelease,
            string installedVersion,
            string gameVersion,
            bool debugBuild)
        {
            // The app container outlives menu scenes, so returning to the menu never retries a failed check or starts another request.
            _check = new Lazy<Task<AvailablePluginUpdate?>>(() => CheckAsync(fetchRelease, installedVersion, gameVersion, debugBuild));
        }

        public Task<AvailablePluginUpdate?> CheckOnceAsync() => _check.Value;

        internal static async Task<string> FetchLatestReleaseAsync()
        {
            using HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = 1024 * 1024,
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ChromaGLS-UpdateCheck");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return await client.GetStringAsync(LatestReleaseApiUrl).ConfigureAwait(false);
        }

        private static async Task<AvailablePluginUpdate?> CheckAsync(
            Func<Task<string>> fetchRelease,
            string installedVersion,
            string gameVersion,
            bool debugBuild)
        {
            try
            {
                string json = await fetchRelease().ConfigureAwait(false);
                return AvailablePluginUpdate.Evaluate(JObject.Parse(json), installedVersion, gameVersion, debugBuild);
            }
            catch (Exception exception)
            {
                // This optional startup request must not interrupt the menu when offline, rate limited, or given invalid release metadata.
                Plugin.Log?.Warn($"[ChromaGLS] Could not check for updates: {exception.Message}");
                return null;
            }
        }
    }
}
