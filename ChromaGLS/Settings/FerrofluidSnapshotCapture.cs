#if FERROFLUID_TWEAKS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using IPA.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    // Every side effect the capture path performs against the host can be replaced by tests;
    // a null member falls back to the real filesystem, clipboard, and shell viewer.
    internal sealed class FerrofluidSnapshotIo
    {
        internal static readonly FerrofluidSnapshotIo Live = new();

        internal Func<DateTime>? Now { get; set; }

        internal Action<string, byte[]>? WriteBytes { get; set; }

        internal Action<string, string>? WriteText { get; set; }

        internal Action<string>? SetClipboardText { get; set; }

        internal Action<string>? OpenFile { get; set; }

        internal DateTime ResolveNow() => Now?.Invoke() ?? DateTime.UtcNow;

        internal void WriteAllBytes(string path, byte[] bytes)
        {
            if (WriteBytes != null)
                WriteBytes(path, bytes);
            else
                File.WriteAllBytes(path, bytes);
        }

        internal void WriteAllText(string path, string text)
        {
            if (WriteText != null)
                WriteText(path, text);
            else
                File.WriteAllText(path, text);
        }

        internal void CopyToClipboard(string text)
        {
            if (SetClipboardText != null)
                SetClipboardText(text);
            else
                GUIUtility.systemCopyBuffer = text;
        }

        internal void OpenPath(string path)
        {
            if (OpenFile != null)
            {
                OpenFile(path);
                return;
            }

            // The plugin defines no UNITY_STANDALONE_* symbol, so the platform check stays runtime.
            if (Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.WindowsEditor)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
    }

    internal sealed class FerrofluidCaptureResult
    {
        internal string? Directory { get; set; }

        internal string? ScreenshotPath { get; set; }

        internal List<string> JsonPaths { get; } = new();
    }

    internal static partial class FerrofluidSnapshotCapture
    {
        internal const string RootDirectoryName = "CglsFerroSnapshots";
        private const int SchemaVersion = 1;
        private const float RenderedFadeThreshold = 0.001f;

        internal static string DefaultRootDirectory()
            => Path.Combine(Path.GetTempPath(), RootDirectoryName);

        // Holding F12 repeats GetKeyDown; the busy flag keeps exactly one coroutine in flight.
        internal static bool ShouldSchedule(bool busy, bool keyDown) => keyDown && !busy;

        // Eligibility is render state only: hidden, disabled, and fully faded objects are skipped
        // while off-screen but still-rendered objects always export.
        internal static bool IsRendered(bool activeInHierarchy, bool rendererEnabled, float fadeAlpha)
            => activeInHierarchy && rendererEnabled && fadeAlpha > RenderedFadeThreshold;

        internal static FerrofluidCaptureResult CaptureFrame()
        {
            var io = FerrofluidSnapshotIo.Live;
            DateTime utc = io.ResolveNow();
            string root = DefaultRootDirectory();
            JObject shared = BuildSharedContext(utc);
            var subjects = new List<JObject>();
            FerrofluidMagnetController? hands = null;

            foreach (FerrofluidPrimitiveEffect effect in Resources.FindObjectsOfTypeAll<FerrofluidPrimitiveEffect>())
            {
                if (!effect.IsRendered())
                    continue;
                hands ??= effect.MagnetController;
                subjects.Add(effect.BuildObjectSnapshot());
            }

            foreach (ChromaGlsRequirementEffect effect in Resources.FindObjectsOfTypeAll<ChromaGlsRequirementEffect>())
            {
                JObject? subject = effect.BuildObjectSnapshot();
                if (subject == null)
                    continue;
                hands ??= effect.MagnetController;
                subjects.Add(subject);
            }

            shared["hands"] = hands != null ? hands.ExportHands() : new JArray();
            shared["rig"] = BuildRigContext(hands != null ? hands.PlayerAnchor : null);
            subjects.Sort((a, b) => string.CompareOrdinal(a["name"]?.Value<string>(), b["name"]?.Value<string>()));

            byte[]? png = CaptureScreenshotPng();
            if (png == null)
            {
                Plugin.Log?.Error("[ChromaGLS Ferrofluid Snapshot] screenshot capture failed; no capture written.");
                return new FerrofluidCaptureResult();
            }

            var readbackFiles = new Dictionary<string, byte[]>();
            if (FerrofluidGpuResources.TryGet(out FerrofluidGpuResources? resources) && resources!.SnapshotReadbackShader != null)
            {
                try
                {
                    readbackFiles = CaptureRenderingAssets((JObject)shared["rendering"]!, resources.SnapshotReadbackShader);
                }
                catch (Exception readbackError)
                {
                    Plugin.Log?.Error($"[ChromaGLS Ferrofluid Snapshot] HDR readback failed: {readbackError}");
                    ((JObject)shared["rendering"]!)["error"] = readbackError.Message;
                    if (shared["rendering"]?["globalTextures"] is JObject textures)
                    {
                        foreach (JProperty property in textures.Properties())
                        {
                            if (property.Value is JObject descriptor)
                            {
                                descriptor["files"] = new JArray();
                                descriptor["fileMetadata"] = new JArray();
                            }
                        }
                    }
                }
            }
            else
            {
                ((JObject)shared["rendering"]!)["error"] = "snapshot readback shader unavailable";
            }

            FerrofluidCaptureResult result = WriteCaptureWithAssets(root, utc, shared, subjects.ToArray(), png, readbackFiles, io);
            Plugin.Log?.Info($"[ChromaGLS Ferrofluid Snapshot] wrote {result.JsonPaths.Count} object JSON(s) and {(result.ScreenshotPath != null ? "screenshot.png" : "no screenshot")} to {result.Directory}.");
            return result;
        }

        internal static FerrofluidCaptureResult WriteCapture(
            string rootDirectory,
            DateTime capturedUtc,
            JObject shared,
            JObject[] subjects,
            byte[]? screenshotPng,
            FerrofluidSnapshotIo io)
            => WriteCaptureWithAssets(rootDirectory, capturedUtc, shared, subjects, screenshotPng, new Dictionary<string, byte[]>(), io);

        internal static FerrofluidCaptureResult WriteCaptureWithAssets(
            string rootDirectory,
            DateTime capturedUtc,
            JObject shared,
            JObject[] subjects,
            byte[]? screenshotPng,
            Dictionary<string, byte[]> hdrAssets,
            FerrofluidSnapshotIo io)
        {
            if (screenshotPng == null || screenshotPng.Length == 0)
                throw new InvalidOperationException("[ChromaGLS Ferrofluid Snapshot] a capture without a screenshot must not publish, prune, or notify.");

            string directory = FerrofluidSnapshotStore.CreateCaptureDirectory(rootDirectory, capturedUtc);
            var result = new FerrofluidCaptureResult { Directory = directory };
            try
            {
                if (screenshotPng != null)
                {
                    result.ScreenshotPath = Path.Combine(directory, "screenshot.png");
                    io.WriteAllBytes(result.ScreenshotPath, screenshotPng);
                }

                foreach (KeyValuePair<string, byte[]> asset in hdrAssets)
                    io.WriteAllBytes(Path.Combine(directory, asset.Key), asset.Value);

                for (int index = 0; index < subjects.Length; index++)
                {
                    var file = new JObject
                    {
                        ["shared"] = shared.DeepClone(),
                        ["subject"] = subjects[index],
                    };
                    string path = Path.Combine(directory, $"{index:D2}-{Slug(subjects[index]["name"]?.Value<string>())}.json");
                    io.WriteAllText(path, file.ToString(Formatting.Indented));
                    result.JsonPaths.Add(path);
                }
            }
            catch
            {
                // A partially written directory is our own fresh timestamp dir, safe to remove outright.
                try
                {
                    FerrofluidSnapshotStore.DeleteTree(directory);
                }
                catch (Exception cleanupError)
                {
                    Plugin.Log?.Error($"[ChromaGLS Ferrofluid Snapshot] could not remove incomplete capture {directory}: {cleanupError}");
                }

                throw;
            }

            // Side effects run only after every write succeeded, and a missing clipboard or viewer
            // never downgrades a valid capture.
            try
            {
                io.CopyToClipboard(directory);
            }
            catch (Exception clipboardError)
            {
                Plugin.Log?.Error($"[ChromaGLS Ferrofluid Snapshot] clipboard copy failed: {clipboardError}");
            }

            if (result.ScreenshotPath != null)
            {
                try
                {
                    io.OpenPath(result.ScreenshotPath);
                }
                catch (Exception viewerError)
                {
                    Plugin.Log?.Error($"[ChromaGLS Ferrofluid Snapshot] could not open {result.ScreenshotPath}: {viewerError}");
                }
            }

            try
            {
                FerrofluidSnapshotStore.PruneCaptureDirectories(rootDirectory, FerrofluidSnapshotStore.MaximumCaptureDirectories);
            }
            catch (Exception pruneError)
            {
                Plugin.Log?.Error($"[ChromaGLS Ferrofluid Snapshot] pruning failed: {pruneError}");
            }

            return result;
        }

        private static byte[]? CaptureScreenshotPng()
        {
            Texture2D? screenshot = null;
            try
            {
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                return EncodeScreenshotPng(screenshot);
            }
            catch (Exception error)
            {
                Plugin.Log?.Error($"[ChromaGLS Ferrofluid Snapshot] screen capture failed: {error}");
                return null;
            }
            finally
            {
                if (screenshot != null)
                    UnityEngine.Object.Destroy(screenshot);
            }
        }

        // The captured frame buffer carries real alpha, and PNG viewers that honor it hide the
        // scene; the screenshot must always be encoded opaque.
        internal static byte[] EncodeScreenshotPng(Texture2D screenshot)
        {
            Color32[] pixels = screenshot.GetPixels32();
            for (int index = 0; index < pixels.Length; index++)
                pixels[index].a = 255;
            screenshot.SetPixels32(pixels);
            return ImageConversion.EncodeToPNG(screenshot);
        }

        private static JObject BuildSharedContext(DateTime utc)
        {
            var shared = new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["coordinateUnits"] = new JObject
                {
                    ["worldPositions"] = "metres",
                    ["localSpace"] = "object-local units before transform.localScale; matrices row-major m00..m33",
                    ["chartPosition"] = "-1..1 per axis; particle texture UV = chart*0.5+0.5",
                    ["quaternion"] = "x,y,z,w",
                    ["eulerAngles"] = "degrees",
                    ["angularVelocity"] = "radians per second",
                    ["springHeightScale"] = "dimensionless multiplier, not metres",
                    ["surfaceDisplacementHeightWorld"] = "metres; rendered minus analytic base, includes bulge/density support and spike deformation",
                    ["trigger"] = "normalized 0..1; triggerPercent = trigger*100",
                },
                ["capturedUtc"] = utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                ["frame"] = Time.frameCount,
                ["time"] = Time.time,
                ["unscaledTime"] = Time.unscaledTime,
                ["fixedTime"] = Time.fixedTime,
                ["unityVersion"] = Application.unityVersion,
                ["gameVersion"] = UnityGame.GameVersion.ToString(),
                ["pluginVersion"] = typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                ["pluginAssembly"] = typeof(Plugin).Assembly.GetName().FullName,
                ["pluginMvid"] = typeof(Plugin).Assembly.ManifestModule.ModuleVersionId.ToString("D"),
                ["ferrofluidBundle"] = new JObject
                {
                    ["resourceName"] = FerrofluidGpuResources.BundleResourceName,
                    ["sha256"] = EmbeddedBundleSha256(),
                },
                ["rendering"] = BuildRenderingContext(),
                ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(),
                ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
                ["screen"] = new JObject
                {
                    ["width"] = Screen.width,
                    ["height"] = Screen.height,
                },
            };

            Camera? main = Camera.main;
            shared["mainCamera"] = main != null ? (JToken)FerrofluidSnapshotPose.From(main.transform.position, main.transform.rotation).ToObject() : JValue.CreateNull();
            var cameras = new JArray();
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null || !camera.enabled)
                    continue;
                cameras.Add(new JObject
                {
                    ["name"] = camera.name,
                    ["tag"] = camera.tag,
                    ["depth"] = camera.depth,
                    ["fieldOfView"] = camera.fieldOfView,
                    ["nearClipPlane"] = camera.nearClipPlane,
                    ["farClipPlane"] = camera.farClipPlane,
                    ["orthographic"] = camera.orthographic,
                    ["orthographicSize"] = camera.orthographicSize,
                    ["viewportRect"] = new JObject
                    {
                        ["x"] = camera.rect.x,
                        ["y"] = camera.rect.y,
                        ["width"] = camera.rect.width,
                        ["height"] = camera.rect.height,
                    },
                    ["stereoTargetEye"] = camera.stereoTargetEye.ToString(),
                    ["stereoEnabled"] = camera.stereoEnabled,
                    ["stereoLeftProjectionMatrix"] = camera.stereoEnabled
                        ? FerrofluidSnapshotMatrix4x4.From(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)).ToToken()
                        : JValue.CreateNull(),
                    ["stereoRightProjectionMatrix"] = camera.stereoEnabled
                        ? FerrofluidSnapshotMatrix4x4.From(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)).ToToken()
                        : JValue.CreateNull(),
                    ["stereoLeftViewMatrix"] = camera.stereoEnabled
                        ? FerrofluidSnapshotMatrix4x4.From(camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left)).ToToken()
                        : JValue.CreateNull(),
                    ["stereoRightViewMatrix"] = camera.stereoEnabled
                        ? FerrofluidSnapshotMatrix4x4.From(camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right)).ToToken()
                        : JValue.CreateNull(),
                    ["pose"] = FerrofluidSnapshotPose.From(camera.transform.position, camera.transform.rotation).ToObject(),
                    ["projectionMatrix"] = FerrofluidSnapshotMatrix4x4.From(camera.projectionMatrix).ToToken(),
                    ["worldToCameraMatrix"] = FerrofluidSnapshotMatrix4x4.From(camera.worldToCameraMatrix).ToToken(),
                });
            }

            shared["cameras"] = cameras;
            return shared;
        }

        private static JToken BuildRigContext(Transform? playerAnchor)
        {
            if (playerAnchor == null)
                return JValue.CreateNull();
            var rig = new JObject
            {
                ["name"] = playerAnchor.name,
                ["pose"] = FerrofluidSnapshotPose.From(playerAnchor.position, playerAnchor.rotation).ToObject(),
            };
            var controller = playerAnchor.GetComponent<MenuPlayerController>();
            if (controller != null)
            {
                rig["headRotation"] = FerrofluidSnapshotQuaternion.From(controller.headRot).ToToken();
                rig["headEuler"] = FerrofluidSnapshotData.ToToken(controller.headRot.eulerAngles);
            }

            return rig;
        }

        private static string Slug(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return "object";
            var slug = new StringBuilder(name!.Length);
            bool lastDash = false;
            foreach (char character in name.ToLowerInvariant())
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    slug.Append(character);
                    lastDash = false;
                }
                else if (!lastDash)
                {
                    slug.Append('-');
                    lastDash = true;
                }
            }

            string text = slug.ToString().Trim('-');
            return text.Length > 0 ? text : "object";
        }
    }
}
#endif
