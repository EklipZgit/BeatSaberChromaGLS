using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests
{
    // The ferrofluid menu-reflection seam stays internal to ChromaGLS.dll; the tests drive it
    // through the same Assembly.Load + reflection seam the snapshot fixtures use.
    internal sealed class FerrofluidMenuReflectionGeometryTests
    {
        private static readonly Assembly Plugin = Assembly.Load("ChromaGLS");
        private static readonly Type Geometry = Plugin.GetType("ChromaGLS.Settings.FerrofluidMenuReflectionGeometry")!;
        private static readonly MethodInfo TryIntersect = Geometry.GetMethod("TryIntersectCanvas", BindingFlags.Static | BindingFlags.NonPublic)!;

        private static bool Hit(Vector3 origin, Vector3 dir, float radius, out Vector3 hit, out int tile)
        {
            object[] args = { origin, dir, (Vector2)new Vector2(0f, -140f), radius, -50f, 50f, 90f, default(Vector3), -1 };
            bool accepted = (bool)TryIntersect.Invoke(null, args)!;
            hit = (Vector3)args[7];
            tile = (int)args[8];
            return accepted;
        }

        [Test]
        public void RayHitsCylinderAndResolvesTile()
        {
            Assert.That(Hit(new Vector3(0f, 0f, 60f), new Vector3(0f, 0f, -1f), 140f, out Vector3 hit, out int tile), Is.True);
            Assert.That(hit.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(tile, Is.InRange(1, 2));
        }

        [Test]
        public void RayOutsideFrontArcMisses()
        {
            Vector3 dir = new(Mathf.Sin(2.35f), 0f, Mathf.Cos(2.35f));
            Assert.That(Hit(new Vector3(0f, 0f, 60f), dir, 140f, out _, out _), Is.False);
        }

        [Test]
        public void RayOutsideVerticalSpanMisses()
        {
            Assert.That(Hit(new Vector3(0f, 200f, 60f), new Vector3(0f, -0.9f, -0.44f).normalized, 140f, out _, out _), Is.False);
        }

        [Test]
        public void BackRootFallbackAcceptsFarIntersection()
        {
            Assert.That(Hit(new Vector3(0f, 0f, -70f), new Vector3(0f, 0f, 1f), 140f, out Vector3 hit, out _), Is.True);
            Assert.That(hit.z, Is.EqualTo(0f).Within(0.001f));
        }
    }

    internal sealed class FerrofluidMenuReflectionBudgetTests
    {
        private static readonly Assembly Plugin = Assembly.Load("ChromaGLS");
        private static readonly Type Budget = Plugin.GetType("ChromaGLS.Settings.FerrofluidMenuReflectionBudget")!;
        private object _budget = null!;
        private PropertyInfo _deferReason = null!;

        [SetUp]
        public void SetUp()
        {
            _budget = Activator.CreateInstance(Budget, nonPublic: true)!;
            _deferReason = Budget.GetProperty("DeferReason", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Budget.GetMethod("SetTargetRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_budget, new object[] { 90f });
        }

        private void Frames(long first, double cpu = 4.0, double gpu = 4.0)
        {
            MethodInfo record = Budget.GetMethod("RecordFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (long id = first; id < first + 4; id++)
                record.Invoke(_budget, new object[] { id, cpu, gpu });
        }

        private bool Admit(long frame, float time)
            => (bool)Budget.GetMethod("AdmitTile", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_budget, new object[] { frame, time })!;

        private string Reason() => (string)_deferReason.GetValue(_budget)!;

        [Test]
        public void AdmitsWhenLastFourConsecutiveFramesFit()
        {
            Frames(10);
            Assert.That(Admit(14, 10f), Is.True, Reason());
        }

        [Test]
        public void UnknownRefreshDefers()
        {
            Budget.GetMethod("SetTargetRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_budget, new object[] { -1f });
            Frames(10);
            Assert.That(Admit(14, 10f), Is.False);
            Assert.That(Reason(), Is.EqualTo("refresh-unknown"));
        }

        [Test]
        public void InvalidFrameTimingResetsHistory()
        {
            Frames(10);
            Budget.GetMethod("RecordFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_budget, new object[] { 14L, double.NaN, 4.0 });
            Assert.That(Admit(15, 10f), Is.False);
            Assert.That(Reason(), Is.EqualTo("timing-history"));
        }

        [Test]
        public void NonConsecutiveFrameResetsHistory()
        {
            Frames(10);
            Budget.GetMethod("RecordFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_budget, new object[] { 20L, 4.0, 4.0 });
            Assert.That(Admit(21, 10f), Is.False);
            Assert.That(Reason(), Is.EqualTo("timing-history"));
        }

        [Test]
        public void QuietPeriodBlocksEarlyAdmit()
        {
            Frames(10);
            Budget.GetMethod("Invalidate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_budget, new object[] { 9.9f });
            Assert.That(Admit(14, 10.2f), Is.False);
            Assert.That(Reason(), Is.EqualTo("quiet-period"));
        }

        [Test]
        public void OverBudgetFrameDefers()
        {
            Frames(10, cpu: 20.0);
            Assert.That(Admit(14, 10f), Is.False);
            Assert.That(Reason(), Is.EqualTo("frame-over-budget"));
        }

        [Test]
        public void OnlyOneAdmitPerFrame()
        {
            Frames(10);
            Assert.That(Admit(14, 10f), Is.True);
            Assert.That((bool)Budget.GetMethod("AdmitPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_budget, new object[] { 14L, 10f })!, Is.False);
            Assert.That(Reason(), Is.EqualTo("already-admitted-this-frame"));
        }

        [Test]
        public void InFlightCaptureDefers()
        {
            Frames(10);
            Budget.GetProperty("Capturing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_budget, true);
            Assert.That(Admit(14, 10f), Is.False);
            Assert.That(Reason(), Is.EqualTo("capture-in-flight"));
        }
    }
}
