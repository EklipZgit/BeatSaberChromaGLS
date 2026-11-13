using System;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChromaGLS.Tests
{
    internal sealed class FerrofluidMenuTimingProbeTests
    {
        private static readonly Assembly Plugin = Assembly.Load("ChromaGLS");
        private static readonly Type ProbeType = Plugin.GetType("ChromaGLS.Settings.FerrofluidMenuTimingProbe+Probe")!;
        private static readonly Type Static = Plugin.GetType("ChromaGLS.Settings.FerrofluidMenuTimingProbe")!;

        private static object NewProbe(string dir, JObject metadata, Func<JObject, string, string>? write = null)
            => Activator.CreateInstance(ProbeType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { dir, metadata, write }, null)!;

        private static void AddSource(object probe, string name, Func<int, JObject, JToken?>? sample = null, Action? dispose = null)
            => ProbeType.GetMethod("AddSource", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(probe, new object?[] { name, sample, dispose });

        private static void Tick(object probe) => ProbeType.GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(probe, Array.Empty<object>());
        private static void Finish(object probe, string reason) => ProbeType.GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(probe, new object[] { reason });
        private static bool Finished(object probe) => (bool)ProbeType.GetField("_finished", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(probe)!;

        [Test]
        public void ProbeCompletesTargetFramesAndWritesOnce()
        {
            int writes = 0;
            int samples = 0;
            int disposes = 0;
            object probe = NewProbe("C:\\fake\\dir", new JObject { ["gameVersion"] = "t" }, (doc, d) => { writes++; return d + "\\timing-probe.json"; });
            AddSource(probe, "fake", (i, f) => { samples++; return i; }, () => disposes++);
            for (int index = 0; index < 120; index++)
                Tick(probe);
            Finish(probe, "completed");
            Assert.That(samples, Is.EqualTo(120));
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(disposes, Is.EqualTo(1));
            Assert.That(Finished(probe), Is.True);
        }

        [Test]
        public void TickAfterFinishDoesNotAppend()
        {
            int samples = 0;
            object probe = NewProbe("C:\\fake\\dir", new JObject(), (doc, d) => "p");
            AddSource(probe, "fake", (i, f) => { samples++; return i; });
            Tick(probe);
            Finish(probe, "completed");
            Tick(probe);
            Assert.That(samples, Is.EqualTo(1));
        }

        [Test]
        public void CancelWritesPartialAndDisposesOnce()
        {
            int disposes = 0;
            int writes = 0;
            object probe = NewProbe("C:\\fake\\dir", new JObject(), (doc, d) => { writes++; return d + "\\timing-probe.json"; });
            AddSource(probe, "fake", (i, f) => i, () => disposes++);
            Tick(probe);
            Finish(probe, "cancelled:menu-exit");
            Finish(probe, "cancelled:menu-exit"); // second finish must be a no-op
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(disposes, Is.EqualTo(1));
        }

        [Test]
        public void WriteFailureStillDisposesSources()
        {
            int disposes = 0;
            object probe = NewProbe("C:\\fake\\dir", new JObject(), (doc, d) => throw new InvalidOperationException("io-fail"));
            AddSource(probe, "fake", (i, f) => i, () => disposes++);
            Tick(probe);
            Assert.DoesNotThrow(() => Finish(probe, "completed"));
            Assert.That(disposes, Is.EqualTo(1));
        }

        [Test]
        public void MissingAndFalseValuesArePreservedNotSubstituted()
        {
            JObject? written = null;
            object probe = NewProbe("C:\\fake\\dir", new JObject(), (doc, d) => { written = doc; return "p"; });
            AddSource(probe, "recorder:Main Thread", (i, f) => new JObject { ["available"] = false });
            AddSource(probe, "frameTiming", (i, f) => new JObject { ["featureEnabled"] = false });
            Tick(probe);
            Finish(probe, "completed");
            Assert.That(written, Is.Not.Null);
            var frame = (JObject)written!["frames"]![0]!;
            Assert.That(frame["recorder:Main Thread"]!["available"]!.Value<bool>(), Is.False);
            Assert.That(frame["frameTiming"]!["featureEnabled"]!.Value<bool>(), Is.False);
        }

        [Test]
        public void ActiveClearsAfterNormalFinish()
        {
            object probe = NewProbe("C:\\fake\\dir", new JObject(), (doc, d) => "p");
            // Manually install as the active probe, finish, confirm the static clears.
            Static.GetProperty("Active", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, probe);
            Finish(probe, "completed");
            Assert.That(Static.GetProperty("Active", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null), Is.Null);
        }

        [Test]
        public void NotifyCaptureOnEmptyDirectoryDoesNotStart()
        {
            // The empty-directory check runs before StartCoroutine, so a null host exercises
            // it without a real GameObject in the test host.
            bool started = (bool)Static.GetMethod("NotifyCapture", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object?[] { null, "" })!;
            Assert.That(started, Is.False);
        }
    }
}
