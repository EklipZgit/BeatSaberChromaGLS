using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using Zenject;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ChromaGLS.Tests
{
    // Exercises the optional reflection-pack manifest/installer/service seams without Unity
    // objects or real HTTP: injected manifest JSON, temp directory, fake handler, path callback.
    [TestFixture]
    [NonParallelizable]
    internal sealed class FerrofluidReflectionPackTests
    {
        private const string PayloadHash = "054edec1d0211f624fed0cbca9d4f9400b0e491c43742af2c5b0abebf0c990d8";
        private static readonly Assembly Plugin = Assembly.Load("ChromaGLS");
        private static readonly Type Service = Plugin.GetType("ChromaGLS.Settings.FerrofluidReflectionPackService", true)!;
        private static readonly Type State = Service.GetNestedType("PackState", BindingFlags.NonPublic)!;
        private static readonly Type ConfigType = Plugin.GetType("ChromaGLS.Settings.Config", true)!;
        private static readonly PropertyInfo StateProp = Service.GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        private static readonly PropertyInfo DeclinedProp = ConfigType.GetProperty("FerrofluidReflectionMapsDeclined")!;
        private static readonly Type QualityEnum = Plugin.GetType("ChromaGLS.Settings.ReflectionQuality", true)!;
        private static readonly Type OwnerType = Plugin.GetType("ChromaGLS.Settings.ParabolicFloorLight", true)!;

        private static string ManifestJson(string url = "https://example.invalid/pack") =>
            $"{{\"schemaVersion\":1,\"assetName\":\"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle\",\"sha256\":\"{PayloadHash}\",\"byteLength\":4,\"downloadUrl\":\"{url}\"}}";

        [Test]
        public void DebugBuildUsesAssemblyMetadataLocalSourceWhenNoDescriptorInjected()
        {
#if !DEBUG
            Assert.Inconclusive("assembly metadata local source is Debug-only");
#else
            object config = Activator.CreateInstance(ConfigType, true)!;
            object service = CreateServiceNullableManifest(config, _ => { }, new StubHandler(), TempDir());
            PropertyInfo usesLocal = Service.GetProperty("UsesLocalSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
            PropertyInfo canDownload = Service.GetProperty("CanDownload", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.IsTrue((bool)usesLocal.GetValue(service)!, "Debug builds must find the embedded developer source without config setup");
            Assert.IsTrue((bool)canDownload.GetValue(service)!, "a local developer pack must allow the consent flow");
#endif
        }

        [Test]
        public void ReleaseBuildIgnoresAssemblyMetadataLocalSource()
        {
#if DEBUG
            Assert.Inconclusive("Release ignores developer metadata");
#else
            object config = Activator.CreateInstance(ConfigType, true)!;
            object service = CreateServiceNullableManifest(config, _ => { }, new StubHandler(), TempDir());
            PropertyInfo usesLocal = Service.GetProperty("UsesLocalSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.IsFalse((bool)usesLocal.GetValue(service)!, "Release must never read the Debug-only developer path");
#endif
        }

        private static object CreateServiceNullableManifest(object config, Action<string?> callback, HttpMessageHandler handler, string dir)
        {
            ConstructorInfo ctor = Service.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Single(c => c.GetParameters().Length == 5);
            return ctor.Invoke(new object?[] { config, callback, handler, dir, null });
        }

        private static object CreateService(object config, Action<string?> callback, HttpMessageHandler handler, string dir, string json)
        {
            ConstructorInfo? ctor = Service.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { ConfigType, typeof(Action<string?>), typeof(HttpMessageHandler), typeof(string), typeof(string) }, null);
            object service = (ctor ?? throw new MissingMethodException("Service test ctor not found.")).Invoke(new object?[] { config, callback, handler, dir, json });
            if (service is IDisposable disposable)
                Services.Add(disposable);
            return service;
        }

        private static readonly List<string> Dirs = new();
        private static readonly List<IDisposable> Services = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (IDisposable service in Services)
                service.Dispose();
            Services.Clear();
            foreach (string dir in Dirs)
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }

            Dirs.Clear();
        }

        private static string TempDir()
        {
            string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "p-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            Dirs.Add(dir);
            return dir;
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            internal int Calls;
            internal byte[]? Body;
            internal HttpContent? Content;
            internal HttpStatusCode Code = HttpStatusCode.OK;
            internal readonly TaskCompletionSource<HttpResponseMessage> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal void Release(byte[]? body = null, HttpStatusCode code = HttpStatusCode.OK)
            {
                Body = body ?? Body;
                Gate.SetResult(new HttpResponseMessage(code) { Content = Body != null ? new ByteArrayContent(Body) : new StringContent("") });
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                if (Content != null)
                    return Task.FromResult(new HttpResponseMessage(Code) { Content = Content });
                if (Body != null && !Gate.Task.IsCompleted)
                    return Task.FromResult(new HttpResponseMessage(Code) { Content = new ByteArrayContent(Body) });
                return Gate.Task;
            }
        }

        private sealed class UnseekableBody : Stream
        {
            private readonly MemoryStream _data;
            private readonly bool _blockAfterFirstRead;
            private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _reads;

            internal UnseekableBody(byte[] bytes, bool blockAfterFirstRead = false)
            {
                _data = new MemoryStream(bytes);
                _blockAfterFirstRead = blockAfterFirstRead;
            }

            internal TaskCompletionSource<bool> ReadBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_blockAfterFirstRead && _reads++ > 0)
                {
                    ReadBlocked.TrySetResult(true);
                    using (cancellationToken.Register(() => _release.TrySetCanceled()))
                        await _release.Task.ConfigureAwait(false);
                }

                return _data.Read(buffer, offset, _blockAfterFirstRead ? Math.Min(count, 2) : count);
            }

            public override int Read(byte[] buffer, int offset, int count) => _data.Read(buffer, offset, count);
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _release.TrySetCanceled();
                    _data.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        [Test]
        public void InitializeWithAbsentFileMakesNoHttp()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            string? path = "sentinel";
            object service = CreateService(config, value => path = value, new StubHandler(), TempDir(), ManifestJson());
            Service.GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
            Task task = (Task)Service.GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!;
            task.Wait();
            Assert.AreEqual("Missing", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.AreEqual("sentinel", path, "No path must be assigned while the pack is absent.");
        }

        [Test]
        public void ValidPayloadDownloadsAtomicallyAndCallsBackAfterVerify()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            StubHandler handler = new() { Body = new byte[] { 0, 1, 2, 3 } };
            string? path = null;
            string dir = TempDir();
            object service = CreateService(config, value => path = value, handler, dir, ManifestJson());
            Task<bool> task = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, CancellationToken.None })!;
            task.Wait();
            Assert.IsTrue(task.Result, "verified payload must install: " + Service.GetProperty("LastError", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(service));
            Assert.IsNotNull(path);
            Assert.IsTrue(File.Exists(path!), "hash-named file must exist after atomic move");
            Assert.AreEqual(1, handler.Calls);
            Assert.AreEqual("Ready", Enum.GetName(State, StateProp.GetValue(service)!));
            Directory.Delete(dir, true);
        }

        [Test]
        public void ReadyCacheLoadsWithoutHttp()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            string dir = TempDir();
            string target = Path.Combine(dir, $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle");
            File.WriteAllBytes(target, new byte[] { 0, 1, 2, 3 });
            string? path = null;
            StubHandler handler = new();
            object service = CreateService(config, value => path = value, handler, dir, ManifestJson());
            ((Task)Service.GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!).Wait();
            Assert.AreEqual(0, handler.Calls, "existing verified cache must never touch HTTP");
            Assert.AreEqual(target, path);
            Assert.AreEqual("Ready", Enum.GetName(State, StateProp.GetValue(service)!));
            Directory.Delete(dir, true);
        }

        [Test]
        public void WrongHashBodyNeverInstallsAndCleansPart()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            StubHandler handler = new() { Body = new byte[] { 9, 9, 9, 9 } };
            string? path = null;
            string dir = TempDir();
            object service = CreateService(config, value => path = value, handler, dir, ManifestJson());
            Task<bool> task = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, CancellationToken.None })!;
            task.Wait();
            Assert.IsFalse(task.Result);
            Assert.IsNull(path);
            Assert.AreEqual("Failed", Enum.GetName(State, StateProp.GetValue(service)!));
            CollectionAssert.IsEmpty(Directory.GetFiles(dir), "unfinished temp file must be removed");
            Directory.Delete(dir, true);
        }

        [Test]
        public void ConcurrentDownloadsShareOneHttpCall()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            StubHandler handler = new();
            string dir = TempDir();
            object service = CreateService(config, _ => { }, handler, dir, ManifestJson());
            MethodInfo download = Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Task<bool> first = (Task<bool>)download.Invoke(service, new object?[] { null, CancellationToken.None })!;
            Assert.IsFalse(first.IsCompleted, "first download must be pending behind the handler gate");
            Task<bool> second = (Task<bool>)download.Invoke(service, new object?[] { null, CancellationToken.None })!;
            Assert.AreSame(first, second, "concurrent calls must share one HTTP download");
            handler.Release(new byte[] { 0, 1, 2, 3 });
            Task.WaitAll(first, second);
            Assert.AreEqual(1, handler.Calls);
        }

        [Test]
        public void ManualRetryAfterDeclineRunsExactlyOneDownload()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            DeclinedProp.SetValue(config, true);
            StubHandler handler = new() { Body = new byte[] { 0, 1, 2, 3 } };
            string dir = TempDir();
            object service = CreateService(config, _ => { }, handler, dir, ManifestJson());
            ((Task)Service.GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!).Wait();
            Assert.IsFalse((bool)Service.GetMethod("TryOfferDownload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!);
            Service.GetMethod("NotifyReflectionsRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
            Assert.IsFalse((bool)Service.GetMethod("TryOfferDownload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!, "declined user gets no auto-offer");
            Task<bool> task = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, CancellationToken.None })!;
            task.Wait();
            Assert.IsTrue(task.Result);
            Assert.AreEqual(1, handler.Calls);
            Assert.IsFalse((bool)DeclinedProp.GetValue(config)!, "manual consent clears the persisted decline");
            Directory.Delete(dir, true);
        }

        [Test]
        public void DownloadWithoutInitializeUsesExistingCacheWithoutHttp()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            string dir = TempDir();
            string target = Path.Combine(dir, $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle");
            File.WriteAllBytes(target, new byte[] { 0, 1, 2, 3 });
            StubHandler handler = new();
            string? path = null;
            object service = CreateService(config, value => path = value, handler, dir, ManifestJson());
            Task<bool> task = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, CancellationToken.None })!;
            task.Wait();
            Assert.IsTrue(task.Result);
            Assert.AreEqual(0, handler.Calls, "valid preexisting cache must satisfy download without HTTP");
            Assert.AreEqual(target, path);
            Directory.Delete(dir, true);
        }

        [Test]
        public void AppInitializationLinksRequestToOffer()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            string dir = TempDir();
            object service = CreateService(config, _ => { }, new StubHandler(), dir, ManifestJson());
            Type initializable = typeof(Zenject.IInitializable);
            initializable.GetMethod("Initialize")!.Invoke(service, null);
            Task task = (Task)Service.GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!;
            task.Wait();
            Assert.AreEqual("Missing", Enum.GetName(State, StateProp.GetValue(service)!));
            Type room = Plugin.GetType("ChromaGLS.Settings.FerrofluidRoomReflections", true)!;
            FieldInfo requested = room.GetField("_environmentRequested", BindingFlags.Static | BindingFlags.NonPublic)!;
            object previousRequested = requested.GetValue(null)!;
            try
            {
                room.GetMethod("RequestEnvironment", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, null);
                Assert.IsTrue(Offer(service), "actual renderer request must make the offer eligible once");
            }
            finally
            {
                requested.SetValue(null, previousRequested);
            }

            Directory.Delete(dir, true);
        }

        [Test]
        public void DownloadingStateMessageShowsInitialProgress()
        {
            Type ui = Plugin.GetType("ChromaGLS.Settings.FerrofluidReflectionDownloadUI", true)!;
            Type stateType = Service.GetNestedType("PackState", BindingFlags.NonPublic)!;
            Type manifestType = Plugin.GetType("ChromaGLS.Settings.FerrofluidReflectionPackManifest", true)!;
            ConstructorInfo manifestCtor = manifestType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string), typeof(long), typeof(string) }, null)!;
            object manifest = manifestCtor.Invoke(new object[] { $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle", PayloadHash, 4L, "https://example.invalid/pack" });
            object downloading = Enum.Parse(stateType, "Downloading");
            string message = (string)ui.InvokeMember("DescribeState", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.OptionalParamBinding, null, null, new object?[] { downloading, manifest, null })!;
            StringAssert.Contains("0%", message, "Downloading state must surface the initial progress percent to the user.");
        }

        [Test]
        public void CompiledManifestDeclaresAssetBundleLoadingTools()
        {
            using Stream? stream = Plugin.GetManifestResourceStream("ChromaGLS.manifest") ?? Plugin.GetManifestResourceStream("ChromaGLS.manifest.json");
            Assert.IsNotNull(stream, "compiled plugin manifest resource must exist");
            using StreamReader reader = new(stream!);
            Newtonsoft.Json.Linq.JObject manifest = Newtonsoft.Json.Linq.JObject.Parse(reader.ReadToEnd());
            Newtonsoft.Json.Linq.JObject? dependency = manifest["dependsOn"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(dependency, "manifest must declare dependencies");
            string? game = (string?)manifest["gameVersion"];
            string? expected = game switch
            {
                "1.29.1" or "1.44.2" => null,
                "1.34.2" => "^1.1.3",
                "1.37.1" => "^1.1.6",
                "1.40.8" => "^1.1.10",
                _ => "^1.1.13",
            };
            Assert.AreEqual(expected, (string?)dependency!["AssetBundleLoadingTools"], $"{game} must declare the matrix-approved AssetBundleLoadingTools dependency");
        }

        [Test]
        public void HttpNotFoundLeavesNothingInstalled()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            StubHandler handler = new();
            string? path = null;
            string dir = TempDir();
            object service = CreateService(config, value => path = value, handler, dir, ManifestJson());
            handler.Release(code: HttpStatusCode.NotFound);
            Task<bool> task = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, CancellationToken.None })!;
            task.Wait();
            Assert.IsFalse(task.Result);
            Assert.IsNull(path);
            CollectionAssert.IsEmpty(Directory.GetFiles(dir));
        }

        [Test]
        public void EmptyUrlManifestKeepsCacheManualOnly()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            string dir = TempDir();
            File.WriteAllBytes(Path.Combine(dir, $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle"), new byte[] { 0, 1, 2, 3 });
            StubHandler handler = new();
            string? path = null;
            object service = CreateService(config, value => path = value, handler, dir, ManifestJson(url: ""));
            ((Task)Service.GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!).Wait();
            Assert.AreEqual(0, handler.Calls);
            Assert.IsNotNull(path, "local file still installs without a URL");
            Service.GetMethod("NotifyReflectionsRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
            Assert.IsFalse((bool)Service.GetMethod("TryOfferDownload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!);
        }

        [Test]
        public void MalformedDescriptorLeavesServiceUnavailable()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            StubHandler handler = new();
            object service = CreateService(config, _ => { }, handler, TempDir(), "{not json");
            Assert.AreEqual("Unavailable", Enum.GetName(State, StateProp.GetValue(service)!));
            Task<bool> task = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, CancellationToken.None })!;
            task.Wait();
            Assert.IsFalse(task.Result);
            Assert.AreEqual(0, handler.Calls);
        }

        [TestCase(3)]
        [TestCase(5)]
        public void MismatchedAnnouncedLengthNeverInstalls(int length)
        {
            string dir = TempDir();
            string? path = null;
            StubHandler handler = new() { Body = new byte[length] };
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, value => path = value, handler, dir, ManifestJson());
            Assert.IsFalse(Download(service).GetAwaiter().GetResult());
            Assert.IsNull(path);
            CollectionAssert.IsEmpty(Directory.GetFiles(dir));
        }

        [TestCase(3)]
        [TestCase(5)]
        public void UnknownLengthBodyMustStillMatchExactLength(int length)
        {
            string dir = TempDir();
            string? path = null;
            using UnseekableBody body = new(new byte[length]);
            using StreamContent content = new(body);
            Assert.IsNull(content.Headers.ContentLength, "exercise the streaming length check without an announced length");
            StubHandler handler = new() { Content = content };
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, value => path = value, handler, dir, ManifestJson());
            Assert.IsFalse(Download(service).GetAwaiter().GetResult());
            Assert.IsNull(path);
            CollectionAssert.IsEmpty(Directory.GetFiles(dir));
        }

        [Test]
        public void MidstreamCancellationRemovesOnlyItsOwnPartialFile()
        {
            string dir = TempDir();
            string unrelated = Path.Combine(dir, "keep.txt");
            File.WriteAllText(unrelated, "keep");
            string? path = null;
            using UnseekableBody body = new(new byte[] { 0, 1, 2, 3 }, blockAfterFirstRead: true);
            using StreamContent content = new(body);
            using CancellationTokenSource cancel = new();
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, value => path = value, new StubHandler { Content = content }, dir, ManifestJson());
            Task<bool> download = Download(service, cancel.Token);
            try
            {
                Assert.IsTrue(body.ReadBlocked.Task.Wait(TimeSpan.FromSeconds(5)), "the body must reach its second read after writing the first chunk");
                Assert.IsFalse(download.IsCompleted);
                cancel.Cancel();
                Assert.IsTrue(((Task)download).Wait(TimeSpan.FromSeconds(5)), "cancellation must finish the transfer");
                Assert.IsFalse(download.Result);
                Assert.IsNull(path);
                Assert.AreEqual("Missing", Enum.GetName(State, StateProp.GetValue(service)!));
                CollectionAssert.AreEquivalent(new[] { unrelated }, Directory.GetFiles(dir));
                Assert.AreEqual("keep", File.ReadAllText(unrelated));
            }
            finally
            {
                cancel.Cancel();
            }
        }

        [Test]
        public void AutomaticOfferRequiresRequestAndQualityAndIsConsumedOnce()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            PropertyInfo quality = ConfigType.GetProperty("ReflectionsQuality")!;
            quality.SetValue(config, Enum.Parse(quality.PropertyType, "None"));
            object service = CreateService(config, _ => { }, new StubHandler(), TempDir(), ManifestJson());
            Initialize(service);
            Assert.IsFalse(Offer(service), "startup alone is not a reflection request");
            NotifyRequest(service);
            Assert.IsFalse(Offer(service), "quality None must never offer a download");
            quality.SetValue(config, Enum.Parse(quality.PropertyType, "Low"));
            Assert.IsTrue(Offer(service));
            Assert.IsFalse(Offer(service), "an automatic offer is consumed once per service lifetime");
        }

        [Test]
        public void DeclineAppliesNoneAndMarksInitialized()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            PropertyInfo quality = ConfigType.GetProperty("ReflectionsQuality")!;
            PropertyInfo initialized = ConfigType.GetProperty("ReflectionsQualityInitialized")!;
            quality.SetValue(config, Enum.Parse(quality.PropertyType, "High"));
            object service = CreateService(config, _ => { }, new StubHandler(), TempDir(), ManifestJson());
            Initialize(service);
            Service.GetMethod("Decline", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
            Assert.AreEqual("None", quality.GetValue(config)!.ToString(), "declining the pack must leave reflections None, not the prior quality");
            Assert.IsTrue((bool)initialized.GetValue(config)!, "declining is an explicit user decision, not an unset preference");
        }

        private static void RequestQuality(object service, string quality)
        {
            object parsed = Enum.Parse(QualityEnum, quality);
            Service.GetMethod("RequestQuality", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new[] { parsed });
        }

        private static int HookPrompts(object service)
        {
            int count = 0;
            EventInfo prompt = Service.GetEvent("DownloadPromptRequested", BindingFlags.Instance | BindingFlags.NonPublic)!;
            prompt.GetAddMethod(true)!.Invoke(service, new object[] { (Action)(() => count++) });
            _promptCounters.Add(service, () => count);
            return count;
        }

        private static readonly ConditionalWeakTable<object, Func<int>> _promptCounters = new();
        private static int PromptCount(object service) => _promptCounters.TryGetValue(service, out Func<int>? count) ? count() : 0;

        private static PropertyInfo QualityProp => ConfigType.GetProperty("ReflectionsQuality")!;

        [Test]
        public void ManualRequestReopensPromptOnEveryAttempt()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            object service = CreateService(config, _ => { }, new StubHandler(), TempDir(), ManifestJson());
            Initialize(service);
            HookPrompts(service);
            RequestQuality(service, "Low");
            Assert.AreEqual(1, PromptCount(service), "first Low attempt must open consent");
            Assert.AreEqual("None", QualityProp.GetValue(serviceConfig(service))!.ToString());
            Service.GetMethod("Decline", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
            RequestQuality(service, "High");
            Assert.AreEqual(2, PromptCount(service), "a second attempt after decline re-prompts");
            RequestQuality(service, "Low");
            Assert.AreEqual(3, PromptCount(service), "every manual attempt re-prompts while missing");
            RequestQuality(service, "None");
            Assert.AreEqual(3, PromptCount(service), "None never prompts");
        }

        private static object serviceConfig(object service) => Service.GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;

        [TestCase("Low")]
        [TestCase("High")]
        [TestCase("Ultra")]
        public void RequestedQualityAppliesOnlyAfterSuccessfulDownload(string choice)
        {
            string dir = TempDir();
            byte[] body = new byte[] { 0, 1, 2, 3 };
            object config = Activator.CreateInstance(ConfigType, true)!;
            object service = CreateService(config, _ => { }, new StubHandler { Body = body }, dir, ManifestJson());
            Initialize(service);
            HookPrompts(service);
            RequestQuality(service, choice);
            Task<bool> download = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, CancellationToken.None })!;
            Assert.IsTrue(download.Result);
            Assert.AreEqual(choice, QualityProp.GetValue(config)!.ToString(), "the requested tier applies once the pack actually lands");
        }

        [Test]
        public void RequestNoneDuringDownloadLeavesNoneAfterSuccess()
        {
            string dir = TempDir();
            byte[] body = new byte[] { 0, 1, 2, 3 };
            object config = Activator.CreateInstance(ConfigType, true)!;
            object service = CreateService(config, _ => { }, new StubHandler { Body = body }, dir, ManifestJson());
            Initialize(service);
            RequestQuality(service, "High");
            RequestQuality(service, "None");
            Task<bool> download = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, CancellationToken.None })!;
            Assert.IsTrue(download.Result);
            Assert.AreEqual("None", QualityProp.GetValue(config)!.ToString(), "None during a pending enable must win over the superseded request");
        }

        [Test]
        public void ReadyCacheAppliesRequestedQualityWithoutPrompt()
        {
            string dir = TempDir();
            object config = Activator.CreateInstance(ConfigType, true)!;
            object first = CreateService(config, _ => { }, new StubHandler { Body = new byte[] { 0, 1, 2, 3 } }, dir, ManifestJson());
            Initialize(first);
            Task<bool> install = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(first, new object?[] { null, CancellationToken.None })!;
            Assert.IsTrue(install.Result, "precondition: the pack must actually be installed");
            StubHandler handler = new();
            object second = CreateService(config, _ => { }, handler, dir, ManifestJson());
            Initialize(second);
            HookPrompts(second);
            RequestQuality(second, "High");
            Assert.AreEqual(0, PromptCount(second), "an installed pack must not ask for consent again");
            Assert.AreEqual("High", QualityProp.GetValue(config)!.ToString());
        }

        [Test]
        public void MenuContainerResolvesTheSameSingleService()
        {
            DiContainer app = new();
            object config = Activator.CreateInstance(ConfigType, true)!;
            object service = CreateService(config, _ => { }, new StubHandler(), TempDir(), ManifestJson());
            app.Bind(ConfigType).FromInstance(config);
            app.Bind(Service).FromInstance(service);
            DiContainer menu = new(app);
            Assert.AreSame(service, menu.Resolve(Service), "the menu container must share the app-level single service");
            object owner = Activator.CreateInstance(OwnerType, BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { config }, null)!;
            menu.Inject(owner);
            object bound = OwnerType.GetField("_packService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
            Assert.AreSame(service, bound, "the owner's inject must receive the app-level service, not a menu-scoped duplicate");
            FieldInfo eventField = Service.GetField("QualityChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Delegate? handler = eventField.GetValue(service) as Delegate;
            Assert.NotNull(handler, "the owner must subscribe to QualityChanged during injection");
            Assert.IsTrue(handler!.GetInvocationList().Any(d => d.Target == owner), "the subscribed handler must target the owner");
        }

        [Test]
        public void RequestBeforeInitializePromptsExactlyOnce()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            object service = CreateService(config, _ => { }, new StubHandler(), TempDir(), ManifestJson());
            HookPrompts(service);
            // Empty cache: initialize resolves synchronously to Missing before this returns.
            RequestQuality(service, "High");
            Assert.AreEqual(1, PromptCount(service), "one manual attempt must produce exactly one prompt");
            Assert.AreEqual("None", QualityProp.GetValue(config)!.ToString());
            RequestQuality(service, "Low");
            Assert.AreEqual(2, PromptCount(service), "a second explicit attempt re-prompts once more");
        }

        [Test]
        public void RequestQualityBeforeInitializeWaitsForCache()
        {
            string dir = TempDir();
            object config = Activator.CreateInstance(ConfigType, true)!;
            object first = CreateService(config, _ => { }, new StubHandler { Body = new byte[] { 0, 1, 2, 3 } }, dir, ManifestJson());
            Initialize(first);
            Task<bool> install = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(first, new object?[] { null, CancellationToken.None })!;
            Assert.IsTrue(install.Result);
            object second = CreateService(config, _ => { }, new StubHandler(), dir, ManifestJson());
            HookPrompts(second);
            // Request arrives while State is still Checking; the service must kick its own init.
            RequestQuality(second, "High");
            SpinWait.SpinUntil(() => StateProp.GetValue(second)!.ToString() == "Ready", TimeSpan.FromSeconds(5));
            Assert.AreEqual("Ready", StateProp.GetValue(second)!.ToString());
            Assert.AreEqual("High", QualityProp.GetValue(config)!.ToString());
            Assert.AreEqual(0, PromptCount(second), "a cached pack must apply without ever prompting");
        }

        [Test]
        public void StoredDeclineSuppressesANewSession()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            object first = CreateService(config, _ => { }, new StubHandler(), TempDir(), ManifestJson());
            Initialize(first);
            Service.GetMethod("Decline", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(first, null);
            Assert.IsTrue((bool)DeclinedProp.GetValue(config)!);
            object restoredConfig = Activator.CreateInstance(ConfigType, true)!;
            DeclinedProp.SetValue(restoredConfig, DeclinedProp.GetValue(config));
            StubHandler handler = new();
            object next = CreateService(restoredConfig, _ => { }, handler, TempDir(), ManifestJson());
            Initialize(next);
            NotifyRequest(next);
            Assert.IsFalse(Offer(next));
            Assert.AreEqual(0, handler.Calls);
        }

        [Test]
        public void NewPackHashRequiresFreshConsentInsteadOfReusingApproval()
        {
            string dir = TempDir();
            object config = Activator.CreateInstance(ConfigType, true)!;
            object first = CreateService(config, _ => { }, new StubHandler { Body = new byte[] { 0, 1, 2, 3 } }, dir, ManifestJson());
            Assert.IsTrue(Download(first).GetAwaiter().GetResult());
            StubHandler handler = new();
            string newManifest = ManifestJson().Replace(PayloadHash, new string('a', 64));
            object next = CreateService(config, _ => { }, handler, dir, newManifest);
            Initialize(next);
            Assert.AreEqual("Missing", Enum.GetName(State, StateProp.GetValue(next)!));
            Assert.AreEqual(0, handler.Calls, "prior approval must not automatically fetch a different content hash");
            NotifyRequest(next);
            Assert.IsTrue(Offer(next), "a changed content hash needs a new consent offer");
        }

        [Test]
        public void DownloadingStateMessageShowsCurrentProgress()
        {
            Type ui = Plugin.GetType("ChromaGLS.Settings.FerrofluidReflectionDownloadUI", true)!;
            object downloading = Enum.Parse(State, "Downloading");
            string message = (string)ui.InvokeMember("DescribeState", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.OptionalParamBinding, null, null, new object?[] { downloading, null, null, 37 })!;
            StringAssert.Contains("37%", message);
        }

        private static bool IsDebugSut() =>
            (Plugin.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false).Cast<System.Diagnostics.DebuggableAttribute>().FirstOrDefault()?.IsJITOptimizerDisabled) == true;

        [Test]
        public void LocalDevelopmentSourceInstallsWithoutHttp()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            string dir = TempDir();
            string src = Path.Combine(dir, "local-source.bin");
            File.WriteAllBytes(src, new byte[] { 0, 1, 2, 3 });
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), src);
            object config = Activator.CreateInstance(ConfigType, true)!;
            StubHandler handler = new();
            string? path = null;
            object service = CreateService(config, value => path = value, handler, dir, ManifestJson(""));
            Initialize(service);
            NotifyRequest(service);
            Assert.IsTrue(Offer(service), "development local source makes the empty-URL pack offerable");
            TaskCompletionSource<int> percentSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> task = (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { new Progress<int>(percent => percentSeen.TrySetResult(percent)), default(CancellationToken) })!;
            task.Wait();
            Assert.IsTrue(task.Result, "verified local source must install");
            Assert.AreEqual(0, handler.Calls, "local development source must not issue HTTP");
            string expected = Path.Combine(dir, $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle");
            Assert.AreEqual(expected, path, "path callback must receive the verified install");
            Assert.IsTrue(File.Exists(expected));
            Assert.AreEqual(new byte[] { 0, 1, 2, 3 }, File.ReadAllBytes(src), "source file must remain intact");
            Assert.IsTrue(percentSeen.Task.Wait(TimeSpan.FromSeconds(5)), "local source must report progress");
            Assert.AreEqual(100, percentSeen.Task.Result, "the verified four-byte copy reports a single 100% update");
        }

        [Test]
        public void LocalDevelopmentSourceWrongHashFailsCleanly()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            string dir = TempDir();
            string src = Path.Combine(dir, "local-source.bin");
            File.WriteAllBytes(src, new byte[] { 9, 9, 9, 9 });
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), src);
            StubHandler handler = new();
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, _ => { }, handler, dir, ManifestJson(""));
            Initialize(service);
            NotifyRequest(service);
            Assert.IsTrue(Offer(service));
            Task<bool> task = Download(service);
            task.Wait();
            Assert.IsFalse(task.Result, "wrong-hash local source must not install");
            Assert.AreEqual("Failed", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.AreEqual(0, handler.Calls);
            Assert.AreEqual(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(src), "source file must not be mutated or deleted");
        }

        [Test]
        public void MissingLocalSourceFailsWithoutHttpFallback()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            string dir = TempDir();
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), Path.Combine(dir, "absent.bin"));
            StubHandler handler = new() { Body = new byte[] { 0, 1, 2, 3 } };
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, _ => { }, handler, dir, ManifestJson());
            Initialize(service);
            Task<bool> task = Download(service);
            task.Wait();
            Assert.IsFalse(task.Result, "absent local source must fail rather than silently fall back to HTTP");
            Assert.AreEqual(0, handler.Calls, "local override must never fall back to HTTP");
        }

        [Test]
        public void LocalDevelopmentSourceShortBodyFailsCleanly()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            string dir = TempDir();
            string src = Path.Combine(dir, "local-source.bin");
            File.WriteAllBytes(src, new byte[] { 0, 1, 2 });
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), src);
            StubHandler handler = new();
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, _ => { }, handler, dir, ManifestJson(""));
            Initialize(service);
            Task<bool> task = Download(service);
            task.Wait();
            Assert.IsFalse(task.Result, "a short local source must not install");
            Assert.AreEqual("Failed", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.AreEqual(0, handler.Calls);
            Assert.AreEqual(new byte[] { 0, 1, 2 }, File.ReadAllBytes(src), "source file must remain intact");
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.part-*").Length, "no owned partial file may remain");
        }

        [Test]
        public void LocalDevelopmentSourceOversizeFailsCleanly()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            string dir = TempDir();
            string src = Path.Combine(dir, "local-source.bin");
            File.WriteAllBytes(src, new byte[] { 0, 1, 2, 3, 4 });
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), src);
            StubHandler handler = new();
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, _ => { }, handler, dir, ManifestJson(""));
            Initialize(service);
            Task<bool> task = Download(service);
            task.Wait();
            Assert.IsFalse(task.Result, "an oversized local source must not install");
            Assert.AreEqual("Failed", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.AreEqual(0, handler.Calls);
            Assert.AreEqual(5, new FileInfo(src).Length, "source file must remain intact");
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.part-*").Length);
        }

        [Test]
        public void LocalDevelopmentSourcePreCancelLeavesNoPartial()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            string dir = TempDir();
            string src = Path.Combine(dir, "local-source.bin");
            File.WriteAllBytes(src, new byte[] { 0, 1, 2, 3 });
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), src);
            StubHandler handler = new();
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, _ => { }, handler, dir, ManifestJson(""));
            Initialize(service);
            using CancellationTokenSource cancel = new();
            cancel.Cancel();
            Task<bool> task = Download(service, cancel.Token);
            task.Wait();
            Assert.IsFalse(task.Result, "a cancelled local transfer must not install");
            Assert.AreEqual("Missing", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.part-*").Length);
            Assert.AreEqual(new byte[] { 0, 1, 2, 3 }, File.ReadAllBytes(src));
        }

        [Test]
        public void LocalDevelopmentSourceExistingCacheTakesPriority()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            string dir = TempDir();
            string target = Path.Combine(dir, $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle");
            File.WriteAllBytes(target, new byte[] { 0, 1, 2, 3 });
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), Path.Combine(dir, "absent.bin"));
            StubHandler handler = new();
            string? path = null;
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, value => path = value, handler, dir, ManifestJson(""));
            Initialize(service);
            Assert.AreEqual("Ready", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.AreEqual(target, path, "the verified cache must be bound without any transfer");
            Assert.AreEqual(0, handler.Calls);
        }

        [Test]
        public void LocalDevelopmentSourceDeclineSuppressesOffer()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            string dir = TempDir();
            string src = Path.Combine(dir, "local-source.bin");
            File.WriteAllBytes(src, new byte[] { 0, 1, 2, 3 });
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), src);
            StubHandler handler = new();
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, _ => { }, handler, dir, ManifestJson(""));
            Initialize(service);
            Service.GetMethod("Decline", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
            NotifyRequest(service);
            Assert.IsFalse(Offer(service), "a declined user gets no automatic offer even with a local source");
        }

        [Test]
        public void ReleaseBuildIgnoresDevelopmentSourceFile()
        {
            Assume.That(!IsDebugSut(), "this asserts the Release compile-out");
            string dir = TempDir();
            string src = Path.Combine(dir, "local-source.bin");
            File.WriteAllBytes(src, new byte[] { 0, 1, 2, 3 });
            File.WriteAllText(Path.Combine(dir, "reflection-pack-development-source.txt"), src);
            StubHandler handler = new();
            object service = CreateService(Activator.CreateInstance(ConfigType, true)!, _ => { }, handler, dir, ManifestJson(""));
            Initialize(service);
            NotifyRequest(service);
            Assert.IsFalse(Offer(service), "Release must ignore the development source file even when present");
            Assert.AreEqual(0, handler.Calls);
        }

        [Test]
        public void LocalDevelopmentSourceMessageIsExplicit()
        {
            Assume.That(IsDebugSut(), "local development source only exists on Debug builds");
            Type ui = Plugin.GetType("ChromaGLS.Settings.FerrofluidReflectionDownloadUI", true)!;
            object missing = Enum.Parse(State, "Missing");
            ConstructorInfo manifestCtor = Plugin.GetType("ChromaGLS.Settings.FerrofluidReflectionPackManifest", true)!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
            object manifest = manifestCtor.Invoke(new object[] { $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle", PayloadHash, 4L, string.Empty });
            string message = (string)ui.InvokeMember("DescribeState", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.OptionalParamBinding, null, null, new object?[] { missing, manifest, null, 0, true })!;
            StringAssert.Contains("Development", message);
            StringAssert.Contains("local", message);
        }

        [Test]
        public void NeedsDownloadIsFalseWhileCheckingReadyOrUnavailable()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            object checking = CreateService(config, _ => { }, new StubHandler(), TempDir(), ManifestJson());
            Assert.AreEqual("Checking", Enum.GetName(State, StateProp.GetValue(checking)!));
            Assert.IsFalse(NeedsDownload(checking), "the button must stay hidden until verification resolves");

            object unavailable = CreateService(config, _ => { }, new StubHandler(), TempDir(), "{not json");
            Assert.AreEqual("Unavailable", Enum.GetName(State, StateProp.GetValue(unavailable)!));
            Assert.IsFalse(NeedsDownload(unavailable), "a build without an actionable pack must not show the entry point");

            string dir = TempDir();
            File.WriteAllBytes(Path.Combine(dir, $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle"), new byte[] { 0, 1, 2, 3 });
            object ready = CreateService(config, _ => { }, new StubHandler(), dir, ManifestJson());
            Initialize(ready);
            Assert.AreEqual("Ready", Enum.GetName(State, StateProp.GetValue(ready)!));
            Assert.IsFalse(NeedsDownload(ready), "an installed pack needs no download entry");
        }

        [Test]
        public void NeedsDownloadIsTrueForMissingDownloadingAndFailed()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            StubHandler handler = new();
            object service = CreateService(config, _ => { }, handler, TempDir(), ManifestJson());
            Initialize(service);
            Assert.AreEqual("Missing", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.IsTrue(NeedsDownload(service));

            Task<bool> pending = Download(service);
            Assert.AreEqual("Downloading", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.IsTrue(NeedsDownload(service), "the button must stay visible so progress or cancel can be reopened");
            handler.Release(new byte[] { 9, 9, 9, 9 });
            Assert.IsFalse(pending.GetAwaiter().GetResult());
            Assert.AreEqual("Failed", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.IsTrue(NeedsDownload(service), "a failed download must keep a visible retry entry");
        }

        [Test]
        public void NeedsDownloadFollowsInstalledBytes()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            string dir = TempDir();
            string target = Path.Combine(dir, $"ChromaGLS-Ferrofluid-Reflections-{PayloadHash}.bundle");
            File.WriteAllBytes(target, new byte[] { 9, 9, 9, 9 });
            object corrupt = CreateService(config, _ => { }, new StubHandler(), dir, ManifestJson());
            Initialize(corrupt);
            Assert.AreEqual("Missing", Enum.GetName(State, StateProp.GetValue(corrupt)!));
            Assert.IsTrue(NeedsDownload(corrupt), "out-of-date or corrupt installed bytes must surface the download entry");

            File.WriteAllBytes(target, new byte[] { 0, 1, 2, 3 });
            object intact = CreateService(config, _ => { }, new StubHandler(), dir, ManifestJson());
            Initialize(intact);
            Assert.AreEqual("Ready", Enum.GetName(State, StateProp.GetValue(intact)!));
            Assert.IsFalse(NeedsDownload(intact), "bytes matching the expected manifest hash must hide the entry");
        }

        [Test]
        public void StatusChangedFiresThroughInitializeAndDownloadTransitions()
        {
            object config = Activator.CreateInstance(ConfigType, true)!;
            StubHandler handler = new();
            object service = CreateService(config, _ => { }, handler, TempDir(), ManifestJson());
            int notifications = 0;
            Service.GetEvent("StatusChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetAddMethod(true)!
                .Invoke(service, new object[] { (Action)(() => notifications++) });
            Initialize(service);
            int afterInitialize = notifications;
            Task<bool> pending = Download(service);
            handler.Release(new byte[] { 0, 1, 2, 3 });
            Assert.IsTrue(pending.GetAwaiter().GetResult());
            Assert.AreEqual("Ready", Enum.GetName(State, StateProp.GetValue(service)!));
            Assert.IsFalse(NeedsDownload(service));
            Assert.Greater(afterInitialize, 0, "initialize resolving to Missing must raise StatusChanged");
            Assert.Greater(notifications, afterInitialize, "the download transition back to Ready must raise StatusChanged");
        }

        private static bool NeedsDownload(object service)
        {
            PropertyInfo? prop = Service.GetProperty("NeedsDownload", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(prop, "the pack service must expose a NeedsDownload flag for the settings button");
            return (bool)prop!.GetValue(service)!;
        }

        private static Task<bool> Download(object service, CancellationToken cancel = default) =>
            (Task<bool>)Service.GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object?[] { null, cancel })!;

        private static void Initialize(object service) =>
            ((Task)Service.GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!).GetAwaiter().GetResult();

        private static bool Offer(object service) =>
            (bool)Service.GetMethod("TryOfferDownload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!;

        private static void NotifyRequest(object service) =>
            Service.GetMethod("NotifyReflectionsRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
    }
}
