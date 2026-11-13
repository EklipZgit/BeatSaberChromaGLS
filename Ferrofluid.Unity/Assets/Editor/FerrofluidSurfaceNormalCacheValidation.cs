#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Native regression for the primitive surface normal cache: the baked 512x512 ARGBHalf
    // cache must carry the exact continuous field normal + spike film, refresh every
    // presentation update, follow transform changes, and release with the simulation.
    internal static class FerrofluidSurfaceNormalCacheValidation
    {
        private const int LogicalSize = 512;
        private const float SourceWorldScale = 0.6111113f;
        private const string CacheKeyword = "FERROFLUID_CACHED_NORMALS";
        private const string CacheTextureName = "ChromaGLS Ferrofluid Surface Normals";
        private const string PositionTextureName = "ChromaGLS Ferrofluid Surface Positions";

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidSurfaceNormalCacheValidation));

        private static IEnumerator RunRuntime()
        {
            var failures = new List<string>();
            foreach (string shapeName in new[] { "RoundedCube", "Sphere" })
            {
                FerrofluidValidationRuntime? runtime = null;
                try
                {
                    object config = FerrofluidValidationRuntime.CreateConfig();
                    SetBool(config, "FerrofluidSpikesEnabled", true);
                    runtime = new FerrofluidValidationRuntime(shapeName, config);
                    var renderer = (MeshRenderer)runtime.Get("_renderer");
                    Transform body = renderer.transform;
                    body.localScale = Vector3.one * 0.55f;
                    body.SetPositionAndRotation(new Vector3(-0.4f, 0.5f, 0.8f), Quaternion.Euler(12f, 23f, 9f));
                    UpdateFrame(runtime, body, 1f);
                    yield return null;
                    Material surface = (Material)runtime.Get("_surfaceMaterial");
                    if (!surface.HasProperty("_SurfaceNormalTex") || !surface.HasProperty("_SurfacePositionTex"))
                    {
                        if (!surface.HasProperty("_SurfaceNormalTex"))
                            failures.Add($"{shapeName}: primitive surface does not expose the normal cache texture (_SurfaceNormalTex).");
                        if (!surface.HasProperty("_SurfacePositionTex"))
                            failures.Add($"{shapeName}: primitive surface does not expose the position cache texture (_SurfacePositionTex).");
                        continue;
                    }

                    RenderTexture? cache = surface.GetTexture("_SurfaceNormalTex") as RenderTexture;
                    RenderTexture? positions = surface.GetTexture("_SurfacePositionTex") as RenderTexture;
                    ValidateCacheBinding(shapeName, surface, cache, failures);
                    ValidatePositionBinding(shapeName, positions, failures);
                    if (cache != null)
                        ValidateCacheTexels(shapeName, runtime, surface, cache, failures);
                    if (positions != null)
                        ValidatePositionTexels(shapeName, runtime, surface, positions, failures);
                    if (cache != null && positions != null)
                    {
                        IEnumerator refresh = ValidatePresentationRefresh(shapeName, runtime, surface, cache, positions, body, failures);
                        while (refresh.MoveNext())
                            yield return null;
                        ValidateTransformRefresh(shapeName, runtime, surface, cache, positions, body, failures);
                    }
                }
                finally
                {
                    runtime?.Dispose();
                }
            }

            // The unmodified shader fails above; do not probe lifecycle paths on a missing interface.
            if (failures.Count != 0)
                throw new Exception($"[FerrofluidSurfaceNormalCacheValidation] {string.Join("; ", failures)}");

            IEnumerator cycles = ValidateEnabledCycles(failures);
            while (cycles.MoveNext())
                yield return null;
            IEnumerator dispose = ValidateDispose(failures);
            while (dispose.MoveNext())
                yield return null;

            if (failures.Count != 0)
                throw new Exception($"[FerrofluidSurfaceNormalCacheValidation] {string.Join("; ", failures)}");
            Debug.Log("[FerrofluidSurfaceNormalCacheValidation] PASS: normal cache binding, texels, refresh, lifecycle verified.");
        }

        private static void UpdateFrame(FerrofluidValidationRuntime runtime, Transform body, float heightAmplification)
            => UpdateFrame(runtime, body, body.TransformPoint(new Vector3(0.2f, 0.25f, 0.5f)), heightAmplification);

        private static void UpdateFrame(FerrofluidValidationRuntime runtime, Transform body, Vector3 magnetWorld, float heightAmplification)
        {
            runtime.Invoke(
                "UpdatePrimitiveFrame",
                SourceWorldScale,
                magnetWorld,
                body.TransformPoint(new Vector3(0.2f, 0.2f, 0.45f)),
                0.9f,
                new Vector3(0f, 0f, -1000f),
                0f,
                Vector3.zero,
                Vector3.zero,
                heightAmplification);
        }

        private static void ValidateCacheBinding(string shapeName, Material surface, RenderTexture? cache, List<string> failures)
        {
            if (cache == null)
            {
                failures.Add($"{shapeName}: _SurfaceNormalTex is not bound to a RenderTexture after the presentation update.");
                return;
            }
            if (cache.width != LogicalSize + 4 || cache.height != LogicalSize + 4)
                failures.Add($"{shapeName}: normal cache is {cache.width}x{cache.height}, expected {LogicalSize + 4} square.");
            if (cache.format != RenderTextureFormat.ARGBHalf || cache.sRGB)
                failures.Add($"{shapeName}: normal cache must be linear ARGBHalf, got {cache.format} sRGB={cache.sRGB}.");
            if (cache.filterMode != FilterMode.Bilinear || cache.wrapMode != TextureWrapMode.Clamp || cache.useMipMap)
                failures.Add($"{shapeName}: normal cache must be bilinear clamp with no mips.");
            if (cache.name != CacheTextureName)
                failures.Add($"{shapeName}: normal cache is named '{cache.name}', expected '{CacheTextureName}'.");
            if (!surface.IsKeywordEnabled(CacheKeyword))
                failures.Add($"{shapeName}: surface material must enable {CacheKeyword} once the cache exists.");
        }

        private static void ValidatePositionBinding(string shapeName, RenderTexture? positions, List<string> failures)
        {
            if (positions == null)
            {
                failures.Add($"{shapeName}: _SurfacePositionTex is not bound to a RenderTexture after the presentation update.");
                return;
            }
            if (positions.width != LogicalSize + 4 || positions.height != LogicalSize + 4)
                failures.Add($"{shapeName}: position cache is {positions.width}x{positions.height}, expected {LogicalSize + 4} square.");
            if (positions.format != RenderTextureFormat.ARGBFloat || positions.sRGB)
                failures.Add($"{shapeName}: position cache must be linear ARGBFloat, got {positions.format} sRGB={positions.sRGB}.");
            if (positions.filterMode != FilterMode.Bilinear || positions.wrapMode != TextureWrapMode.Clamp || positions.useMipMap)
                failures.Add($"{shapeName}: position cache must be bilinear clamp with no mips.");
            if (positions.name != PositionTextureName)
                failures.Add($"{shapeName}: position cache is named '{positions.name}', expected '{PositionTextureName}'.");
        }

        private static void ValidatePositionTexels(string shapeName, FerrofluidValidationRuntime runtime, Material surface, RenderTexture positions, List<string> failures)
        {
            (int i, int j)[] interior = { (53, 81), (256, 256), (453, 423) };
            foreach ((int i, int j) in interior)
                ComparePositionTexel(shapeName, runtime, surface, positions, i, j, QueryUv(i, j), failures);

            (int i, int j)[] padding = { (-1, 170), (512, 170), (170, -1), (170, 512) };
            foreach ((int i, int j) in padding)
            {
                Vector2 query = QueryUv(i, j);
                Vector2 canonical = Fold(runtime, query * 2f - Vector2.one) * 0.5f + new Vector2(0.5f, 0.5f);
                ComparePositionTexel(shapeName, runtime, surface, positions, i, j, canonical, failures);
            }
        }

        private static void ComparePositionTexel(string shapeName, FerrofluidValidationRuntime runtime, Material surface, RenderTexture positions, int i, int j, Vector2 expectedUv, List<string> failures)
        {
            var renderer = (MeshRenderer)runtime.Get("_renderer");
            Color expected = FerrofluidSurfaceProbe.Sample(surface, new[] { expectedUv }, 0, renderer.localToWorldMatrix)[0];
            Color actual = ReadTexel(positions, i + 2, j + 2);
            float distance = Vector3.Distance(new Vector3(actual.r, actual.g, actual.b), new Vector3(expected.r, expected.g, expected.b));
            if (!float.IsFinite(distance) || distance > 0.00002f)
                failures.Add($"{shapeName}: position texel ({i},{j}) {actual} differs from exact probe {expected} by {distance:F7}m.");
        }

        private static void ValidateCacheTexels(string shapeName, FerrofluidValidationRuntime runtime, Material surface, RenderTexture cache, List<string> failures)
        {
            (int i, int j)[] interior = { (53, 81), (256, 256), (453, 423) };
            foreach ((int i, int j) in interior)
                CompareCacheTexel(shapeName, runtime, surface, cache, i, j, QueryUv(i, j), failures);

            // Padding texels sit outside the chart; the canonical folded query must feed the bake.
            (int i, int j)[] padding = { (-1, 170), (512, 170), (170, -1), (170, 512) };
            foreach ((int i, int j) in padding)
            {
                Vector2 query = QueryUv(i, j);
                Vector2 canonical = Fold(runtime, query * 2f - Vector2.one) * 0.5f + new Vector2(0.5f, 0.5f);
                CompareCacheTexel(shapeName, runtime, surface, cache, i, j, canonical, failures);
            }
        }

        private static Vector2 QueryUv(int i, int j)
            => new((i + 0.5f) / LogicalSize, (j + 0.5f) / LogicalSize);

        private static Vector2 Fold(FerrofluidValidationRuntime runtime, Vector2 chartPosition)
        {
            MethodInfo fold = runtime.Simulation.GetType().GetMethod("FoldPrimitiveChartPosition", BindingFlags.Static | BindingFlags.NonPublic)!;
            return (Vector2)fold.Invoke(null, new object[] { chartPosition })!;
        }

        private static void CompareCacheTexel(string shapeName, FerrofluidValidationRuntime runtime, Material surface, RenderTexture cache, int i, int j, Vector2 expectedUv, List<string> failures)
        {
            var renderer = (MeshRenderer)runtime.Get("_renderer");
            Color expected = FerrofluidSurfaceProbe.Sample(surface, new[] { expectedUv }, 1, renderer.localToWorldMatrix)[0];
            Color actual = ReadTexel(cache, i + 2, j + 2);
            float normalDistance = Vector3.Distance(new Vector3(actual.r, actual.g, actual.b), new Vector3(expected.r, expected.g, expected.b));
            float filmDiff = Mathf.Abs(actual.a - expected.a);
            if (!float.IsFinite(normalDistance) || normalDistance > 0.002f)
                failures.Add($"{shapeName}: cache texel ({i},{j}) normal {actual} differs from exact probe {expected} by {normalDistance:F6}.");
            if (!float.IsFinite(filmDiff) || filmDiff > 0.002f)
                failures.Add($"{shapeName}: cache texel ({i},{j}) film {actual.a:F6} differs from exact {expected.a:F6} by {filmDiff:F6}.");
        }

        private static Color ReadTexel(RenderTexture source, int x, int y)
        {
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = source;
                readback.ReadPixels(new Rect(x, y, 1f, 1f), 0, 0);
                readback.Apply();
                return readback.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(readback);
            }
        }

        private static IEnumerator ValidatePresentationRefresh(string shapeName, FerrofluidValidationRuntime runtime, Material surface, RenderTexture cache, RenderTexture positions, Transform body, List<string> failures)
        {
            (int i, int j) = (256, 256);
            Vector2 query = QueryUv(i, j);
            var renderer = (MeshRenderer)runtime.Get("_renderer");
            Color before = ReadTexel(cache, i + 2, j + 2);
            Color positionsBefore = ReadTexel(positions, i + 2, j + 2);
            // Target the primary magnet at the sampled point's own surface position so the
            // refresh must alter the baked normal, not just the amplification constant.
            Color probePosition = FerrofluidSurfaceProbe.Sample(surface, new[] { query }, 0, renderer.localToWorldMatrix)[0];
            var magnetWorld = new Vector3(probePosition.r, probePosition.g, probePosition.b);
            // Force zero simulation ticks: only the presentation update may refresh the cache.
            runtime.Set("_lastSimulationAt", Time.unscaledTime);
            runtime.Set("_simulationAccumulator", 0f);
            UpdateFrame(runtime, body, magnetWorld, 1.6f);
            yield return null;
            if (!ReferenceEquals(surface.GetTexture("_SurfaceNormalTex"), cache))
                failures.Add($"{shapeName}: presentation refresh must reuse the same normal cache instance.");
            if (!ReferenceEquals(surface.GetTexture("_SurfacePositionTex"), positions))
                failures.Add($"{shapeName}: presentation refresh must reuse the same position cache instance.");
            Color after = ReadTexel(cache, i + 2, j + 2);
            Color positionsAfter = ReadTexel(positions, i + 2, j + 2);
            Color expected = FerrofluidSurfaceProbe.Sample(surface, new[] { query }, 1, renderer.localToWorldMatrix)[0];
            float normalDistance = Vector3.Distance(new Vector3(after.r, after.g, after.b), new Vector3(expected.r, expected.g, expected.b));
            if (!float.IsFinite(normalDistance) || normalDistance > 0.002f)
                failures.Add($"{shapeName}: refreshed cache normal {after} differs from exact probe {expected} by {normalDistance:F6}.");
            Color expectedPosition = FerrofluidSurfaceProbe.Sample(surface, new[] { query }, 0, renderer.localToWorldMatrix)[0];
            float positionDistance = Vector3.Distance(new Vector3(positionsAfter.r, positionsAfter.g, positionsAfter.b), new Vector3(expectedPosition.r, expectedPosition.g, expectedPosition.b));
            if (!float.IsFinite(positionDistance) || positionDistance > 0.00002f)
                failures.Add($"{shapeName}: refreshed position cache {positionsAfter} differs from exact probe {expectedPosition} by {positionDistance:F7}m.");
            float beforeAfter = Vector3.Distance(new Vector3(before.r, before.g, before.b), new Vector3(after.r, after.g, after.b))
                + Mathf.Abs(before.a - after.a);
            if (!float.IsFinite(beforeAfter) || beforeAfter <= 0.0001f)
                failures.Add($"{shapeName}: zero-tick presentation update left the cache unchanged at the magnet point (delta {beforeAfter:F6}).");
            float positionDelta = Vector3.Distance(new Vector3(positionsBefore.r, positionsBefore.g, positionsBefore.b), new Vector3(positionsAfter.r, positionsAfter.g, positionsAfter.b));
            if (!float.IsFinite(positionDelta) || positionDelta <= 0.0001f)
                failures.Add($"{shapeName}: zero-tick presentation update left the position cache unchanged at the magnet point (delta {positionDelta:F7}m).");
        }

        private static void ValidateTransformRefresh(string shapeName, FerrofluidValidationRuntime runtime, Material surface, RenderTexture cache, RenderTexture positions, Transform body, List<string> failures)
        {
            var renderer = (MeshRenderer)runtime.Get("_renderer");
            (int i, int j) = (256, 256);
            Vector2 query = QueryUv(i, j);
            Color positionsBefore = FerrofluidSurfaceProbe.Sample(surface, new[] { query }, 0, renderer.localToWorldMatrix)[0];
            Color[] stateBefore = FerrofluidValidationRuntime.Read((Texture)runtime.Get("_currentState"));
            Color[] springBefore = FerrofluidValidationRuntime.Read((Texture)runtime.Get("_springCurrent"));
            int activeCount = Math.Min(runtime.Count, stateBefore.Length);

            body.SetPositionAndRotation(body.position + new Vector3(0.2f, 0.1f, -0.3f), Quaternion.Euler(33f, 81f, -14f));
            body.localScale = Vector3.one * 0.7f;
            runtime.Set("_lastSimulationAt", Time.unscaledTime);
            runtime.Set("_simulationAccumulator", 0f);
            UpdateFrame(runtime, body, 1.6f);
            // RenderPrimitiveDeformation's Blit owns the active render target afterwards; the
            // cache bake must restore it rather than leave the cache bound.
            if (ReferenceEquals(RenderTexture.active, cache) || ReferenceEquals(RenderTexture.active, positions))
                failures.Add($"{shapeName}: cache refresh left a cache as the active render target.");

            // The direct render path must preserve whatever target was active on entry.
            var sentinel = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = sentinel;
                runtime.Invoke("RenderSurfaceNormals");
                if (!ReferenceEquals(RenderTexture.active, sentinel))
                    failures.Add($"{shapeName}: RenderSurfaceNormals did not preserve the active render target.");
            }
            finally
            {
                RenderTexture.active = previous;
                sentinel.Release();
                Object.DestroyImmediate(sentinel);
            }

            if (!ReferenceEquals(surface.GetTexture("_SurfaceNormalTex"), cache))
                failures.Add($"{shapeName}: transform refresh must reuse the same normal cache instance.");
            if (!ReferenceEquals(surface.GetTexture("_SurfacePositionTex"), positions))
                failures.Add($"{shapeName}: transform refresh must reuse the same position cache instance.");
            Color actual = ReadTexel(cache, i + 2, j + 2);
            Color expected = FerrofluidSurfaceProbe.Sample(surface, new[] { query }, 1, renderer.localToWorldMatrix)[0];
            float normalDistance = Vector3.Distance(new Vector3(actual.r, actual.g, actual.b), new Vector3(expected.r, expected.g, expected.b));
            if (!float.IsFinite(normalDistance) || normalDistance > 0.002f)
                failures.Add($"{shapeName}: cache after transform {actual} differs from exact probe {expected} by {normalDistance:F6}.");
            Color positionsActual = ReadTexel(positions, i + 2, j + 2);
            Color positionsExpected = FerrofluidSurfaceProbe.Sample(surface, new[] { query }, 0, renderer.localToWorldMatrix)[0];
            float positionsDistance = Vector3.Distance(new Vector3(positionsActual.r, positionsActual.g, positionsActual.b), new Vector3(positionsExpected.r, positionsExpected.g, positionsExpected.b));
            if (!float.IsFinite(positionsDistance) || positionsDistance > 0.00002f)
                failures.Add($"{shapeName}: position cache after transform {positionsActual} differs from exact probe {positionsExpected} by {positionsDistance:F7}m.");

            Color positionsAfter = FerrofluidSurfaceProbe.Sample(surface, new[] { query }, 0, renderer.localToWorldMatrix)[0];
            if (Vector3.Distance(new Vector3(positionsBefore.r, positionsBefore.g, positionsBefore.b), new Vector3(positionsAfter.r, positionsAfter.g, positionsAfter.b)) < 0.0001f)
                failures.Add($"{shapeName}: transform change did not move the sampled surface position; the cache check cannot distinguish bake state.");

            // The vertex normal skip must not alter geometry: positions identical with the keyword on and off.
            Vector2[] uvs = { query, QueryUv(53, 81), QueryUv(453, 423) };
            surface.DisableKeyword(CacheKeyword);
            Color[] positionsOff = FerrofluidSurfaceProbe.Sample(surface, uvs, 0, renderer.localToWorldMatrix);
            surface.EnableKeyword(CacheKeyword);
            Color[] positionsOn = FerrofluidSurfaceProbe.Sample(surface, uvs, 0, renderer.localToWorldMatrix);
            for (int sample = 0; sample < uvs.Length; sample++)
            {
                float positionDistance = Vector3.Distance(
                    new Vector3(positionsOff[sample].r, positionsOff[sample].g, positionsOff[sample].b),
                    new Vector3(positionsOn[sample].r, positionsOn[sample].g, positionsOn[sample].b));
                if (!float.IsFinite(positionDistance) || positionDistance > 0.000001f)
                    failures.Add($"{shapeName}: cached-normals keyword changed the sampled world position at uv {uvs[sample]} by {positionDistance:F7}.");
            }

            Color[] stateAfter = FerrofluidValidationRuntime.Read((Texture)runtime.Get("_currentState"));
            Color[] springAfter = FerrofluidValidationRuntime.Read((Texture)runtime.Get("_springCurrent"));
            for (int pixel = 0; pixel < activeCount; pixel++)
            {
                float stateDiff = Vector4.Distance(stateBefore[pixel], stateAfter[pixel]);
                if (!float.IsFinite(stateDiff) || stateDiff > 0.000001f)
                {
                    failures.Add($"{shapeName}: _currentState texel {pixel} changed during a zero-tick cache update.");
                    break;
                }
            }
            for (int pixel = 0; pixel < Math.Min(activeCount, springBefore.Length); pixel++)
            {
                float springDiff = Vector4.Distance(springBefore[pixel], springAfter[pixel]);
                if (!float.IsFinite(springDiff) || springDiff > 0.000001f)
                {
                    failures.Add($"{shapeName}: _springCurrent texel {pixel} changed during a zero-tick cache update.");
                    break;
                }
            }
        }

        private static IEnumerator ValidateEnabledCycles(List<string> failures)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            SetBool(config, "FerrofluidSpikesEnabled", false);
            using var runtime = new FerrofluidValidationRuntime("RoundedCube", config);
            var surface = (Material)runtime.Get("_surfaceMaterial");
            runtime.Invoke("ApplyEnabledState", config);
            if (surface.IsKeywordEnabled(CacheKeyword))
                failures.Add($"spikes disabled: surface material still enables {CacheKeyword}.");
            if (surface.GetTexture("_SurfaceNormalTex") is RenderTexture bound && bound.IsCreated())
                failures.Add("spikes disabled: normal cache RenderTexture is still bound and alive.");
            if (surface.GetTexture("_SurfacePositionTex") is RenderTexture boundPositions && boundPositions.IsCreated())
                failures.Add("spikes disabled: position cache RenderTexture is still bound and alive.");
            yield return null;

            for (int cycle = 0; cycle < 3; cycle++)
            {
                SetBool(config, "FerrofluidSpikesEnabled", true);
                runtime.Invoke("ApplyEnabledState", config);
                UpdateFrame(runtime, ((MeshRenderer)runtime.Get("_renderer")).transform, 1f);
                yield return null;
                var cache = surface.GetTexture("_SurfaceNormalTex") as RenderTexture;
                var positions = surface.GetTexture("_SurfacePositionTex") as RenderTexture;
                if (cache == null || !cache.IsCreated() || cache.width != LogicalSize + 4
                    || positions == null || !positions.IsCreated() || positions.width != LogicalSize + 4)
                {
                    failures.Add($"cycle {cycle}: spikes re-enabled did not recreate live 516x516 caches.");
                    continue;
                }
                if (!surface.IsKeywordEnabled(CacheKeyword))
                    failures.Add($"cycle {cycle}: re-enabled surface must set {CacheKeyword}.");
                SetBool(config, "FerrofluidSpikesEnabled", false);
                runtime.Invoke("ApplyEnabledState", config);
                if (cache.IsCreated() || positions.IsCreated())
                    failures.Add($"cycle {cycle}: disabling spikes must release both caches.");
                yield return null;
                if (cache != null || positions != null)
                    failures.Add($"cycle {cycle}: released caches should be destroyed by the next frame.");
            }

            object enabledConfig = FerrofluidValidationRuntime.CreateConfig();
            SetBool(enabledConfig, "FerrofluidSpikesEnabled", true);
            runtime.Invoke("ApplyEnabledState", enabledConfig);
            UpdateFrame(runtime, ((MeshRenderer)runtime.Get("_renderer")).transform, 1f);
            Texture? first = surface.GetTexture("_SurfaceNormalTex");
            Texture? firstPositions = surface.GetTexture("_SurfacePositionTex");
            runtime.Invoke("ApplyEnabledState", enabledConfig);
            if (!ReferenceEquals(first, surface.GetTexture("_SurfaceNormalTex")) || !ReferenceEquals(firstPositions, surface.GetTexture("_SurfacePositionTex")))
                failures.Add("repeated ApplyEnabledState with spikes enabled must not recreate the caches.");

            runtime.Invoke("DisableSpikes", enabledConfig);
            runtime.Invoke("ApplyEnabledState", enabledConfig);
            UpdateFrame(runtime, ((MeshRenderer)runtime.Get("_renderer")).transform, 1f);
            if (surface.GetTexture("_SurfaceNormalTex") is RenderTexture || surface.GetTexture("_SurfacePositionTex") is RenderTexture || surface.IsKeywordEnabled(CacheKeyword))
                failures.Add("permanent DisableSpikes must remove both caches and the keyword.");

            object torusConfig = FerrofluidValidationRuntime.CreateConfig();
            SetBool(torusConfig, "FerrofluidSpikesEnabled", true);
            using var torus = new FerrofluidValidationRuntime("Torus", torusConfig);
            var torusSurface = (Material)torus.Get("_surfaceMaterial");
            torus.Invoke("ApplyEnabledState", torusConfig);
            if (!torusSurface.HasProperty("_SurfaceNormalTex") || !torusSurface.HasProperty("_SurfacePositionTex"))
                failures.Add("torus surface must still expose both cache properties on the shared shader.");
            if (torusSurface.GetTexture("_SurfaceNormalTex") is RenderTexture || torusSurface.GetTexture("_SurfacePositionTex") is RenderTexture || torusSurface.IsKeywordEnabled(CacheKeyword))
                failures.Add("torus must never bind the caches or enable the keyword.");
        }

        private static IEnumerator ValidateDispose(List<string> failures)
        {
            int cachesBefore = CountNamedCaches();
            object config = FerrofluidValidationRuntime.CreateConfig();
            SetBool(config, "FerrofluidSpikesEnabled", true);
            var runtime = new FerrofluidValidationRuntime("RoundedCube", config);
            try
            {
                var body = ((MeshRenderer)runtime.Get("_renderer")).transform;
                body.localScale = Vector3.one * 0.55f;
                UpdateFrame(runtime, body, 1f);
                yield return null;
                var surface = (Material)runtime.Get("_surfaceMaterial");
                if (surface.GetTexture("_SurfaceNormalTex") is not RenderTexture cache || !cache.IsCreated()
                    || surface.GetTexture("_SurfacePositionTex") is not RenderTexture positions || !positions.IsCreated())
                {
                    failures.Add("dispose check needs live bound caches before Dispose.");
                    yield break;
                }
                runtime.Simulation.GetType().GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(runtime.Simulation, null);
                if (cache.IsCreated() || positions.IsCreated())
                    failures.Add("simulation Dispose must release both cache RenderTextures.");
                yield return null;
                if (cache != null || positions != null)
                    failures.Add("disposed caches should be destroyed by the next frame.");
                if (CountNamedCaches() > cachesBefore)
                    failures.Add($"named caches leaked: {cachesBefore} before Dispose, {CountNamedCaches()} after.");
            }
            finally
            {
                runtime.Dispose();
            }
        }

        private static int CountNamedCaches()
        {
            int count = 0;
            foreach (RenderTexture texture in Resources.FindObjectsOfTypeAll<RenderTexture>())
                if (texture.name == CacheTextureName || texture.name == PositionTextureName)
                    count++;
            return count;
        }

        private static void SetBool(object config, string name, bool value)
        {
            config.GetType().GetProperty(name)!.SetValue(config, value);
        }
    }
}
