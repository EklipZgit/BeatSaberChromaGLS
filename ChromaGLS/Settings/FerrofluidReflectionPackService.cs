using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IPA.Utilities;
using Zenject;

namespace ChromaGLS.Settings
{
    /// <summary>App-lifetime state for the optional ferrofluid reflection map pack: installed-path verification, consent-gated download, decline persistence.</summary>
    internal sealed class FerrofluidReflectionPackService : IInitializable, IDisposable
    {
        private const string DescriptorResource = "ChromaGLS.Settings.ferrofluid-reflections-pack.json";
        private readonly Config _config;
        private readonly Action<string?> _pathCallback;
        private readonly HttpClient _client;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly FerrofluidReflectionPackManifest? _manifest;
        private readonly FerrofluidReflectionPackInstaller? _installer;
        private Task? _initializeTask;
        private Task<bool>? _downloadTask;
        private CancellationTokenSource? _downloadCancel;
        private bool _offerConsumed;
        private ReflectionQuality _pendingQuality = ReflectionQuality.None;
        private bool _reflectionsRequested;
        private bool _disposed;
        private int _progress;

        [Inject]
        internal FerrofluidReflectionPackService(Config config)
            : this(config, null, null, null, null)
        {
#if FERROFLUID_REFLECTION_PACK_UNAVAILABLE
            State = PackState.Unavailable;
#endif
        }

        internal FerrofluidReflectionPackService(Config config, Action<string?>? pathCallback = null, HttpMessageHandler? handler = null, string? installDirectory = null, string? manifestJson = null)
        {
            _config = config;
            _pathCallback = pathCallback ?? FerrofluidRoomReflections.SetReflectionPackPath;
            string directory = installDirectory ?? Path.Combine(UnityGame.UserDataPath, "ChromaGLS", "Ferrofluid");
            _client = handler != null ? new HttpClient(handler) : new HttpClient();
            string? json = manifestJson;
            if (json == null)
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DescriptorResource);
                if (stream != null)
                {
                    using (stream)
                    using (StreamReader reader = new(stream))
                    {
                        json = reader.ReadToEnd();
                    }
                }
            }

            if (json == null || !FerrofluidReflectionPackManifest.TryParse(json, out _manifest))
            {
                _manifest = null;
                Plugin.Log?.Warn("[ChromaGLS Ferrofluid] reflection pack descriptor missing or malformed; optional reflections unavailable.");
            }

            string? localSource = null;
#if DEBUG
            // Debug builds can test consent and installation from a local file before the pack is hosted.
            try
            {
                string developmentSource = Path.Combine(directory, "reflection-pack-development-source.txt");
                if (File.Exists(developmentSource))
                {
                    string? first;
                    using (StreamReader reader = new(developmentSource))
                        first = reader.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(first))
                        localSource = first;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Plugin.Log?.Warn($"[ChromaGLS Ferrofluid] could not read reflection-pack-development-source.txt: {ex.Message}");
            }

            // Without an explicit override the Debug build tries its own assembly metadata so the
            // repository bundle needs zero game-config setup; tests that inject a fake descriptor
            // must not fall back to the real 300 MB pack.
            if (localSource == null && manifestJson == null)
            {
                foreach (object attribute in Assembly.GetExecutingAssembly()
                    .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                {
                    if (attribute is AssemblyMetadataAttribute metadata
                        && metadata.Key == "ChromaGLS.FerrofluidReflectionDevelopmentSource"
                        && !string.IsNullOrEmpty(metadata.Value)
                        && File.Exists(metadata.Value))
                    {
                        localSource = metadata.Value;
                    }
                }
            }
#endif
            if (_manifest != null)
                _installer = new FerrofluidReflectionPackInstaller(_manifest, directory, _client, localSource);

            State = _manifest == null ? PackState.Unavailable : PackState.Checking;
        }

        internal event Action? StatusChanged;

        internal event Action<ReflectionQuality>? QualityChanged;

        internal event Action? DownloadPromptRequested;

        internal enum PackState
        {
            Checking,
            Missing,
            Downloading,
            Ready,
            Failed,
            Unavailable,
        }

        internal PackState State { get; private set; }

        internal bool UsesLocalSource => _installer?.UsesLocalSource == true;

        internal bool CanDownload => _installer?.CanDownload == true;

        internal int Progress => _progress;

        internal string? LastError { get; private set; }

        internal FerrofluidReflectionPackManifest? Manifest => _manifest;

        public void Dispose()
        {
            _disposed = true;
            FerrofluidRoomReflections.ReflectionsRequested -= NotifyReflectionsRequested;
            _lifetime.Cancel();
            _downloadCancel?.Cancel();
            _lifetime.Dispose();
            _client.Dispose();
        }

        internal void NotifyReflectionsRequested()
        {
            _reflectionsRequested = true;
            Notify();
        }

        void IInitializable.Initialize()
        {
            FerrofluidRoomReflections.ReflectionsRequested += NotifyReflectionsRequested;
            if (FerrofluidRoomReflections.EnvironmentRequested)
                NotifyReflectionsRequested();
            InitializeAsync();
        }

        /// <summary>One-time startup check, cached; never downloads, even after a prior approval.</summary>
        internal Task InitializeAsync() => _initializeTask ??= RunInitializeAsync();

        /// <summary>One automatic offer per session: renderer actually requested, quality enabled, missing, not declined, downloadable.</summary>
        internal bool TryOfferDownload()
        {
            if (_offerConsumed
                || !_reflectionsRequested
                || _config.FerrofluidReflectionMapsDeclined
                || _config.ReflectionsQuality == ReflectionQuality.None
                || !CanDownload
                || State != PackState.Missing)
            {
                return false;
            }

            _offerConsumed = true;
            return true;
        }

        /// <summary>Explicit user consent; concurrent calls share the in-flight task. Ready returns immediately with no HTTP.</summary>
        internal Task<bool> DownloadAsync(IProgress<int>? progress = null, CancellationToken cancel = default)
        {
            if (_downloadTask is { IsCompleted: false })
                return _downloadTask;
            _downloadTask = RunDownloadFlowAsync(progress, cancel);
            return _downloadTask;
        }

        private async Task<bool> RunDownloadFlowAsync(IProgress<int>? progress, CancellationToken cancel)
        {
            await InitializeAsync();
            if (_manifest == null || _installer == null || State == PackState.Unavailable)
                return false;
            if (State == PackState.Ready)
                return true;
            _offerConsumed = true;
            _config.FerrofluidReflectionMapsDeclined = false;
            LastError = null;
            State = PackState.Downloading;
            _progress = 0;
            Notify();
            _downloadCancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancel);
            try
            {
                return await RunDownloadAsync(progress, _downloadCancel.Token);
            }
            finally
            {
                _downloadCancel.Dispose();
                _downloadCancel = null;
            }
        }

        internal void CancelDownload() => _downloadCancel?.Cancel();

        internal void Decline()
        {
            _config.FerrofluidReflectionMapsDeclined = true;
            _offerConsumed = true;
            _pendingQuality = ReflectionQuality.None;
            SetQuality(ReflectionQuality.None);
            Notify();
        }

        /// <summary>
        /// Manual quality selection from the UI. Every attempt to enable while the pack is
        /// missing re-opens consent regardless of a persisted decline; a successful download
        /// applies the chosen quality. None and failed installs always stay None.
        /// </summary>
        internal void RequestQuality(ReflectionQuality quality)
        {
            // Latest intent wins: a None request during a pending enable cancels it so a later
            // Ready or download completion cannot resurrect the superseded selection.
            _pendingQuality = quality;
            if (quality == ReflectionQuality.None)
            {
                SetQuality(ReflectionQuality.None);
                return;
            }

            if (State == PackState.Ready)
            {
                SetQuality(quality);
                _pendingQuality = ReflectionQuality.None;
                return;
            }

            // Missing/Failed/Unavailable: keep None until a pack actually lands. Checking waits
            // for the in-flight initialize; its completion either applies the pending choice or
            // fires the prompt. Every other attempt re-prompts, even when nothing can download,
            // so the user always gets an explanation.
            SetQuality(ReflectionQuality.None);
            bool wasChecking = State == PackState.Checking;
            InitializeAsync();

            // Init completion owns the prompt for a Checking request; firing here too would
            // double-prompt when the cache check resolves synchronously.
            if (!wasChecking)
                DownloadPromptRequested?.Invoke();
        }

        private void SetQuality(ReflectionQuality quality)
        {
            // Any explicit request is a settled user decision even when the value is unchanged.
            _config.ReflectionsQualityInitialized = true;
            if (_config.ReflectionsQuality == quality)
                return;
            _config.ReflectionsQuality = quality;
            QualityChanged?.Invoke(quality);
        }

        private async Task RunInitializeAsync()
        {
            if (_installer == null || State == PackState.Unavailable)
                return;
            try
            {
                string? path = await _installer.GetInstalledPathAsync(_lifetime.Token);
                if (_disposed || _lifetime.IsCancellationRequested)
                    return;
                if (path != null)
                {
                    State = PackState.Ready;
                    _pathCallback(path);
                    if (_pendingQuality != ReflectionQuality.None)
                    {
                        SetQuality(_pendingQuality);
                        _pendingQuality = ReflectionQuality.None;
                    }
                }
                else if (State == PackState.Checking)
                {
                    State = PackState.Missing;
                    if (_config.FerrofluidReflectionMapsDeclined)
                        SetQuality(ReflectionQuality.None);
                }
            }
            catch (OperationCanceledException)
            {
            }

            Notify();
            if (State == PackState.Missing && _pendingQuality != ReflectionQuality.None && !_disposed)
                DownloadPromptRequested?.Invoke();
        }

        private async Task<bool> RunDownloadAsync(IProgress<int>? progress, CancellationToken cancel)
        {
            // The service always surfaces progress so UI and tests see updates even without a caller IProgress.
            IProgress<int> wrapped = new Progress<int>(percent =>
            {
                if (_disposed)
                    return;
                _progress = percent;
                progress?.Report(percent);
                Notify();
            });
            try
            {
                string? path = await _installer!.DownloadAsync(wrapped, cancel);
                bool ready = path != null;
                State = ready ? PackState.Ready : PackState.Failed;
                if (!ready)
                {
                    LastError = "download did not match the expected content hash or length.";
                    _pendingQuality = ReflectionQuality.None;
                    SetQuality(ReflectionQuality.None);
                }

                if (ready && !_disposed)
                {
                    _pathCallback(path);
                    if (_pendingQuality != ReflectionQuality.None)
                    {
                        SetQuality(_pendingQuality);
                        _pendingQuality = ReflectionQuality.None;
                    }
                }

                Notify();
                return ready;
            }
            catch (OperationCanceledException)
            {
                State = PackState.Missing;
                _pendingQuality = ReflectionQuality.None;
                SetQuality(ReflectionQuality.None);
                Notify();
                return false;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                State = PackState.Failed;
                _pendingQuality = ReflectionQuality.None;
                SetQuality(ReflectionQuality.None);
                Plugin.Log?.Warn($"[ChromaGLS Ferrofluid] reflection pack download failed: {exception.Message}");
                Notify();
                return false;
            }
        }

        private void Notify()
        {
            if (!_disposed)
                StatusChanged?.Invoke();
        }
    }
}
