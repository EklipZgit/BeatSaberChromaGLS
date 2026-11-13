#if FERROFLUID_TWEAKS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    // One-shot F12 timing diagnostic: starts on the first menu F12 that wrote a real snapshot,
    // samples the settled timing candidates for 120 rendered frames, then writes
    // timing-probe.json beside the triggering capture. A second F12 while running does not
    // restart it, menu exit cancels and writes whatever was collected. Results are diagnostic
    // candidates, not proven budget-admission inputs.
    internal static class FerrofluidMenuTimingProbe
    {
        internal const int TargetFrames = 120;
        internal const string TimingProbeFileName = "timing-probe.json";

#if !V1_29_1
        private static readonly string[] Candidates =
        {
            "Main Thread",
            "CPU Main Thread Frame Time",
            "CPU Render Thread Frame Time",
            "PlayerLoop",
            "GPU Frame Time",
            "Gfx.WaitForPresentOnGfxThread",
            "WaitForTargetFPS",
            "Gfx.PresentFrame",
        };
#endif

        internal static Probe? Active { get; private set; }

        // Called from the F12 capture path after the snapshot is written. Returns false when a
        // probe is already running or when the capture directory is missing/empty — a failed
        // screenshot must not schedule the probe or write beside the current directory.
        internal static bool NotifyCapture(MonoBehaviour host, string? captureDirectory)
        {
            if (Active != null || captureDirectory == null || captureDirectory.Length == 0 || !Directory.Exists(captureDirectory))
                return false;

            Probe? probe = null;
            try
            {
                probe = new Probe(captureDirectory, BuildMetadata());
                BuildSources(probe);
            }
            catch (Exception error)
            {
                // A partial source list must still release whatever recorders were created.
                probe?.Dispose();
                Plugin.Log?.Warn($"[ChromaGLS Timing Probe] setup failed, leaving snapshot intact: {error}");
                return false;
            }

            Active = probe;
            try
            {
                host.StartCoroutine(probe.Run());
            }
            catch (Exception error)
            {
                Active = null;
                probe.Dispose();
                Plugin.Log?.Warn($"[ChromaGLS Timing Probe] could not start frames: {error}");
                return false;
            }

            return true;
        }

        // Menu exit / host teardown: finish with the reason; Finish owns the dispose.
        internal static void Cancel(string reason)
        {
            Active?.Finish("cancelled:" + reason);
        }

        private static JObject BuildMetadata()
        {
            Camera? main = Camera.main;
            return new JObject
            {
                ["gameVersion"] = Application.version,
                ["unityVersion"] = Application.unityVersion,
                ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(),
                ["stereoEnabled"] = main != null && main.stereoEnabled ? JToken.FromObject(true) : JToken.FromObject(false),
                ["stereoCamera"] = main != null ? main.name : null,
                ["targetFrameRate"] = Application.targetFrameRate,
                ["vSyncCount"] = QualitySettings.vSyncCount,
            };
        }

        private static void BuildSources(Probe probe)
        {
            var frameTiming = new FrameTiming[1];
            bool frameTimingEnabled = false;
#if !PRE_V1_40_8
            try
            {
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
            }
            catch
            {
                // unavailable on this build: the source records featureEnabled:false every frame
            }
#endif

            probe.AddSource("frameTiming", (index, frame) =>
            {
                var entry = new JObject { ["featureEnabled"] = frameTimingEnabled };
                if (frameTimingEnabled)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    uint count = FrameTimingManager.GetLatestTimings(1, frameTiming);
                    entry["count"] = (int)count;
                    if (count > 0)
                    {
                        FrameTiming timing = frameTiming[0];
#if !PRE_V1_40_8
                        entry["frameStartTimestamp"] = timing.frameStartTimestamp;
#endif
                        entry["cpuFrameTime"] = timing.cpuFrameTime;
                        entry["gpuFrameTime"] = timing.gpuFrameTime;
#if !PRE_V1_40_8
                        entry["cpuMainThreadFrameTime"] = timing.cpuMainThreadFrameTime;
                        entry["cpuRenderThreadFrameTime"] = timing.cpuRenderThreadFrameTime;
                        entry["cpuMainThreadPresentWaitTime"] = timing.cpuMainThreadPresentWaitTime;
#endif
                    }
                }

                return entry;
            });

            UnityEngine.XR.XRDisplaySubsystem? subsystem = null;
            try
            {
                var instances = new List<UnityEngine.XR.XRDisplaySubsystem>();
#if V1_29_1
                SubsystemManager.GetInstances(instances);
#else
                SubsystemManager.GetSubsystems(instances);
#endif
                foreach (UnityEngine.XR.XRDisplaySubsystem instance in instances)
                {
                    if (instance.running)
                    {
                        subsystem = instance;
                        break;
                    }
                }

                subsystem ??= null;
            }
            catch (Exception error)
            {
                probe.AddSource("xrDisplay", (index, frame) => new JObject { ["error"] = error.Message });
            }

            if (subsystem != null)
            {
                probe.AddSource("xrDisplay", (index, frame) =>
                {
                    bool gpuOk = subsystem.TryGetAppGPUTimeLastFrame(out float gpuSeconds);
                    bool refreshOk = subsystem.TryGetDisplayRefreshRate(out float refreshRate);
                    return new JObject
                    {
                        ["running"] = subsystem.running,
#if V1_29_1
                        ["id"] = subsystem.SubsystemDescriptor.id,
#else
                        ["id"] = subsystem.subsystemDescriptor.id,
#endif
                        ["appGpuTimeOk"] = gpuOk,
                        ["appGpuTimeSeconds"] = gpuSeconds,
                        ["appGpuTimeMs"] = gpuSeconds * 1000f,
                        ["refreshRateOk"] = refreshOk,
                        ["refreshRate"] = refreshRate,
                    };
                });
            }
            else
            {
                probe.AddSource("xrDisplay", (index, frame) => new JObject { ["active"] = false });
            }

#if !V1_29_1
            var discovered = new HashSet<string>();
            try
            {
                var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
                Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
                foreach (Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle handle in handles)
                {
                    Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderDescription description =
                        Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(handle);
                    string name = description.Name;
                    if (!Candidates.Contains(name) || !discovered.Add(name))
                        continue;
                    try
                    {
                        var recorder = new Unity.Profiling.ProfilerRecorder(
                            handle,
                            1,
                            Unity.Profiling.ProfilerRecorderOptions.StartImmediately | Unity.Profiling.ProfilerRecorderOptions.WrapAroundWhenCapacityReached);

                        // Register ownership before discovering another handle can fail.
                        probe.AddSource(
                            "recorder:" + name,
                            (index, frame) =>
                            {
                                var entry = new JObject
                                {
                                    ["available"] = true,
                                    ["lastValue"] = recorder.LastValue,
                                    ["count"] = recorder.Count,
                                    ["valid"] = recorder.Valid,
                                    ["running"] = recorder.IsRunning,
                                    ["unit"] = description.UnitType.ToString(),
                                    ["category"] = description.Category.ToString(),
                                    ["flags"] = description.Flags.ToString(),
                                    ["capacity"] = recorder.Capacity,
                                };
                                if (recorder.Count > 0)
                                    entry["sampleCount"] = recorder.GetSample(0).Count;

                                return entry;
                            },
                            recorder.Dispose);
                    }
                    catch (Exception error)
                    {
                        probe.AddSource("recorder:" + name, (index, frame) => new JObject
                        {
                            ["available"] = false,
                            ["error"] = error.Message,
                        });
                    }
                }
            }
            catch (Exception error)
            {
                probe.AddSource("recorders", (index, frame) => new JObject { ["error"] = error.Message });
            }

            foreach (string name in Candidates)
            {
                if (!discovered.Contains(name))
                    probe.AddSource("recorder:" + name, (index, frame) => new JObject { ["available"] = false });
            }
#endif
        }

        // Sources and the writer are injected so the lifecycle can be tested without timing APIs.
        internal sealed class Probe : IDisposable
        {
            private readonly string _directory;
            private readonly JObject _metadata;
            private readonly Func<JObject, string, string> _write;
            private readonly List<Source> _sources = new();
            private readonly JArray _frames = new();
            private readonly WaitForEndOfFrame _wait = new();
            private int _frameIndex;
            private bool _finished;
            private bool _disposed;

            internal Probe(string directory, JObject metadata, Func<JObject, string, string>? write = null)
            {
                _directory = directory;
                _metadata = metadata;
                _write = write ?? DefaultWrite;
            }

            internal void AddSource(string name, Func<int, JObject, JToken?>? sample = null, Action? dispose = null)
                => _sources.Add(new Source(name, sample, dispose));

            internal IEnumerator Run()
            {
                for (int index = 0; index < TargetFrames && !_finished; index++)
                {
                    yield return _wait;
                    Tick();
                }

                Finish("completed");
            }

            internal void Tick()
            {
                if (_finished)
                    return;

                var frame = new JObject { ["frameIndex"] = _frameIndex };
                try
                {
                    frame["frameCount"] = Time.frameCount;
                    frame["realtime"] = Time.realtimeSinceStartup;
                    frame["unscaledTime"] = Time.unscaledTime;
                }
                catch
                {
                    // the timing fields are unavailable in a host without the engine clock
                }

                foreach (Source source in _sources)
                {
                    if (source.SampleFunc == null)
                        continue;
                    try
                    {
                        frame[source.Name] = source.SampleFunc(_frameIndex, frame);
                    }
                    catch (Exception error)
                    {
                        frame[source.Name] = new JObject { ["error"] = error.Message };
                    }
                }

                _frames.Add(frame);
                _frameIndex++;
            }

            // Idempotent finish: writes once, clears Active only when this probe owns it,
            // and disposes the sources exactly once.
            internal void Finish(string reason)
            {
                if (_finished)
                    return;
                _finished = true;
                if (ReferenceEquals(FerrofluidMenuTimingProbe.Active, this))
                    FerrofluidMenuTimingProbe.Active = null;

                var document = new JObject
                {
                    ["kind"] = "menu-timing-probe",
                    ["status"] = reason,
                    ["captureDirectory"] = _directory,
                    ["metadata"] = _metadata,
                    ["framesRequested"] = TargetFrames,
                    ["framesCaptured"] = _frames.Count,
                    ["frames"] = _frames,
                };
                try
                {
                    string path = _write(document, _directory);
                    Plugin.Log?.Info($"[ChromaGLS Timing Probe] {reason} after {_frames.Count} frame(s) -> {path}");
                }
                catch (Exception error)
                {
                    Plugin.Log?.Error($"[ChromaGLS Timing Probe] write failed: {error}");
                }
                finally
                {
                    Dispose();
                }
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (Source source in _sources)
                {
                    try
                    {
                        source.DisposeAction?.Invoke();
                    }
                    catch
                    {
                        // a source's own disposal must not abort the rest
                    }
                }
            }

            private static string DefaultWrite(JObject document, string directory)
            {
                string path = Path.Combine(directory, TimingProbeFileName);
                FerrofluidSnapshotIo.Live.WriteAllText(path, document.ToString(Newtonsoft.Json.Formatting.Indented));
                return path;
            }

            private sealed class Source
            {
                internal Source(string name, Func<int, JObject, JToken?>? sample, Action? dispose)
                {
                    Name = name;
                    SampleFunc = sample;
                    DisposeAction = dispose;
                }

                internal string Name { get; }

                internal Func<int, JObject, JToken?>? SampleFunc { get; }

                internal Action? DisposeAction { get; }
            }
        }
    }
}
#endif
