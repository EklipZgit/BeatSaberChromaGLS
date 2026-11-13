#if FERROFLUID_TWEAKS
using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    // Snapshot DTOs stay primitives-only so Json.NET can never reach a Unity type's recursive
    // computed properties (Quaternion.eulerAngles, Matrix4x4.inverse, and friends).
    internal struct FerrofluidSnapshotVector3
    {
        public float X;
        public float Y;
        public float Z;

        internal static FerrofluidSnapshotVector3 From(Vector3 vector)
            => new() { X = vector.x, Y = vector.y, Z = vector.z };

        internal JToken ToToken() => new JArray(X, Y, Z);
    }

    internal struct FerrofluidSnapshotQuaternion
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        internal static FerrofluidSnapshotQuaternion From(Quaternion quaternion)
            => new() { X = quaternion.x, Y = quaternion.y, Z = quaternion.z, W = quaternion.w };

        internal JToken ToToken() => new JArray(X, Y, Z, W);
    }

    internal struct FerrofluidSnapshotPose
    {
        public FerrofluidSnapshotVector3 Position;
        public FerrofluidSnapshotQuaternion Rotation;
        public FerrofluidSnapshotVector3 Euler;

        internal static FerrofluidSnapshotPose From(Vector3 position, Quaternion rotation)
            => new()
            {
                Position = FerrofluidSnapshotVector3.From(position),
                Rotation = FerrofluidSnapshotQuaternion.From(rotation),
                Euler = FerrofluidSnapshotVector3.From(rotation.eulerAngles),
            };

        internal JObject ToObject()
            => JObject.FromObject(this);
    }

    internal struct FerrofluidSnapshotMatrix4x4
    {
        // Row-major m00..m33 to match Unity's Matrix4x4.m{row}{column} naming.
        public float[]? M;

        internal static FerrofluidSnapshotMatrix4x4 From(Matrix4x4 matrix)
        {
            var values = new float[16];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                    values[(row * 4) + column] = matrix[row, column];
            }

            return new FerrofluidSnapshotMatrix4x4 { M = values };
        }

        internal JToken ToToken() => M != null ? new JArray(M) : JValue.CreateNull();
    }

    // The torus's own tube basis so a chart position maps to a readable object-local site.
    internal struct FerrofluidTorusChartGeometry
    {
        internal Vector2 Center;
        internal Vector2 HalfExtents;
        internal float CornerRadius;
        internal float ViewerLocalZSign;
        internal float DepthRadius;

        internal FerrofluidTorusChartGeometry(
            Vector2 center,
            Vector2 halfExtents,
            float cornerRadius,
            float viewerLocalZSign,
            float depthRadius)
        {
            Center = center;
            HalfExtents = halfExtents;
            CornerRadius = cornerRadius;
            ViewerLocalZSign = viewerLocalZSign;
            DepthRadius = depthRadius;
        }
    }

    internal static class FerrofluidSnapshotData
    {
        // Velocity below this is written as null so resting spikes stay readable.
        private const float ChartVelocityEpsilon = 0.000001f;

        // GPU pixels arrive from GetPixels as RGBA rows starting at the texture's bottom-left texel.
        internal static float[] PixelsToFloats(Color[] pixels)
        {
            var values = new float[pixels.Length * 4];
            for (int index = 0; index < pixels.Length; index++)
            {
                values[(index * 4) + 0] = pixels[index].r;
                values[(index * 4) + 1] = pixels[index].g;
                values[(index * 4) + 2] = pixels[index].b;
                values[(index * 4) + 3] = pixels[index].a;
            }

            return values;
        }

        // Config exposes only scalar persisted and session properties, so dump every public one.
        internal static JObject ConfigToJObject(object config)
        {
            var json = new JObject();
            foreach (PropertyInfo property in config.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead)
                    continue;
                Type type = property.PropertyType;
                if (type != typeof(float) && type != typeof(int) && type != typeof(bool))
                    continue;
                json[property.Name] = JToken.FromObject(property.GetValue(config));
            }

            return json;
        }

        internal static JArray DecodeSpikeList(
            FerrofluidSurfaceShape shape,
            int particleCount,
            Color[]? state,
            Color[]? rest,
            Color[]? spring,
            Color[]? siteSurface,
            Color[]? siteDeformation,
            Color[]? siteBulge,
            float roundness,
            float chartYawDegrees,
            FerrofluidTorusChartGeometry? torusChart)
        {
            var spikes = new JArray();
            for (int index = 0; index < particleCount; index++)
            {
                var spike = new JObject { ["index"] = index };
                Vector2? chart = ReadVector2(state, index);
                Vector2? velocity = ReadVelocity(state, index);
                Vector2? home = ReadVector2(rest, index);
                spike["chartPosition"] = chart.HasValue ? ToToken(chart.Value) : JValue.CreateNull();
                spike["chartVelocityPerSecond"] = velocity.HasValue && velocity.Value.magnitude > ChartVelocityEpsilon
                    ? ToToken(velocity.Value)
                    : JValue.CreateNull();
                spike["homeChartPosition"] = home.HasValue ? ToToken(home.Value) : JValue.CreateNull();
                spike["restDensity"] = ReadScalar(rest, index, c => c.b);

                // _springCurrent.x is a dimensionless scale applied to the spike displacement, not
                // an additive world height; use surfaceDisplacementHeightWorld for metres.
                spike["springHeightScale"] = ReadScalar(spring, index, c => c.r);
                spike["springHeightScaleVelocityPerSecond"] = ReadScalar(spring, index, c => c.g);

                Vector3? liveBase = ReadVector3(siteSurface, index);
                Vector3? basePosition = liveBase ?? (chart.HasValue
                    ? EvaluateBasePosition(shape, chart.Value, roundness, chartYawDegrees, torusChart)
                    : (Vector3?)null);
                Vector3? homePosition = home.HasValue
                    ? EvaluateBasePosition(shape, home.Value, roundness, chartYawDegrees, torusChart)
                    : (Vector3?)null;
                Vector3? normal = chart.HasValue
                    ? EvaluateNormal(shape, chart.Value, basePosition, roundness, chartYawDegrees, torusChart)
                    : (Vector3?)null;
                spike["localBasePosition"] = basePosition.HasValue ? ToToken(basePosition.Value) : JValue.CreateNull();
                spike["localHomePosition"] = homePosition.HasValue ? ToToken(homePosition.Value) : JValue.CreateNull();
                spike["localSurfaceNormal"] = normal.HasValue ? ToToken(normal.Value) : JValue.CreateNull();
                spike["spikeOrientation"] = normal.HasValue && normal.Value.sqrMagnitude > 0.000001f
                    ? ToToken(Quaternion.LookRotation(normal.Value))
                    : JValue.CreateNull();
                spike["siteSpacing"] = ReadScalar(siteSurface, index, c => c.a);

                Vector3? deformation = ReadVector3(siteDeformation, index);
                spike["deformationDirectionWorld"] = deformation.HasValue ? ToToken(deformation.Value) : JValue.CreateNull();
                spike["apexSharpening"] = ReadScalar(siteDeformation, index, c => c.a);
                spike["densityBulgeHeightWorld"] = ReadScalar(siteBulge, index, c => c.a);
                spikes.Add(spike);
            }

            return spikes;
        }

        private static Vector3? EvaluateBasePosition(
            FerrofluidSurfaceShape shape,
            Vector2 chart,
            float roundness,
            float chartYawDegrees,
            FerrofluidTorusChartGeometry? torusChart)
        {
            if (shape == FerrofluidSurfaceShape.Torus)
            {
                if (!torusChart.HasValue)
                    return null;
                Vector2 siteUv = (chart * 0.5f) + (Vector2.one * 0.5f);
                float crossSectionAngle = Mathf.Repeat(siteUv.y, 1f) * Mathf.PI * 2f;
                Vector2 center = FerrofluidGpuSimulation.EvaluateRoundedRectangleCenterline(
                    siteUv.x,
                    torusChart.Value.Center,
                    torusChart.Value.HalfExtents,
                    torusChart.Value.CornerRadius);
                Vector2 outward = FerrofluidGpuSimulation.EvaluateRoundedRectangleOutward(
                    center,
                    torusChart.Value.Center,
                    torusChart.Value.HalfExtents,
                    torusChart.Value.CornerRadius);
                float radial = FerrofluidGpuSimulation.RadialCenter
                    + (FerrofluidGpuSimulation.RadialRadius * Mathf.Cos(crossSectionAngle));
                return new Vector3(
                    center.x + (outward.x * radial),
                    center.y + (outward.y * radial),
                    torusChart.Value.ViewerLocalZSign * torusChart.Value.DepthRadius * Mathf.Sin(crossSectionAngle));
            }

            return FerrofluidEnvironmentObjects.EvaluateSiteSurfacePosition(shape, chart, roundness, chartYawDegrees);
        }

        private static Vector3? EvaluateNormal(
            FerrofluidSurfaceShape shape,
            Vector2 chart,
            Vector3? basePosition,
            float roundness,
            float chartYawDegrees,
            FerrofluidTorusChartGeometry? torusChart)
        {
            if (shape == FerrofluidSurfaceShape.Torus)
            {
                if (!torusChart.HasValue || !basePosition.HasValue)
                    return null;
                Vector2 siteUv = (chart * 0.5f) + (Vector2.one * 0.5f);
                float crossSectionAngle = Mathf.Repeat(siteUv.y, 1f) * Mathf.PI * 2f;
                Vector2 center = FerrofluidGpuSimulation.EvaluateRoundedRectangleCenterline(
                    siteUv.x,
                    torusChart.Value.Center,
                    torusChart.Value.HalfExtents,
                    torusChart.Value.CornerRadius);
                Vector2 outward = FerrofluidGpuSimulation.EvaluateRoundedRectangleOutward(
                    center,
                    torusChart.Value.Center,
                    torusChart.Value.HalfExtents,
                    torusChart.Value.CornerRadius);

                // TorusSurface's ellipse normal divides by the actual tube radii, so it is not the
                // radial unit vector unless the tube is circular.
                float radialWeight = Mathf.Cos(crossSectionAngle);
                float depthWeight = Mathf.Sin(crossSectionAngle);
                return new Vector3(
                    outward.x * radialWeight / FerrofluidGpuSimulation.RadialRadius,
                    outward.y * radialWeight / FerrofluidGpuSimulation.RadialRadius,
                    torusChart.Value.ViewerLocalZSign * depthWeight / torusChart.Value.DepthRadius).normalized;
            }

            if (!basePosition.HasValue)
                return null;
            return FerrofluidEnvironmentObjects.EvaluateSurfaceNormalFromDirection(shape, basePosition.Value, roundness);
        }

        private static Vector2? ReadVector2(Color[]? pixels, int index)
        {
            return pixels != null && index < pixels.Length
                ? new Vector2(pixels[index].r, pixels[index].g)
                : (Vector2?)null;
        }

        private static Vector2? ReadVelocity(Color[]? state, int index)
        {
            return state != null && index < state.Length
                ? new Vector2(state[index].b, state[index].a)
                : (Vector2?)null;
        }

        private static Vector3? ReadVector3(Color[]? pixels, int index)
        {
            return pixels != null && index < pixels.Length
                ? new Vector3(pixels[index].r, pixels[index].g, pixels[index].b)
                : (Vector3?)null;
        }

        private static JToken ReadScalar(Color[]? pixels, int index, Func<Color, float> channel)
        {
            return pixels != null && index < pixels.Length
                ? new JValue(channel(pixels[index]))
                : JValue.CreateNull();
        }

        internal static JToken ToToken(Vector2 vector) => new JArray(vector.x, vector.y);

        internal static JToken ToToken(Vector3 vector) => new JArray(vector.x, vector.y, vector.z);

        internal static JToken ToToken(Vector4 vector) => new JArray(vector.x, vector.y, vector.z, vector.w);

        internal static JToken ToToken(Quaternion quaternion) => new JArray(quaternion.x, quaternion.y, quaternion.z, quaternion.w);
    }
}
#endif
