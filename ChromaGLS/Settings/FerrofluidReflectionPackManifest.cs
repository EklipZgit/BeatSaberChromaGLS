using System;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

namespace ChromaGLS.Settings
{
    /// <summary>Content-addressed descriptor for the optional ferrofluid reflection map pack published separately from the core plugin.</summary>
    [UsedImplicitly]
    internal sealed class FerrofluidReflectionPackManifest
    {
        private const string SchemaVersionKey = "schemaVersion";
        private const string AssetNameKey = "assetName";
        private const string Sha256Key = "sha256";
        private const string ByteLengthKey = "byteLength";
        private const string DownloadUrlKey = "downloadUrl";

        private FerrofluidReflectionPackManifest(string assetName, string sha256, long byteLength, string downloadUrl)
        {
            AssetName = assetName;
            Sha256 = sha256;
            ByteLength = byteLength;
            DownloadUrl = downloadUrl;
        }

        internal string AssetName { get; }

        internal string Sha256 { get; }

        internal long ByteLength { get; }

        internal string DownloadUrl { get; }

        internal bool CanDownload => DownloadUrl.Length != 0;

        internal static bool TryParse(string json, out FerrofluidReflectionPackManifest? manifest)
        {
            manifest = null;
            try
            {
                JObject root = JObject.Parse(json);
                long byteLength = root.Value<long>(ByteLengthKey);
                if (root.Value<int>(SchemaVersionKey) != 1
                    || byteLength <= 0
                    || root.Value<string>(Sha256Key) is not { Length: 64 } sha256
                    || !IsHex(sha256)
                    || root.Value<string>(DownloadUrlKey) is not { } downloadUrl
                    || (downloadUrl.Length != 0 && (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps))
                    || root.Value<string>(AssetNameKey) is not { } assetName
                    || assetName != "ChromaGLS-Ferrofluid-Reflections-" + sha256 + ".bundle")
                {
                    return false;
                }

                manifest = new FerrofluidReflectionPackManifest(assetName, sha256, byteLength, downloadUrl);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsHex(string value)
        {
            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                    return false;
            }

            return true;
        }
    }
}
