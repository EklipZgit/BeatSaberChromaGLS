#if FERROFLUID_TWEAKS
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed partial class FerrofluidGpuSimulation
    {
        // Material.Get* can only read names declared in the shader's Properties block; uniforms set
        // through CGINCLUDE declarations exist only on the GPU side, so these lists contain exactly
        // the declared names (verified against FerrofluidSimulation.shader and
        // FerrofluidSurface.shader) and read their applied values directly.
        private static readonly string[] SimulationDeclaredFloatSnapshotProperties =
        {
            "_PrimitivePressureSpacing", "_PrimitiveHomeElasticityGain", "_PrimitiveHomeQuadraticGain",
            "_PrimitiveHomeSoftRange", "_PrimitiveHomeTravelLimit", "_PrimitiveHomeDampingGain",
            "_PrimitiveRestDensityReady",
        };

        private static readonly string[] SimulationDeclaredTextureSnapshotProperties =
        {
            "_StateTex", "_RestPositionTex", "_DensityPressureTex", "_ForceTex",
            "_CoarseCandidateTex", "_SiteBulgeTex",
        };

        private static readonly string[] SurfaceDeclaredFloatSnapshotProperties =
        {
            "_SpikesEnabled", "_SpikeSpringStrength", "_SpikeSpringDamping", "_Reflectivity",
            "_FadeAlpha", "_EmissionStrength", "_FerrofluidThinFilmStrength", "_ThinFilmAngleRate",
            "_Exposure", "_ThinFilmScale", "_ThinFilmOffset", "_SurfaceRoundness",
            "_SurfaceShapeMode", "_SpikeMoundCurvature", "_BackHeightFalloffExponent",
            "_MagnetHeightAmplification", "_MagnetHeightDeadZoneRadius", "_MagnetHeightStrengthCap",
            "_PrimitiveSpikeHeightRadius", "_PrimitiveSpikeTiltRadius",
        };

        private static readonly string[] SurfaceDeclaredVectorSnapshotProperties =
        {
            "_FerrofluidBaseColor", "_FerrofluidReflectionStrengths",
        };

        private static readonly string[] SurfaceDeclaredTextureSnapshotProperties =
        {
            "_EnvironmentTex", "_ThinFilmTex", "_HeightMapTex", "_RestPositionTex",
            "_DensityPressureTex", "_SpringStateTex", "_SurfaceNormalTex",
        };

        // Property id -> shader uniform name for every uniform this class applies through an
        // XxxId parameter. Literal "_Name" Set calls bypass this map and record directly.
        private static readonly Dictionary<int, string> SnapshotPropertyNames = new()
        {
            [Shader.PropertyToID("_StateTex")] = "_StateTex",
            [Shader.PropertyToID("_RestPositionTex")] = "_RestPositionTex",
            [Shader.PropertyToID("_DensityPressureTex")] = "_DensityPressureTex",
            [Shader.PropertyToID("_ForceTex")] = "_ForceTex",
            [Shader.PropertyToID("_HeightMapTex")] = "_HeightMapTex",
            [Shader.PropertyToID("_CoarseCandidateTex")] = "_CoarseCandidateTex",
            [Shader.PropertyToID("_CoarseCandidateSize")] = "_CoarseCandidateSize",
            [Shader.PropertyToID("_SpringStateTex")] = "_SpringStateTex",
            [Shader.PropertyToID("_SurfaceNormalTex")] = "_SurfaceNormalTex",
            [Shader.PropertyToID("_SurfaceNormalLogicalSize")] = "_SurfaceNormalLogicalSize",
            [Shader.PropertyToID("_SpikeSpringStrength")] = "_SpikeSpringStrength",
            [Shader.PropertyToID("_SpikeSpringDamping")] = "_SpikeSpringDamping",
            [Shader.PropertyToID("_Reflectivity")] = "_Reflectivity",
            [Shader.PropertyToID("_FerrofluidBaseColor")] = "_FerrofluidBaseColor",
            [Shader.PropertyToID("_FerrofluidReflectionStrengths")] = "_FerrofluidReflectionStrengths",
            [Shader.PropertyToID("_FerrofluidThinFilmStrength")] = "_FerrofluidThinFilmStrength",
            [Shader.PropertyToID("_ThinFilmAngleRate")] = "_ThinFilmAngleRate",
            [Shader.PropertyToID("_FadeAlpha")] = "_FadeAlpha",
            [Shader.PropertyToID("_FerrofluidObjectToWorld")] = "_FerrofluidObjectToWorld",
            [Shader.PropertyToID("_EnvironmentTex")] = "_EnvironmentTex",
            [Shader.PropertyToID("_ThinFilmTex")] = "_ThinFilmTex",
            [Shader.PropertyToID("_ThinFilmScale")] = "_ThinFilmScale",
            [Shader.PropertyToID("_ThinFilmOffset")] = "_ThinFilmOffset",
            [Shader.PropertyToID("_ParticleCount")] = "_ParticleCount",
            [Shader.PropertyToID("_DomainScale")] = "_DomainScale",
            [Shader.PropertyToID("_PointerPosition")] = "_PointerPosition",
            [Shader.PropertyToID("_PointerStrength")] = "_PointerStrength",
            [Shader.PropertyToID("_DeltaTimeMilliseconds")] = "_DeltaTimeMilliseconds",
            [Shader.PropertyToID("_PerimeterSegments")] = "_PerimeterSegments",
            [Shader.PropertyToID("_CornerRadius")] = "_CornerRadius",
            [Shader.PropertyToID("_RadialRadius")] = "_RadialRadius",
            [Shader.PropertyToID("_HeightFactor")] = "_HeightFactor",
            [Shader.PropertyToID("_RectCenter")] = "_RectCenter",
            [Shader.PropertyToID("_RectSize")] = "_RectSize",
            [Shader.PropertyToID("_RectMinY")] = "_RectMinY",
            [Shader.PropertyToID("_PivotY")] = "_PivotY",
            [Shader.PropertyToID("_Skew")] = "_Skew",
            [Shader.PropertyToID("_HalfExtents")] = "_HalfExtents",
            [Shader.PropertyToID("_DepthRadius")] = "_DepthRadius",
            [Shader.PropertyToID("_ViewerLocalZSign")] = "_ViewerLocalZSign",
            [Shader.PropertyToID("_RadialCenter")] = "_RadialCenter",
            [Shader.PropertyToID("_SourceLocalScale")] = "_SourceLocalScale",
            [Shader.PropertyToID("_LocalToCurvedCanvas")] = "_LocalToCurvedCanvas",
            [Shader.PropertyToID("_CurvedCanvasToLocal")] = "_CurvedCanvasToLocal",
            [Shader.PropertyToID("_CurvedCanvasRadius")] = "_CurvedCanvasRadius",
            [Shader.PropertyToID("_HasCurvedCanvas")] = "_HasCurvedCanvas",
            [Shader.PropertyToID("_MagneticFieldPosition")] = "_MagneticFieldPosition",
            [Shader.PropertyToID("_MagneticFieldStrength")] = "_MagneticFieldStrength",
            [Shader.PropertyToID("_SecondaryMagneticFieldPosition")] = "_SecondaryMagneticFieldPosition",
            [Shader.PropertyToID("_SecondaryMagneticFieldStrength")] = "_SecondaryMagneticFieldStrength",
            [Shader.PropertyToID("_PrimitiveMagnetPosition")] = "_PrimitiveMagnetPosition",
            [Shader.PropertyToID("_PrimitiveHandPosition")] = "_PrimitiveHandPosition",
            [Shader.PropertyToID("_SecondaryPointerPosition")] = "_SecondaryPointerPosition",
            [Shader.PropertyToID("_MagnetOpeningPosition")] = "_MagnetOpeningPosition",
            [Shader.PropertyToID("_MagnetOpeningNormal")] = "_MagnetOpeningNormal",
            [Shader.PropertyToID("_MagnetOpeningDirection")] = "_MagnetOpeningDirection",
            [Shader.PropertyToID("_MagnetOpeningStrength")] = "_MagnetOpeningStrength",
            [Shader.PropertyToID("_SurfaceShapeMode")] = "_SurfaceShapeMode",
            [Shader.PropertyToID("_SurfaceRoundness")] = "_SurfaceRoundness",
            [Shader.PropertyToID("_PrimitiveFrameRotation")] = "_PrimitiveFrameRotation",
            [Shader.PropertyToID("_PrimitiveLinearVelocityDelta")] = "_PrimitiveLinearVelocityDelta",
            [Shader.PropertyToID("_PrimitivePreviousAngularVelocity")] = "_PrimitivePreviousAngularVelocity",
            [Shader.PropertyToID("_PrimitiveAngularVelocity")] = "_PrimitiveAngularVelocity",
            [Shader.PropertyToID("_MagnetHeightAmplification")] = "_MagnetHeightAmplification",
            [Shader.PropertyToID("_ParticleTextureSize")] = "_ParticleTextureSize",
            [Shader.PropertyToID("_PrimitiveSpikeSize")] = "_PrimitiveSpikeSize",
            [Shader.PropertyToID("_PrimitiveTipRoundness")] = "_PrimitiveTipRoundness",
            [Shader.PropertyToID("_PrimitiveTipCapRounding")] = "_PrimitiveTipCapRounding",
            [Shader.PropertyToID("_PrimitiveSpikeSpacing")] = "_PrimitiveSpikeSpacing",
            [Shader.PropertyToID("_SpikeMoundCurvature")] = "_SpikeMoundCurvature",
            [Shader.PropertyToID("_PrimitiveSpikeHeightRadius")] = "_PrimitiveSpikeHeightRadius",
            [Shader.PropertyToID("_PrimitiveSpikeTiltRadius")] = "_PrimitiveSpikeTiltRadius",
            [Shader.PropertyToID("_PrimitiveFlattenRadius")] = "_PrimitiveFlattenRadius",
            [Shader.PropertyToID("_PrimitiveFlattenExponent")] = "_PrimitiveFlattenExponent",
            [Shader.PropertyToID("_PrimitiveGlobalSpikeFlatten")] = "_PrimitiveGlobalSpikeFlatten",
            [Shader.PropertyToID("_SphereBulgeScale")] = "_SphereBulgeScale",
            [Shader.PropertyToID("_SiteSurfaceTex")] = "_SiteSurfaceTex",
            [Shader.PropertyToID("_SiteDeformationTex")] = "_SiteDeformationTex",
            [Shader.PropertyToID("_SiteBulgeTex")] = "_SiteBulgeTex",
            [Shader.PropertyToID("_RestSurfaceTex")] = "_RestSurfaceTex",
            [Shader.PropertyToID("_PrimitiveYawCosSin")] = "_PrimitiveYawCosSin",
            [Shader.PropertyToID("_PrimitiveHomeElasticityGain")] = "_PrimitiveHomeElasticityGain",
            [Shader.PropertyToID("_PrimitiveHomeQuadraticGain")] = "_PrimitiveHomeQuadraticGain",
            [Shader.PropertyToID("_PrimitiveHomeSoftRange")] = "_PrimitiveHomeSoftRange",
            [Shader.PropertyToID("_PrimitiveHomeTravelLimit")] = "_PrimitiveHomeTravelLimit",
            [Shader.PropertyToID("_PrimitiveHomeDampingGain")] = "_PrimitiveHomeDampingGain",
            [Shader.PropertyToID("_PrimitiveRestDensityReady")] = "_PrimitiveRestDensityReady",
            [Shader.PropertyToID("_PrimitiveCellMetric")] = "_PrimitiveCellMetric",
            [Shader.PropertyToID("_BackHeightFalloffExponent")] = "_BackHeightFalloffExponent",
            [Shader.PropertyToID("_MagnetHeightStrengthCap")] = "_MagnetHeightStrengthCap",
            [Shader.PropertyToID("_MagnetHeightDeadZoneRadius")] = "_MagnetHeightDeadZoneRadius",
        };

        // Snapshot texture key -> the material property each live texture is bound to, so a pingpong
        // swap cannot silently scramble which buffer a replay should read.
        private static readonly (string Key, string Property, string Description)[] GpuTextureChannels =
        {
            ("_currentState", "_StateTex", "RGBA per particle: xy=chart position -1..1, zw=chart velocity units per second"),
            ("_restPositionTexture", "_RestPositionTex", "RGBA per particle: xy=home chart position -1..1, z=baked rest density, w=unused"),
            ("_springCurrent", "_SpringStateTex", "RGBA per particle: x=dimensionless spring height scale (multiplies spikeDirection*selectedHeight*_FluidHeightScale*(1-globalSpikeFlatten) in DistortSurface; not additive height), y=height scale velocity per second"),
            ("_heightMap", "_HeightMapTex", "RGBA per texel: x=smooth spike height * _HeightFactor, y=nearest site particle id, z=density support alpha, w=sharpened spike height * _HeightFactor"),
            ("_densityPressure", "_DensityPressureTex", "RGBA per particle: x=density, y=pressure, zw=unused"),
            ("_force", "_ForceTex", "RGBA per particle: xy=chart-space force (primitive: surface tangent force through FerrofluidTangentToChart(surfaceForce/scale); torus: chart tangent force), zw=unused"),
            ("_coarseCandidates", "_CoarseCandidateTex", "RGBA: torus=4 nearest particle ids per cell; primitive=3x2 atlas per cell, blocks 0-3 hold four ids each, block 4 x=16th distance bound y=minimum site spacing, block 5 padding"),
            ("_siteSurface", "_SiteSurfaceTex", "RGBA per particle: xyz=live site base surface position (object-local), w=site spacing"),
            ("_restSurface", "_RestSurfaceTex", "RGBA per particle: xyz=rest site base surface position (object-local), w=rest spacing"),
            ("_siteDeformation", "_SiteDeformationTex", "RGBA per particle: xyz=world-space spike direction (SpikeDeformation output), w=apex sharpening"),
            ("_siteBulge", "_SiteBulgeTex", "RGBA per particle: w=density bulge height in world units (alpha only)"),
            ("_surfaceNormalCache", "_SurfaceNormalTex", "RGBA physical 516x516 (512 logical + 2 texel gutter): rgb=world-space continuous surface normal, a=spike film alpha"),
        };

        private readonly Dictionary<string, SnapshotUniformValue> _simulationUniforms = new();
        private readonly Dictionary<string, SnapshotUniformValue> _surfaceUniforms = new();

        partial void RecordSnapshotFloat(Material material, int propertyId, float value)
            => RecordUniform(material, SnapshotPropertyNames[propertyId], new SnapshotUniformValue { Kind = 1, Float = value });

        partial void RecordSnapshotFloat(Material material, string propertyName, float value)
            => RecordUniform(material, propertyName, new SnapshotUniformValue { Kind = 1, Float = value });

        partial void RecordSnapshotInt(Material material, int propertyId, int value)
            => RecordUniform(material, SnapshotPropertyNames[propertyId], new SnapshotUniformValue { Kind = 2, Int = value });

        partial void RecordSnapshotInt(Material material, string propertyName, int value)
            => RecordUniform(material, propertyName, new SnapshotUniformValue { Kind = 2, Int = value });

        partial void RecordSnapshotVector(Material material, int propertyId, Vector4 value)
            => RecordUniform(material, SnapshotPropertyNames[propertyId], new SnapshotUniformValue { Kind = 3, Vector = value });

        partial void RecordSnapshotVector(Material material, string propertyName, Vector4 value)
            => RecordUniform(material, propertyName, new SnapshotUniformValue { Kind = 3, Vector = value });

        partial void RecordSnapshotMatrix(Material material, int propertyId, Matrix4x4 value)
            => RecordUniform(material, SnapshotPropertyNames[propertyId], new SnapshotUniformValue { Kind = 4, Matrix = value });

        partial void RecordSnapshotTexture(Material material, int propertyId, Texture? texture)
            => RecordUniform(material, SnapshotPropertyNames[propertyId], new SnapshotUniformValue { Kind = 5, Texture = texture });

        partial void RecordSnapshotTexture(Material material, string propertyName, Texture? texture)
            => RecordUniform(material, propertyName, new SnapshotUniformValue { Kind = 5, Texture = texture });

        private void RecordUniform(Material material, string name, in SnapshotUniformValue value)
        {
            Dictionary<string, SnapshotUniformValue> store = ReferenceEquals(material, _simulationMaterial)
                ? _simulationUniforms
                : _surfaceUniforms;
            store[name] = value;
        }

        // localToRenderedBasis applies the torus's skew/curved-canvas transform; primitives pass null
        // because their analytic local basis already is the rendered basis.
        internal JObject ExportSnapshot(
            FerrofluidTorusChartGeometry? torusChart,
            Func<Vector3, Vector3>? localToRenderedBasis = null)
        {
            var snapshot = new JObject
            {
                ["shape"] = _shape.ToString(),
                ["surfaceShapeMode"] = (float)_shape,
                ["particleCount"] = _particleCount,
                ["particleTextureSize"] = _particleTextureSize,
                ["fieldColumns"] = _fieldColumns,
                ["fieldRows"] = _fieldRows,
                ["spikeHeightScale"] = _spikeHeightScale,
                ["homeChartYaw"] = _homeChartYaw,
                ["lastFadeAlpha"] = _lastFadeAlpha,
                ["renderEnabled"] = _renderEnabled,
                ["config"] = FerrofluidSnapshotData.ConfigToJObject(_config),
                ["appliedUniforms"] = new JObject
                {
                    ["simulationMaterial"] = DumpAppliedUniforms(_simulationUniforms),
                    ["surfaceMaterial"] = DumpAppliedUniforms(_surfaceUniforms),
                },
                ["simulationMaterial"] = DumpMaterial(
                    _simulationMaterial,
                    SimulationDeclaredFloatSnapshotProperties,
                    null,
                    null,
                    SimulationDeclaredTextureSnapshotProperties),
                ["surfaceMaterial"] = DumpMaterial(
                    _surfaceMaterial,
                    SurfaceDeclaredFloatSnapshotProperties,
                    SurfaceDeclaredVectorSnapshotProperties,
                    null,
                    SurfaceDeclaredTextureSnapshotProperties,
                    _renderer),
                ["motion"] = new JObject
                {
                    ["simulationAccumulator"] = _simulationAccumulator,
                    ["lastSimulationAt"] = _lastSimulationAt,
                    ["primitiveHistoryValid"] = _primitiveHistoryValid,
                    ["previousPrimitiveRotation"] = _primitiveHistoryValid ? FerrofluidSnapshotData.ToToken(_previousPrimitiveRotation) : JValue.CreateNull(),
                    ["previousPrimitiveLinearVelocity"] = _primitiveHistoryValid ? FerrofluidSnapshotData.ToToken(_previousPrimitiveLinearVelocity) : JValue.CreateNull(),
                    ["previousPrimitiveAngularVelocity"] = _primitiveHistoryValid ? FerrofluidSnapshotData.ToToken(_previousPrimitiveAngularVelocity) : JValue.CreateNull(),
                    ["pendingPrimitiveLinearVelocity"] = _primitiveHistoryValid ? FerrofluidSnapshotData.ToToken(_pendingPrimitiveLinearVelocity) : JValue.CreateNull(),
                    ["pendingPrimitiveAngularVelocity"] = _primitiveHistoryValid ? FerrofluidSnapshotData.ToToken(_pendingPrimitiveAngularVelocity) : JValue.CreateNull(),
                },
            };

            var channelDescriptions = new JObject();
            foreach ((string key, _, string description) in GpuTextureChannels)
                channelDescriptions[key] = description;
            snapshot["textureChannelDescriptions"] = channelDescriptions;
            snapshot["textureLayout"] = "RGBA float, row-major, origin at bottom-left texel";

            // Each texture is read back once; the same pixels feed the raw dump and the spike decode.
            var textures = new JObject();
            var pixels = new Dictionary<string, Color[]>();
            foreach ((string key, string property, string description) in GpuTextureChannels)
            {
                Texture? texture = GpuTextureForKey(key);
                if (texture == null)
                    continue;
                Color[]? texturePixels = ReadPixelsOrNull(texture);
                if (texturePixels == null)
                {
                    throw new InvalidOperationException(
                        $"[ChromaGLS Ferrofluid Snapshot] required GPU texture readback failed for {key} ({texture.name}).");
                }

                pixels[key] = texturePixels;
                textures[key] = new JObject
                {
                    ["name"] = texture.name,
                    ["materialProperty"] = property,
                    ["description"] = description,
                    ["width"] = texture.width,
                    ["height"] = texture.height,
                    ["filterMode"] = texture.filterMode.ToString(),
                    ["wrapMode"] = texture.wrapMode.ToString(),
                    ["format"] = texture is Texture2D t2d ? t2d.format.ToString() : ((RenderTexture)texture).format.ToString(),
                    ["data"] = new JArray(FerrofluidSnapshotData.PixelsToFloats(texturePixels)),
                };
            }

            snapshot["textures"] = textures;

            Color[]? statePixels = ReadStoredPixels(pixels, "_currentState");
            JArray spikes = FerrofluidSnapshotData.DecodeSpikeList(
                _shape,
                _particleCount,
                statePixels,
                ReadStoredPixels(pixels, "_restPositionTexture"),
                ReadStoredPixels(pixels, "_springCurrent"),
                ReadStoredPixels(pixels, "_siteSurface"),
                ReadStoredPixels(pixels, "_siteDeformation"),
                ReadStoredPixels(pixels, "_siteBulge"),
                _shape == FerrofluidSurfaceShape.RoundedCube ? _surfaceMaterial.GetFloat(SurfaceRoundnessId) : 0.15f,
                _homeChartYaw,
                torusChart);
            if (torusChart.HasValue)
            {
                foreach (JObject spike in spikes)
                    spike["analyticBasis"] = "pre-skew/curved-canvas local chart";
            }

            MergeRenderedSites(spikes, statePixels, localToRenderedBasis);
            snapshot["spikes"] = spikes;
            snapshot["renderedSiteNote"] = "renderedSite* values are sampled at each site's current chart position; the rendered apex may sit between sites, so they are not the global spike maximum.";
            return snapshot;
        }

        // The pingpong binding can point at the live buffer, so each applied texture records which
        // raw dump key it actually matches by reference identity.
        private JObject DumpAppliedUniforms(Dictionary<string, SnapshotUniformValue> store)
        {
            var floats = new JObject();
            var ints = new JObject();
            var vectors = new JObject();
            var matrices = new JObject();
            var textures = new JObject();
            foreach (KeyValuePair<string, SnapshotUniformValue> entry in store)
            {
                SnapshotUniformValue value = entry.Value;
                switch (value.Kind)
                {
                    case 1:
                        floats[entry.Key] = value.Float;
                        break;
                    case 2:
                        ints[entry.Key] = value.Int;
                        break;
                    case 3:
                        vectors[entry.Key] = FerrofluidSnapshotData.ToToken(value.Vector);
                        break;
                    case 4:
                        matrices[entry.Key] = FerrofluidSnapshotMatrix4x4.From(value.Matrix).ToToken();
                        break;
                    case 5:
                        textures[entry.Key] = DumpTextureIdentity(value.Texture);
                        break;
                }
            }

            return new JObject
            {
                ["floats"] = floats,
                ["ints"] = ints,
                ["vectors"] = vectors,
                ["matrices"] = matrices,
                ["textures"] = textures,
            };
        }

        private JToken DumpTextureIdentity(Texture? texture)
        {
            return texture == null
                ? JValue.CreateNull()
                : new JObject
                {
                    ["name"] = texture.name,
                    ["instanceId"] = texture.GetInstanceID(),
                    ["width"] = texture.width,
                    ["height"] = texture.height,
                    ["filterMode"] = texture.filterMode.ToString(),
                    ["wrapMode"] = texture.wrapMode.ToString(),
                    ["snapshotTexture"] = SnapshotTextureKey(texture),
                };
        }

        private JToken SnapshotTextureKey(Texture texture)
        {
            if (ReferenceEquals(texture, _currentState))
                return "_currentState";
            if (ReferenceEquals(texture, _restPositionTexture))
                return "_restPositionTexture";
            if (ReferenceEquals(texture, _springCurrent))
                return "_springCurrent";
            if (ReferenceEquals(texture, _heightMap))
                return "_heightMap";
            if (ReferenceEquals(texture, _densityPressure))
                return "_densityPressure";
            if (ReferenceEquals(texture, _force))
                return "_force";
            if (ReferenceEquals(texture, _coarseCandidates))
                return "_coarseCandidates";
            if (ReferenceEquals(texture, _siteSurface))
                return "_siteSurface";
            if (ReferenceEquals(texture, _restSurface))
                return "_restSurface";
            if (ReferenceEquals(texture, _siteDeformation))
                return "_siteDeformation";
            if (ReferenceEquals(texture, _siteBulge))
                return "_siteBulge";
            if (ReferenceEquals(texture, _surfaceNormalCache))
                return "_surfaceNormalCache";
            return JValue.CreateNull();
        }

        private static Color[]? ReadStoredPixels(Dictionary<string, Color[]> pixels, string key)
        {
            return pixels.TryGetValue(key, out Color[]? stored) ? stored : null;
        }

        private Texture? GpuTextureForKey(string key)
        {
            return key switch
            {
                "_currentState" => _currentState,
                "_restPositionTexture" => _restPositionTexture,
                "_springCurrent" => _springCurrent,
                "_heightMap" => _heightMap,
                "_densityPressure" => _densityPressure,
                "_force" => _force,
                "_coarseCandidates" => _coarseCandidates,
                "_siteSurface" => _siteSurface,
                "_restSurface" => _restSurface,
                "_siteDeformation" => _siteDeformation,
                "_siteBulge" => _siteBulge,
                "_surfaceNormalCache" => _surfaceNormalCache,
                _ => null,
            };
        }

        // Draw one clip-space quad per site UV through the existing FERROFLUID_WORLD_POSITION pass:
        // output mode 0 is the deformed world position, mode 1 the deformed normal and spike film.
        // The live material must be used: a Material clone drops every runtime uniform value (rect
        // geometry, canvas matrices, texture bindings), and _ValidationOutput is a uniform the game
        // only ever leaves at zero, so restoring zero in finally preserves the live state.
        private void MergeRenderedSites(JArray spikes, Color[]? statePixels, Func<Vector3, Vector3>? localToRenderedBasis)
        {
            int pass = _surfaceMaterial.FindPass("FERROFLUID_WORLD_POSITION");
            if (pass < 0)
                throw new InvalidOperationException("[ChromaGLS Ferrofluid Snapshot] FERROFLUID_WORLD_POSITION pass missing.");

            int count = _particleCount;
            var samples = new Vector2[count];
            for (int index = 0; index < count; index++)
            {
                samples[index] = new Vector2(
                    (statePixels![index].r * 0.5f) + 0.5f,
                    (statePixels[index].g * 0.5f) + 0.5f);
            }

            Color[] positions = SampleWorldPositionPass(samples, pass, 0f);
            Color[] normals = SampleWorldPositionPass(samples, pass, 1f);

            Transform rendererTransform = _renderer.transform;
            for (int index = 0; index < spikes.Count; index++)
            {
                JObject spike = (JObject)spikes[index];
                Vector3 world = new(positions[index].r, positions[index].g, positions[index].b);
                Vector3 worldNormal = new(normals[index].r, normals[index].g, normals[index].b);
                bool sampled = positions[index].a > 0.5f
                    && !float.IsNaN(world.x) && !float.IsNaN(world.y) && !float.IsNaN(world.z)
                    && !float.IsNaN(worldNormal.x) && !float.IsNaN(worldNormal.y) && !float.IsNaN(worldNormal.z);
                spike["renderedSiteWorldPosition"] = sampled ? FerrofluidSnapshotData.ToToken(world) : JValue.CreateNull();
                spike["renderedSiteWorldNormal"] = sampled ? FerrofluidSnapshotData.ToToken(worldNormal) : JValue.CreateNull();
                spike["renderedSiteLocalPosition"] = sampled
                    ? FerrofluidSnapshotData.ToToken(rendererTransform.InverseTransformPoint(world))
                    : JValue.CreateNull();
                spike["renderedSiteLocalNormal"] = sampled
                    ? FerrofluidSnapshotData.ToToken(rendererTransform.InverseTransformDirection(worldNormal).normalized)
                    : JValue.CreateNull();

                Vector3? localBase = ReadVector(spike["localBasePosition"]);
                Vector3? localHome = ReadVector(spike["localHomePosition"]);
                if (!localBase.HasValue)
                {
                    spike["renderedBasisBaseWorldPosition"] = JValue.CreateNull();
                    spike["surfaceDisplacement"] = JValue.CreateNull();
                    spike["surfaceDisplacementHeightWorld"] = JValue.CreateNull();
                    continue;
                }

                Vector3 renderedBasisLocal = localToRenderedBasis != null
                    ? localToRenderedBasis(localBase.Value)
                    : localBase.Value;
                Vector3 baseWorld = rendererTransform.TransformPoint(renderedBasisLocal);
                if (localHome.HasValue)
                {
                    Vector3 renderedBasisHome = localToRenderedBasis != null
                        ? localToRenderedBasis(localHome.Value)
                        : localHome.Value;
                    spike["renderedBasisHomeWorldPosition"] = FerrofluidSnapshotData.ToToken(rendererTransform.TransformPoint(renderedBasisHome));
                }

                if (localToRenderedBasis != null)
                {
                    spike["renderedBasisBaseLocalPosition"] = FerrofluidSnapshotData.ToToken(renderedBasisLocal);
                    if (localHome.HasValue)
                        spike["renderedBasisHomeLocalPosition"] = FerrofluidSnapshotData.ToToken(localToRenderedBasis(localHome.Value));
                }

                spike["renderedBasisBaseWorldPosition"] = FerrofluidSnapshotData.ToToken(baseWorld);
                if (sampled)
                {
                    Vector3 displacement = world - baseWorld;
                    spike["surfaceDisplacement"] = FerrofluidSnapshotData.ToToken(displacement);
                    spike["surfaceDisplacementHeightWorld"] = displacement.magnitude;
                }
                else
                {
                    spike["surfaceDisplacement"] = JValue.CreateNull();
                    spike["surfaceDisplacementHeightWorld"] = JValue.CreateNull();
                }
            }
        }

        private static Vector3? ReadVector(JToken? token)
        {
            return token is JArray array && array.Count == 3
                ? new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>())
                : (Vector3?)null;
        }

        // One clip-space quad per sample UV through the existing validation pass; every resource is
        // owned and released inside this call so a failure mid-export cannot leak.
        private Color[] SampleWorldPositionPass(Vector2[] samples, int pass, float outputMode)
        {
            int count = samples.Length;
            RenderTexture target = RenderTexture.GetTemporary(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.filterMode = FilterMode.Point;
            Mesh quad = new();
            Texture2D? readback = null;
            RenderTexture previous = RenderTexture.active;
            _surfaceMaterial.SetFloat("_ValidationOutput", outputMode);
            try
            {
                Vector3[] vertices = new Vector3[4 * count];
                Vector2[] uvs = new Vector2[4 * count];
                int[] triangles = new int[6 * count];
                for (int index = 0; index < count; index++)
                {
                    float left = -1f + (2f * index / count);
                    float right = -1f + (2f * (index + 1) / count);
                    int baseIndex = index * 4;
                    vertices[baseIndex + 0] = new Vector3(left, -1f, 0f);
                    vertices[baseIndex + 1] = new Vector3(right, -1f, 0f);
                    vertices[baseIndex + 2] = new Vector3(left, 1f, 0f);
                    vertices[baseIndex + 3] = new Vector3(right, 1f, 0f);
                    uvs[baseIndex + 0] = samples[index];
                    uvs[baseIndex + 1] = samples[index];
                    uvs[baseIndex + 2] = samples[index];
                    uvs[baseIndex + 3] = samples[index];
                    int triIndex = index * 6;
                    triangles[triIndex + 0] = baseIndex + 0;
                    triangles[triIndex + 1] = baseIndex + 2;
                    triangles[triIndex + 2] = baseIndex + 1;
                    triangles[triIndex + 3] = baseIndex + 1;
                    triangles[triIndex + 4] = baseIndex + 2;
                    triangles[triIndex + 5] = baseIndex + 3;
                }

                quad.vertices = vertices;
                quad.uv = uvs;
                quad.triangles = triangles;
                RenderTexture.active = target;
                GL.Clear(true, true, new Color(-2f, -2f, -2f, -2f));
                if (!_surfaceMaterial.SetPass(pass))
                    throw new InvalidOperationException("[ChromaGLS Ferrofluid Snapshot] FERROFLUID_WORLD_POSITION pass could not be bound.");

                Graphics.DrawMeshNow(quad, _renderer.transform.localToWorldMatrix);
                readback = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true);
                readback.ReadPixels(new Rect(0f, 0f, count, 1), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                _surfaceMaterial.SetFloat("_ValidationOutput", 0f);
                if (readback != null)
                    UnityEngine.Object.Destroy(readback);
                UnityEngine.Object.DestroyImmediate(quad);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Color[]? ReadPixelsOrNull(Texture? texture)
        {
            if (texture == null)
                return null;
            try
            {
                if (texture is Texture2D texture2D)
                {
                    if (texture2D.isReadable)
                        return texture2D.GetPixels();

                    // Baked textures apply makeNoLongerReadable, so they come back through a
                    // temporary render target instead of CPU GetPixels.
                    RenderTexture previous2D = RenderTexture.active;
                    RenderTexture temp = RenderTexture.GetTemporary(texture2D.width, texture2D.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    Texture2D readback2D = new(texture2D.width, texture2D.height, TextureFormat.RGBAFloat, false, true);
                    try
                    {
                        Graphics.Blit(texture2D, temp);
                        RenderTexture.active = temp;
                        readback2D.ReadPixels(new Rect(0f, 0f, temp.width, temp.height), 0, 0);
                        readback2D.Apply();
                        return readback2D.GetPixels();
                    }
                    finally
                    {
                        RenderTexture.active = previous2D;
                        RenderTexture.ReleaseTemporary(temp);
                        UnityEngine.Object.Destroy(readback2D);
                    }
                }

                if (texture is RenderTexture renderTexture)
                {
                    RenderTexture previous = RenderTexture.active;
                    Texture2D readback = new(renderTexture.width, renderTexture.height, TextureFormat.RGBAFloat, false, true);
                    try
                    {
                        RenderTexture.active = renderTexture;
                        readback.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
                        readback.Apply();
                        return readback.GetPixels();
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        UnityEngine.Object.Destroy(readback);
                    }
                }
            }
            catch (Exception error)
            {
                Plugin.Log?.Error($"[ChromaGLS Ferrofluid Snapshot] texture readback failed for {texture.name}: {error}");
            }

            return null;
        }

        private static JObject DumpMaterial(
            Material material,
            string[] floatNames,
            string[]? vectorNames,
            string[]? matrixNames,
            string[] textureNames,
            Renderer? renderer = null)
        {
            var floats = new JObject();
            foreach (string name in floatNames)
                floats[name] = material.GetFloat(name);
            var vectors = new JObject();
            if (vectorNames != null)
            {
                foreach (string name in vectorNames)
                    vectors[name] = FerrofluidSnapshotData.ToToken(material.GetVector(name));
            }

            var matrices = new JObject();
            if (matrixNames != null)
            {
                foreach (string name in matrixNames)
                    matrices[name] = FerrofluidSnapshotMatrix4x4.From(material.GetMatrix(name)).ToToken();
            }

            var materialTextures = new JObject();
            foreach (string name in textureNames)
            {
                Texture? texture = material.GetTexture(name);
                materialTextures[name] = texture != null
                    ? new JObject
                    {
                        ["name"] = texture.name,
                        ["instanceId"] = texture.GetInstanceID(),
                        ["width"] = texture.width,
                        ["height"] = texture.height,
                        ["filterMode"] = texture.filterMode.ToString(),
                        ["wrapMode"] = texture.wrapMode.ToString(),
                    }
                    : JValue.CreateNull();
            }

            var declared = new JObject();
            Shader shader = material.shader;
            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int index = 0; index < count; index++)
                {
                    string propertyName = shader.GetPropertyName(index);
                    UnityEngine.Rendering.ShaderPropertyType type = shader.GetPropertyType(index);
                    declared[propertyName] = type switch
                    {
                        UnityEngine.Rendering.ShaderPropertyType.Color => (JToken)new JObject { ["type"] = "color", ["value"] = FerrofluidSnapshotData.ToToken((Vector4)material.GetColor(propertyName)) },
                        UnityEngine.Rendering.ShaderPropertyType.Vector => new JObject { ["type"] = "vector", ["value"] = FerrofluidSnapshotData.ToToken(material.GetVector(propertyName)) },
                        UnityEngine.Rendering.ShaderPropertyType.Float or UnityEngine.Rendering.ShaderPropertyType.Range => new JObject { ["type"] = "float", ["value"] = material.GetFloat(propertyName) },
#if !V1_29_1
                        UnityEngine.Rendering.ShaderPropertyType.Int => new JObject { ["type"] = "int", ["value"] = material.GetInt(propertyName) },
#endif
                        UnityEngine.Rendering.ShaderPropertyType.Texture => DeclaredTexture(material, propertyName),
                        _ => new JObject { ["type"] = type.ToString() },
                    };
                }
            }

            var passNames = new JArray();
            var passStates = new JObject();
            if (shader != null)
            {
                for (int pass = 0; pass < material.passCount; pass++)
                {
                    string passName = material.GetPassName(pass) ?? $"pass{pass}";
                    passNames.Add(passName);
                    passStates[passName] = material.GetShaderPassEnabled(passName);
                }
            }

            return new JObject
            {
                ["name"] = material.name,
                ["shader"] = material.shader != null ? material.shader.name : null,
                ["shaderKeywords"] = new JArray(material.shaderKeywords),
                ["enableInstancing"] = material.enableInstancing,
                ["renderQueue"] = material.renderQueue,
                ["maximumLOD"] = shader != null ? shader.maximumLOD : 0,
                ["shaderSupported"] = shader != null && shader.isSupported,
                ["passNames"] = passNames,
                ["passStates"] = passStates,
                ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
                ["hasPropertyBlock"] = renderer != null && renderer.HasPropertyBlock(),
                ["declaredProperties"] = declared,
                ["floats"] = floats,
                ["vectors"] = vectors,
                ["matrices"] = matrices,
                ["textures"] = materialTextures,
            };
        }

        private static JObject DeclaredTexture(Material material, string propertyName)
        {
            Texture? texture = material.GetTexture(propertyName);
            return new JObject
            {
                ["type"] = "texture",
                ["value"] = texture == null
                    ? JValue.CreateNull()
                    : new JObject
                    {
                        ["name"] = texture.name,
                        ["instanceId"] = texture.GetInstanceID(),
                        ["dimension"] = texture.dimension.ToString(),
                        ["width"] = texture.width,
                        ["height"] = texture.height,
                        ["scale"] = new JArray(material.GetTextureScale(propertyName).x, material.GetTextureScale(propertyName).y),
                        ["offset"] = new JArray(material.GetTextureOffset(propertyName).x, material.GetTextureOffset(propertyName).y),
                    },
            };
        }

        private struct SnapshotUniformValue
        {
            internal byte Kind;
            internal float Float;
            internal int Int;
            internal Vector4 Vector;
            internal Matrix4x4 Matrix;
            internal Texture? Texture;
        }
    }
}
#endif
