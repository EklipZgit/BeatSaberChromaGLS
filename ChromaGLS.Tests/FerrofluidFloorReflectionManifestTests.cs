using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests
{
    [TestFixture]
    public class FerrofluidFloorReflectionManifestTests
    {
        private const string Valid =
            "{\"floorReflection\":{\"minimumX\":-1.5,\"minimumZ\":-2.25,\"inverseWidth\":0.25,\"inverseDepth\":0.5,\"planeY\":0.005}}";

        [Test]
        public void ValidManifestParsesBoundsAndPlane()
        {
            Assert.IsTrue(TryParse(Valid, out Vector4 bounds, out float planeY));
            Assert.AreEqual(-1.5f, bounds.x, 1e-6f);
            Assert.AreEqual(-2.25f, bounds.y, 1e-6f);
            Assert.AreEqual(0.25f, bounds.z, 1e-6f);
            Assert.AreEqual(0.5f, bounds.w, 1e-6f);
            Assert.AreEqual(0.005f, planeY, 1e-6f);
        }

        [TestCase("minimumX")]
        [TestCase("minimumZ")]
        [TestCase("inverseWidth")]
        [TestCase("inverseDepth")]
        [TestCase("planeY")]
        public void MissingRequiredKeyFails(string key)
        {
            string json = Valid.Replace($"\"{key}\":", $"\"{key}Removed\":");
            Assert.IsFalse(TryParse(json, out _, out _));
        }

        [TestCase("null", "\"minimumX\":null,\"minimumZ\":-2,\"inverseWidth\":0.25,\"inverseDepth\":0.5,\"planeY\":0.005")]
        [TestCase("nonNumeric", "\"minimumX\":\"left\",\"minimumZ\":-2,\"inverseWidth\":0.25,\"inverseDepth\":0.5,\"planeY\":0.005")]
        [TestCase("zeroInverseWidth", "\"minimumX\":-1,\"minimumZ\":-2,\"inverseWidth\":0,\"inverseDepth\":0.5,\"planeY\":0.005")]
        [TestCase("negativeInverseDepth", "\"minimumX\":-1,\"minimumZ\":-2,\"inverseWidth\":0.25,\"inverseDepth\":-0.5,\"planeY\":0.005")]
        [TestCase("nanValue", "\"minimumX\":NaN,\"minimumZ\":-2,\"inverseWidth\":0.25,\"inverseDepth\":0.5,\"planeY\":0.005")]
        public void InvalidFieldFails(string label, string floor)
        {
            string json = "{\"floorReflection\":{" + floor + "}}";
            Assert.IsFalse(TryParse(json, out _, out _), label);
        }

        [Test]
        public void MissingFloorObjectFails()
        {
            Assert.IsFalse(TryParse("{\"exportedUtc\":\"now\"}", out _, out _));
        }

        private static bool TryParse(string json, out Vector4 bounds, out float planeY)
        {
            MethodInfo method = Assembly.Load("ChromaGLS")
                .GetType("ChromaGLS.Settings.FerrofluidGpuResources", throwOnError: true)!
                .GetMethod("TryParseFloorReflection", BindingFlags.Static | BindingFlags.NonPublic)!;
            object[] arguments = { json, default(Vector4), 0f };
            bool result = (bool)method.Invoke(null, arguments);
            bounds = (Vector4)arguments[1];
            planeY = (float)arguments[2];
            return result;
        }
    }
}
