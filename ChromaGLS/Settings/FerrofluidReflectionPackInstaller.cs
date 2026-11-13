using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ChromaGLS.Settings
{
    /// <summary>Verifies and installs the content-addressed reflection pack on disk. File IO only; no Unity API is referenced so tests can run on plain threads.</summary>
    internal sealed class FerrofluidReflectionPackInstaller
    {
        private const int BufferSize = 65536;
        private readonly FerrofluidReflectionPackManifest _manifest;
        private readonly string _installPath;
        private readonly HttpClient _client;
        private readonly string? _localSourcePath;

        internal FerrofluidReflectionPackInstaller(FerrofluidReflectionPackManifest manifest, string installDirectory, HttpClient client, string? localSourcePath = null)
        {
            _manifest = manifest;
            _installPath = Path.Combine(installDirectory, manifest.AssetName);
            _localSourcePath = localSourcePath;
            _client = client;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        internal bool UsesLocalSource => _localSourcePath != null;

        internal bool CanDownload => _localSourcePath != null || _manifest.CanDownload;

        internal string InstallPath => _installPath;

        /// <summary>Hashes the installed file once off the Unity main thread; null when absent or invalid.</summary>
        internal async Task<string?> GetInstalledPathAsync(CancellationToken cancel)
        {
            if (!File.Exists(_installPath))
                return null;
            return await Task.Run(() => Verify(_installPath) ? _installPath : null, cancel).ConfigureAwait(false);
        }

        internal Task<string?> DownloadAsync(IProgress<int>? progress, CancellationToken cancel)
        {
            if (!CanDownload)
                return Task.FromResult<string?>(null);
            if (_localSourcePath != null)
                return Task.Run(() => DownloadCoreAsync(progress, cancel), cancel);
            return DownloadCoreAsync(progress, cancel);
        }

        private async Task<string?> DownloadCoreAsync(IProgress<int>? progress, CancellationToken cancel)
        {
            if (!CanDownload)
                return null;
            Directory.CreateDirectory(Path.GetDirectoryName(_installPath)!);
            string tempPath = _installPath + ".part-" + Guid.NewGuid().ToString("N");
            int lastPercent = -1;
            try
            {
                using CancellationTokenSource body = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                body.CancelAfter(TimeSpan.FromMinutes(30));
                using HttpResponseMessage? response = _localSourcePath == null ? await _client.GetAsync(_manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, body.Token).ConfigureAwait(false) : null;
                if (response != null)
                {
                    response.EnsureSuccessStatusCode();
                    long? announced = response.Content.Headers.ContentLength;
                    if (announced.HasValue && announced.Value != _manifest.ByteLength)
                        return null;
                }

                using SHA256 hasher = SHA256.Create();
                using Stream source = _localSourcePath != null
                    ? new FileStream(_localSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true)
                    : await response!.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using FileStream target = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[BufferSize];
                long written = 0;
                while (true)
                {
                    int read = await source.ReadAsync(buffer, 0, buffer.Length, body.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    written += read;
                    if (written > _manifest.ByteLength)
                        return null;
                    hasher.TransformBlock(buffer, 0, read, null, 0);
                    await target.WriteAsync(buffer, 0, read, body.Token).ConfigureAwait(false);
                    int percent = (int)(written * 100 / _manifest.ByteLength);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent);
                    }
                }

                hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                if (written != _manifest.ByteLength || !string.Equals(ToHex(hasher.Hash!), _manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    return null;

                body.Token.ThrowIfCancellationRequested();
                target.Close();
                if (File.Exists(_installPath))
                    File.Replace(tempPath, _installPath, null);
                else
                    File.Move(tempPath, _installPath);
                return _installPath;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception)
                {
                    // Best-effort cleanup of our own unfinished temp file only.
                }
            }
        }

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);

        private bool Verify(string path)
        {
            try
            {
                FileInfo info = new(path);
                if (!info.Exists || info.Length != _manifest.ByteLength)
                    return false;
                using SHA256 hasher = SHA256.Create();
                using FileStream stream = File.OpenRead(path);
                byte[] hash = hasher.ComputeHash(stream);
                return string.Equals(ToHex(hash), _manifest.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
