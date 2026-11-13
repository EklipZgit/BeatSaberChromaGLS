using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Zenject;
#if PRE_V1_42_1
using FerroMaterial = UnityEngine.PhysicMaterial;
using FerroMaterialCombine = UnityEngine.PhysicMaterialCombine;
#else
using FerroMaterial = UnityEngine.PhysicsMaterial;
using FerroMaterialCombine = UnityEngine.PhysicsMaterialCombine;
#endif

namespace ChromaGLS.Settings
{
    internal enum FerrofluidSurfaceShape
    {
        Torus = 0,
        RoundedCube = 1,
        Sphere = 2,
    }

    // Binds the shared enclosure contact material to the ground-level PileOfNotes box colliders
    // so note impacts use the same restitution/friction pair as the enclosure. Original materials
    // (including null) are restored on teardown and only while the collider still uses ours.
    internal sealed class FerrofluidMenuNoteContacts
    {
        private readonly FerroMaterial _material;
        private readonly BoxCollider?[] _colliders;
        private readonly FerroMaterial?[] _originals;

        internal FerrofluidMenuNoteContacts(Transform environment, FerroMaterial material)
        {
            _material = material;
            Transform? pile = environment.Find("PileOfNotes");
            BoxCollider[] found = pile != null
                ? pile.GetComponentsInChildren<BoxCollider>(true)
                : System.Array.Empty<BoxCollider>();
            int count = 0;
            foreach (BoxCollider collider in found)
            {
                if (collider != null && !collider.isTrigger)
                    count++;
            }

            _colliders = new BoxCollider?[count];
            _originals = new FerroMaterial?[count];
            int index = 0;
            foreach (BoxCollider collider in found)
            {
                if (collider == null || collider.isTrigger)
                    continue;
                _colliders[index] = collider;
                _originals[index] = collider.sharedMaterial;
                index++;
            }
        }

        internal static FerrofluidMenuNoteContacts? Create(Scene scene, FerroMaterial material)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name != "DefaultMenuEnvironment")
                        continue;
                    return new FerrofluidMenuNoteContacts(candidate, material);
                }
            }

            return null;
        }

        internal void Apply()
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                    _colliders[i]!.sharedMaterial = _material;
            }
        }

        internal void Restore()
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null && _colliders[i]!.sharedMaterial == _material)
                    _colliders[i]!.sharedMaterial = _originals[i];
            }
        }
    }

    // The menu cube and sphere have separate toggles from the SongCore info-button ring.
    internal static class FerrofluidEnvironmentObjects
    {
        private static readonly Vector3 CubeScale = new(0.55f, 0.55f, 0.55f);

        private static readonly Vector3 SphereScale = new(0.55f, 0.55f, 0.55f);
        private static Config? _config;
        private static FerrofluidEnvironmentManager? _manager;
        private static bool _cubeEnabled;
        private static bool _sphereEnabled;
        private static bool _fluidCubeEnabled;
        private static bool _fluidSphereEnabled;
        private static bool _menuActive;

        // The floor carries the shared 0.15 Minimum half of every pair; cube faces layer an
        // owned Multiply material on top for their own tuned grip while bounce stays shared.
        internal static FerroMaterial CreateContactMaterial(bool floor)
            => new(floor ? "ChromaGLS Ferrofluid Floor Contact" : "ChromaGLS Ferrofluid Enclosure")
            {
                bounciness = FerrofluidGrabPhysics.FloorRestitution,
                dynamicFriction = floor ? FerrofluidGrabPhysics.FloorFriction : 0.6f,
                staticFriction = floor ? FerrofluidGrabPhysics.FloorFriction : 0.6f,
                bounceCombine = FerroMaterialCombine.Average,
                frictionCombine = floor ? FerroMaterialCombine.Minimum : FerroMaterialCombine.Average,
                hideFlags = HideFlags.DontSave,
            };

        internal static void BeginMenuSession()
        {
            _menuActive = true;
            Reconcile();
        }

        internal static void EndMenuSession()
        {
            _menuActive = false;
            if (_manager != null)
            {
                _manager.gameObject.SetActive(false);
                Object.Destroy(_manager.gameObject);
                _manager = null;
            }
        }

        internal static void SetCubeEnabled(bool enabled)
        {
            _cubeEnabled = enabled;
            Reconcile();
        }

        internal static void RunSpinTest(bool sphere, float rotationsPerSecond)
        {
            if (_manager != null)
                _manager.RunSpinTest(sphere, rotationsPerSecond);
        }

        internal static void StopSpinTests()
        {
            if (_manager != null)
                _manager.StopSpinTests();
        }

        internal static void SetSphereEnabled(bool enabled)
        {
            _sphereEnabled = enabled;
            Reconcile();
        }

        internal static void SetFluidCubeEnabled(bool enabled)
        {
            _fluidCubeEnabled = enabled;
            Reconcile();
        }

        internal static void SetFluidSphereEnabled(bool enabled)
        {
            _fluidSphereEnabled = enabled;
            Reconcile();
        }

        internal static void ApplyConfiguration(Config config)
        {
            _config = config;
            if (_manager != null)
            {
                _manager.ApplyConfiguration(config);
            }
        }

        internal static void EvaluatePlacement(
            FerrofluidSurfaceShape shape,
            out Vector3 localCenter,
            out Vector3 localScale)
        {
            if (shape == FerrofluidSurfaceShape.RoundedCube)
            {
                localScale = CubeScale;
                localCenter = new Vector3(-1f, localScale.y * 0.5f, 1f);
                return;
            }

            localScale = SphereScale;
            localCenter = new Vector3(1f, localScale.y * 0.5f, 1f);
        }

        // The extra pair parks behind the originals at z=0 inside the same enclosure.
        internal static void EvaluateFluidPlacement(
            FerrofluidSurfaceShape shape,
            out Vector3 localCenter,
            out Vector3 localScale)
        {
            EvaluatePlacement(shape, out localCenter, out localScale);
            localCenter.z = 0f;
        }

        internal static void ApplyFloorPlacement(
            Transform primitiveTransform,
            Transform menuPlayerAnchor,
            FerrofluidSurfaceShape shape)
        {
            primitiveTransform.SetParent(null, false);
            if (primitiveTransform.gameObject.scene != menuPlayerAnchor.gameObject.scene)
            {
                SceneManager.MoveGameObjectToScene(
                    primitiveTransform.gameObject,
                    menuPlayerAnchor.gameObject.scene);
            }

            EvaluatePlacement(shape, out Vector3 worldCenter, out Vector3 localScale);
            primitiveTransform.SetPositionAndRotation(worldCenter, Quaternion.identity);
            primitiveTransform.localScale = localScale;
        }

        // Chart yaw rotates the spike-layout seams. At zero, the four seams meet the cardinal directions.
        internal static float EvaluateChartYawDegrees(FerrofluidSurfaceShape shape, Config config)
        {
            if (shape == FerrofluidSurfaceShape.RoundedCube)
            {
                // Preserve the approved fixed chart orientation; changing re-encoding
                // is not needed to remove the tweak.
                return 45f;
            }

            return shape == FerrofluidSurfaceShape.Sphere
                ? Mathf.Clamp(config.FerrofluidSphereChartYaw, 0f, 90f)
                : 0f;
        }

        internal static float[] EvaluateChartCreaseAzimuths(float yawDegrees)
        {
            float[] azimuths = new float[4];
            for (int index = 0; index < azimuths.Length; index++)
            {
                azimuths[index] = Mathf.Repeat((index * 90f) + yawDegrees, 360f);
            }

            return azimuths;
        }

        internal static Vector3 EvaluateSurfacePosition(
            FerrofluidSurfaceShape shape,
            Vector2 uv,
            float roundness,
            float yawDegrees)
        {
            return EvaluateSurfacePositionFromDirection(
                shape,
                EvaluateSphereDirection(uv, yawDegrees),
                roundness);
        }

        // Use the same surface projection for spike spacing and magnet hits.
        internal static Vector3 EvaluateSurfacePositionFromDirection(
            FerrofluidSurfaceShape shape,
            Vector3 direction,
            float roundness)
        {
            Vector3 sphereDirection = direction.normalized;
            if (shape == FerrofluidSurfaceShape.Sphere)
            {
                return sphereDirection * 0.5f;
            }

            // A superellipsoid keeps spike positions and normals smooth across rounded cube edges.
            float exponent = EvaluateRoundedCubeExponent(roundness);
            float poweredX = Mathf.Pow(Mathf.Abs(sphereDirection.x), exponent);
            float poweredY = Mathf.Pow(Mathf.Abs(sphereDirection.y), exponent);
            float poweredZ = Mathf.Pow(Mathf.Abs(sphereDirection.z), exponent);
            float radialNorm = Mathf.Pow(poweredX + poweredY + poweredZ, 1f / exponent);
            return (sphereDirection / Mathf.Max(radialNorm, 0.000001f)) * 0.5f;
        }

        internal static Vector3 EvaluateSurfaceNormal(
            FerrofluidSurfaceShape shape,
            Vector2 uv,
            float roundness,
            float yawDegrees)
        {
            return EvaluateSurfaceNormalFromDirection(shape, EvaluateSphereDirection(uv, yawDegrees), roundness);
        }

        internal static Vector3 EvaluateSurfaceNormalFromDirection(
            FerrofluidSurfaceShape shape,
            Vector3 direction,
            float roundness)
        {
            EvaluateSurfacePositionAndNormal(shape, direction, roundness, out _, out Vector3 normal);
            return normal;
        }

        // Reuse the powered direction terms when calculating both position and normal during relaxation.
        internal static void EvaluateSurfacePositionAndNormal(
            FerrofluidSurfaceShape shape,
            Vector3 direction,
            float roundness,
            out Vector3 position,
            out Vector3 normal)
        {
            Vector3 sphereDirection = direction.normalized;
            if (shape == FerrofluidSurfaceShape.Sphere)
            {
                position = sphereDirection * 0.5f;
                normal = sphereDirection;
                return;
            }

            float exponent = EvaluateRoundedCubeExponent(roundness);
            float poweredX = Mathf.Pow(Mathf.Abs(sphereDirection.x), exponent);
            float poweredY = Mathf.Pow(Mathf.Abs(sphereDirection.y), exponent);
            float poweredZ = Mathf.Pow(Mathf.Abs(sphereDirection.z), exponent);
            float radialNorm = Mathf.Pow(poweredX + poweredY + poweredZ, 1f / exponent);
            position = (sphereDirection / Mathf.Max(radialNorm, 0.000001f)) * 0.5f;
            Vector3 rawNormal = new(
                Mathf.Abs(sphereDirection.x) > 0.000001f ? Mathf.Sign(sphereDirection.x) * (poweredX / Mathf.Abs(sphereDirection.x)) : 0f,
                Mathf.Abs(sphereDirection.y) > 0.000001f ? Mathf.Sign(sphereDirection.y) * (poweredY / Mathf.Abs(sphereDirection.y)) : 0f,
                Mathf.Abs(sphereDirection.z) > 0.000001f ? Mathf.Sign(sphereDirection.z) * (poweredZ / Mathf.Abs(sphereDirection.z)) : 0f);
            normal = rawNormal.sqrMagnitude < 0.000000001f
                ? sphereDirection
                : rawNormal.normalized;
        }

        // Invert the shader's rotated octahedral mapping so magnet hits select the cell on the touched surface.
        internal static Vector2 EvaluateSurfaceUv(Vector3 localDirection, float yawDegrees)
        {
            Vector3 world = localDirection.normalized;
            float yawRadians = yawDegrees * Mathf.Deg2Rad;
            float yawCos = Mathf.Cos(yawRadians);
            float yawSin = Mathf.Sin(yawRadians);
            Vector3 direction = new(
                (world.x * yawCos) - (world.z * yawSin),
                world.y,
                (world.x * yawSin) + (world.z * yawCos));
            float inverseL1 = 1f / Mathf.Max(
                Mathf.Abs(direction.x) + Mathf.Abs(direction.y) + Mathf.Abs(direction.z),
                0.000001f);
            Vector2 p = new(direction.x * inverseL1, direction.z * inverseL1);
            if (direction.y < 0f)
            {
                p = new Vector2(
                    (1f - Mathf.Abs(p.y)) * (p.x >= 0f ? 1f : -1f),
                    (1f - Mathf.Abs(p.x)) * (p.y >= 0f ? 1f : -1f));
            }

            return new Vector2(
                Mathf.Clamp01((p.x * 0.5f) + 0.5f),
                Mathf.Clamp01((p.y * 0.5f) + 0.5f));
        }

        // Match SITE_SURFACE, including each spike's nearest-neighbor spacing used to size its base.
        internal static Vector4[] EvaluatePrimitiveSites(
            FerrofluidSurfaceShape shape,
            Vector2[] restPositions,
            float roundness,
            float yawDegrees)
        {
            int count = restPositions.Length;
            Vector3[] positions = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                positions[index] = EvaluateSiteSurfacePosition(shape, restPositions[index], roundness, yawDegrees);
            }

            Vector4[] sites = new Vector4[count];
            if (shape == FerrofluidSurfaceShape.RoundedCube)
            {
                float sharedSpacing = EvaluateCubeSpikeSpacing(positions);
                for (int index = 0; index < count; index++)
                {
                    sites[index] = new Vector4(
                        positions[index].x,
                        positions[index].y,
                        positions[index].z,
                        sharedSpacing);
                }

                return sites;
            }

            for (int index = 0; index < count; index++)
            {
                float spacing = float.MaxValue;
                for (int neighbor = 0; neighbor < count; neighbor++)
                {
                    if (neighbor == index)
                    {
                        continue;
                    }

                    float distance = (positions[neighbor] - positions[index]).sqrMagnitude;
                    if (distance < spacing)
                    {
                        spacing = distance;
                    }
                }

                sites[index] = new Vector4(
                    positions[index].x,
                    positions[index].y,
                    positions[index].z,
                    Mathf.Max(Mathf.Sqrt(spacing), 0.0001f));
            }

            return sites;
        }

        internal static float EvaluateCubeSpikeSpacing(Vector3[] positions)
        {
            Vector3[] faceTargets =
            {
                Vector3.up * 0.5f,
                Vector3.down * 0.5f,
                Vector3.left * 0.5f,
                Vector3.right * 0.5f,
                Vector3.forward * 0.5f,
                Vector3.back * 0.5f,
            };
            float total = 0f;
            foreach (Vector3 target in faceTargets)
            {
                int selected = 0;
                float selectedDistance = float.MaxValue;
                for (int index = 0; index < positions.Length; index++)
                {
                    float distance = (positions[index] - target).sqrMagnitude;
                    if (distance < selectedDistance)
                    {
                        selectedDistance = distance;
                        selected = index;
                    }
                }

                float neighborDistance = float.MaxValue;
                for (int index = 0; index < positions.Length; index++)
                {
                    if (index == selected)
                    {
                        continue;
                    }

                    float distance = (positions[index] - positions[selected]).sqrMagnitude;
                    if (distance < neighborDistance)
                    {
                        neighborDistance = distance;
                    }
                }

                total += Mathf.Sqrt(neighborDistance);
            }

            return Mathf.Max(total / faceTargets.Length, 0.0001f);
        }

        internal static float EvaluatePrimitiveConeHeight(
            FerrofluidSurfaceShape shape,
            Vector3 surfacePoint,
            Vector4[] sites,
            Config config)
        {
            EvaluatePrimitiveConeProfiles(shape, surfacePoint, sites, config, out float smoothHeight, out _);
            return smoothHeight;
        }

        // Keep the smooth and sharpened height channels separate because the shader blends them per spike.
        internal static void EvaluatePrimitiveConeProfiles(
            FerrofluidSurfaceShape shape,
            Vector3 surfacePoint,
            Vector4[] sites,
            Config config,
            out float smoothHeight,
            out float sharpHeight)
        {
            float cellMetric = FerrofluidGpuSimulation.EvaluatePrimitiveCellMetric(config.FerrofluidPrimitiveSpikeSize);
            float spikeFactor = FerrofluidGpuSimulation.EvaluateSpikeShape(
                config.FerrofluidSpikeBaseSize,
                config.FerrofluidSpikeHeightScale).x;
            float tipRoundness = Mathf.Clamp(config.FerrofluidPrimitiveTipRoundness, 0f, 1f);
            float tipCapRounding = Mathf.Clamp01(config.FerrofluidPrimitiveTipCapRounding);
            float cutoff = FerrofluidGpuSimulation.EvaluateSpikeBaseCutoff(config.FerrofluidSpikeBaseCutoff);
            float resultDistance = 1f;
            float nearestDistance = 10000000000f;
            float secondNearestDistance = 10000000000f;
            foreach (Vector4 site in sites)
            {
                Vector3 sitePosition = new(site.x, site.y, site.z);
                float distanceToParticle = (Vector3.Distance(surfacePoint, sitePosition) / Mathf.Max(site.w, 0.0001f))
                    * cellMetric
                    * FerrofluidGpuSimulation.HeightMapScaleFactor;
                if (distanceToParticle < nearestDistance)
                {
                    secondNearestDistance = nearestDistance;
                    nearestDistance = distanceToParticle;
                }
                else if (distanceToParticle < secondNearestDistance)
                {
                    secondNearestDistance = distanceToParticle;
                }

                float blendT = Mathf.Clamp01(
                    (((resultDistance - distanceToParticle) / FerrofluidGpuSimulation.HeightMapSmoothFactor) + 1f) * 0.5f);
                float blend = blendT * blendT * (3f - (2f * blendT));
                resultDistance = Mathf.Lerp(resultDistance, distanceToParticle, blend)
                    - (blend * (1f - blend) * (FerrofluidGpuSimulation.HeightMapSmoothFactor / (1f + (3f * FerrofluidGpuSimulation.HeightMapSmoothFactor))));
            }

            resultDistance = Mathf.Clamp01(resultDistance * spikeFactor);

            if (tipCapRounding > 0f)
            {
                resultDistance = AlmostIdentity(resultDistance, 0.12f * tipCapRounding, 0.02f * tipCapRounding);
            }

            float coneHeight = 1f - resultDistance;
            coneHeight = coneHeight * coneHeight * (3f - (2f * coneHeight));
            float roundedHeight = Mathf.Sqrt(Mathf.Max(0f, 1f - (resultDistance * resultDistance)));
            float spikeHeight = Mathf.Lerp(coneHeight, roundedHeight, tipRoundness);
            float sharpConeHeight = Mathf.Lerp(
                Mathf.Clamp01((1f - resultDistance) / Mathf.Lerp(1f, 0.95f, tipCapRounding)),
                roundedHeight,
                tipRoundness);
            float boundaryVisibility = cutoff > 0f
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((secondNearestDistance - nearestDistance) / cutoff))
                : 1f;
            smoothHeight = spikeHeight * boundaryVisibility;
            sharpHeight = sharpConeHeight * boundaryVisibility;
        }

        // Convert primitive world height using the shader's 0.9-unit prototype width.
        internal static float EvaluateSourceWorldScale(Transform primitiveTransform)
        {
            return Mathf.Max(primitiveTransform.lossyScale.y, 0.0001f) / 0.9f;
        }

        internal static float EvaluatePrimitiveTargetStrength(
            float fieldSurfaceDistance,
            float surfaceMissDistance,
            float sourceWorldScale)
        {
            float normalizedDistance = fieldSurfaceDistance
                / Mathf.Max(0.3f * sourceWorldScale, 0.0001f);
            float distanceStrength = 1f / (1f + (normalizedDistance * normalizedDistance));
            float normalizedMiss = Mathf.Clamp01(Mathf.InverseLerp(
                0.12f * sourceWorldScale,
                0.35f * sourceWorldScale,
                Mathf.Max(surfaceMissDistance, 0f)));
            float proximityWeight = 1f - Mathf.SmoothStep(0f, 1f, normalizedMiss);
            return distanceStrength * proximityWeight;
        }

        internal static float EvaluatePrimitiveSurfaceGap(Config config)
        {
            return Mathf.Clamp(config.MagnetForwardDistance, 0f, 0.6f);
        }

        internal static float EvaluateRoundness(FerrofluidSurfaceShape shape, Config config)
        {
            return shape == FerrofluidSurfaceShape.RoundedCube
                ? Mathf.Clamp01(config.FerrofluidCubeRoundness)
                : 0.2f;
        }

        private static float EvaluateRoundedCubeExponent(float roundness)
        {
            return Mathf.Lerp(8f, 2f, Mathf.Clamp01(roundness));
        }

        // Match the shader's octahedral layout, with the bottom pole shared by the four UV corners.
        private static Vector3 EvaluateSphereDirection(Vector2 uv, float yawDegrees)
        {
            Vector2 p = new(
                (Mathf.Clamp01(uv.x) * 2f) - 1f,
                (Mathf.Clamp01(uv.y) * 2f) - 1f);
            Vector3 direction = new(p.x, 1f - Mathf.Abs(p.x) - Mathf.Abs(p.y), p.y);
            float fold = Mathf.Clamp01(-direction.y);
            direction.x += direction.x >= 0f ? -fold : fold;
            direction.z += direction.z >= 0f ? -fold : fold;
            Vector3 normalized = direction.normalized;
            float yawRadians = yawDegrees * Mathf.Deg2Rad;
            float yawCos = Mathf.Cos(yawRadians);
            float yawSin = Mathf.Sin(yawRadians);
            return new Vector3(
                (normalized.x * yawCos) + (normalized.z * yawSin),
                normalized.y,
                (normalized.z * yawCos) - (normalized.x * yawSin));
        }

        internal static Vector3 EvaluateSiteSurfacePosition(
            FerrofluidSurfaceShape shape,
            Vector2 restPosition,
            float roundness,
            float yawDegrees)
        {
            return EvaluateSurfacePosition(
                shape,
                (restPosition * 0.5f) + (Vector2.one * 0.5f),
                roundness,
                yawDegrees);
        }

        private static float AlmostIdentity(float x, float m, float n)
        {
            if (x > m)
            {
                return x;
            }

            float a = (2f * n) - m;
            float b = (2f * m) - (3f * n);
            float t = x / m;
            return (((a * t) + b) * t * t) + n;
        }

        // The menu container owns this manager. App-level preferences must not start object discovery in gameplay.
        private static void Reconcile()
        {
            if (!_menuActive)
                return;

            if ((_cubeEnabled || _sphereEnabled || _fluidCubeEnabled || _fluidSphereEnabled) && _config != null && _manager == null)
            {
                Transform? playerAnchor = FerrofluidMagnetController.ResolvePlayerAnchor();
                if (playerAnchor == null)
                    return;

                GameObject managerObject = new("ChromaGLS Ferrofluid Environment");
                managerObject.SetActive(false);
                managerObject.transform.SetParent(playerAnchor, false);
                _manager = managerObject.AddComponent<FerrofluidEnvironmentManager>();
                _manager.Initialize(playerAnchor);
                managerObject.SetActive(true);
            }

            if (_manager != null)
            {
                _manager.Reconcile(_cubeEnabled, _sphereEnabled, _fluidCubeEnabled, _fluidSphereEnabled, _config);
            }
        }

        // Grab arbitration must run after VRPointer's LateUpdate hand selection, or a binary trigger
        // pull would be consumed before the pointer reports its newly selected hand.
        [DefaultExecutionOrder(1000)]
        private sealed class FerrofluidEnvironmentManager : MonoBehaviour
        {
            private Transform _playerAnchor = null!;
            private FerrofluidPrimitiveEffect? _cube;
            private FerrofluidPrimitiveEffect? _sphere;
            private FerrofluidPrimitiveEffect? _fluidCube;
            private FerrofluidPrimitiveEffect? _fluidSphere;
            private bool _cubeCreationFailed;
            private bool _sphereCreationFailed;
            private bool _fluidCubeCreationFailed;
            private bool _fluidSphereCreationFailed;
            private bool _enclosureResolved;
            private bool _enclosureFailedLogged;
            private FerrofluidGrabEnclosure _enclosure;
            private FerroMaterial? _enclosureMaterial;
            private Vector3 _shadowLightDirection = new Vector3(0f, 1f, 1f).normalized;
            private FerroMaterial? _floorMaterial;
            private Collider? _floorCollider;
            private Transform? _headTransform;
            private FerrofluidCameraCollision? _cameraCollision;
            private FerrofluidMagnetController? _grabInput;
            private FerrofluidGrabOwnership? _grabOwnership;
            private FerrofluidMenuNoteContacts? _menuNoteContacts;
            private readonly FerrofluidTriggerTracker _triggerTracker = new();

            internal void Initialize(Transform playerAnchor)
            {
                _playerAnchor = playerAnchor;
                MenuPlayerController? menuPlayerController = playerAnchor.GetComponent<MenuPlayerController>();
                _headTransform = menuPlayerController != null ? menuPlayerController._headTransform : null;
                TryResolveEnclosure();
            }

            internal void Reconcile(
                bool cubeEnabled,
                bool sphereEnabled,
                bool fluidCubeEnabled,
                bool fluidSphereEnabled,
                Config? config)
            {
                // Retry failed GPU creation only after a toggle or a new menu session.
                if (config == null || (!cubeEnabled && !sphereEnabled && !fluidCubeEnabled && !fluidSphereEnabled))
                {
                    DestroyPrimitive(ref _cube);
                    DestroyPrimitive(ref _sphere);
                    DestroyPrimitive(ref _fluidCube);
                    DestroyPrimitive(ref _fluidSphere);
                    _cubeCreationFailed = false;
                    _sphereCreationFailed = false;
                    _fluidCubeCreationFailed = false;
                    _fluidSphereCreationFailed = false;
                    return;
                }

                if (!_playerAnchor.gameObject.activeInHierarchy)
                {
                    return;
                }

                ReconcilePrimitive(ref _cube, ref _cubeCreationFailed, cubeEnabled, FerrofluidSurfaceShape.RoundedCube, fluid: false, config);
                ReconcilePrimitive(ref _sphere, ref _sphereCreationFailed, sphereEnabled, FerrofluidSurfaceShape.Sphere, fluid: false, config);
                ReconcilePrimitive(ref _fluidCube, ref _fluidCubeCreationFailed, fluidCubeEnabled, FerrofluidSurfaceShape.RoundedCube, fluid: true, config);
                ReconcilePrimitive(ref _fluidSphere, ref _fluidSphereCreationFailed, fluidSphereEnabled, FerrofluidSurfaceShape.Sphere, fluid: true, config);

                // A later Reconcile can resolve the enclosure; attach grabbables deferred until then.
                if (_enclosureResolved)
                {
                    AttachDeferred(_cube, isSphere: false, config);
                    AttachDeferred(_sphere, isSphere: true, config);
                    AttachDeferred(_fluidCube, isSphere: false, config);
                    AttachDeferred(_fluidSphere, isSphere: true, config);
                }
            }

            internal void ApplyConfiguration(Config config)
            {
                _cube?.ApplyConfiguration(config);
                _sphere?.ApplyConfiguration(config);
                _fluidCube?.ApplyConfiguration(config);
                _fluidSphere?.ApplyConfiguration(config);
            }

            private void LateUpdate()
            {
                UpdateGrabInteraction();
            }

            private void UpdateGrabInteraction()
            {
                if (!_enclosureResolved || _grabInput == null || _grabOwnership == null)
                    return;

                FerrofluidGrabbable? hovered = null;
                VRController? controller = null;
                float trigger = 0f;
                Vector3 grabPoint = Vector3.zero;
                if (_grabInput.TryGetPointerState(out VRController selected, out float triggerValue, out RaycastResult hovering))
                {
                    controller = selected;
                    trigger = triggerValue;
                    hovered = ResolveHovered(hovering.gameObject);
                    grabPoint = hovering.worldPosition;
                }

                _grabInput.SuppressGrabCursor(hovered != null);

                // Each hand's crossing is judged on its own previous sample, so a binary pull is
                // never lost when the pointer switches hands on the same frame.
                _grabInput.TryGetHandTriggers(out float leftTrigger, out float rightTrigger);
                _triggerTracker.Update(
                    leftTrigger,
                    rightTrigger,
                    out bool leftCrossing,
                    out bool rightCrossing);
                if (EvaluateGrabDecision(
                        _grabOwnership,
                        controller,
                        controller != null && ReferenceEquals(controller, _grabInput.LeftController),
                        leftCrossing,
                        rightCrossing,
                        hovered))
                    hovered!.Acquire(FerrofluidMagnetController.GetAttachTransform(controller!), grabPoint);

                ApplyMagnetHeightAmplification(_cube != null ? _cube.Grabbable : null, hovered, controller, trigger);
                ApplyMagnetHeightAmplification(_sphere != null ? _sphere.Grabbable : null, hovered, controller, trigger);
                ApplyMagnetHeightAmplification(_fluidCube != null ? _fluidCube.Grabbable : null, hovered, controller, trigger);
                ApplyMagnetHeightAmplification(_fluidSphere != null ? _fluidSphere.Grabbable : null, hovered, controller, trigger);
                ReleaseDroppedGrip(_cube != null ? _cube.Grabbable : null);
                ReleaseDroppedGrip(_sphere != null ? _sphere.Grabbable : null);
                ReleaseDroppedGrip(_fluidCube != null ? _fluidCube.Grabbable : null);
                ReleaseDroppedGrip(_fluidSphere != null ? _fluidSphere.Grabbable : null);
                Vector3 playerRight = _grabInput.EvaluatePlayerRight();
                ApplyControllerInput(_cube != null ? _cube.Grabbable : null, hovered, controller, playerRight);
                ApplyControllerInput(_sphere != null ? _sphere.Grabbable : null, hovered, controller, playerRight);
                ApplyControllerInput(_fluidCube != null ? _fluidCube.Grabbable : null, hovered, controller, playerRight);
                ApplyControllerInput(_fluidSphere != null ? _fluidSphere.Grabbable : null, hovered, controller, playerRight);
                if (_cube != null && _cube.Grabbable != null)
                    _cube.Grabbable.SampleGrabTarget(Time.unscaledDeltaTime, Time.unscaledTime);

                if (_sphere != null && _sphere.Grabbable != null)
                    _sphere.Grabbable.SampleGrabTarget(Time.unscaledDeltaTime, Time.unscaledTime);
                if (_fluidCube != null && _fluidCube.Grabbable != null)
                    _fluidCube.Grabbable.SampleGrabTarget(Time.unscaledDeltaTime, Time.unscaledTime);
                if (_fluidSphere != null && _fluidSphere.Grabbable != null)
                    _fluidSphere.Grabbable.SampleGrabTarget(Time.unscaledDeltaTime, Time.unscaledTime);
            }

            // The acquisition decision a grab frame makes once the pointer's currently selected hand
            // is known; separated so native validation can drive the real arbitration path.
            internal static bool EvaluateGrabDecision(
                FerrofluidGrabOwnership ownership,
                object? selectedHand,
                bool selectedIsLeft,
                bool leftCrossing,
                bool rightCrossing,
                FerrofluidGrabbable? hovered)
                => selectedHand != null
                    && (selectedIsLeft ? leftCrossing : rightCrossing)
                    && hovered != null
                    && !hovered.IsHeld
                    && ownership.TryAcquire(selectedHand, hovered);

            private void ApplyMagnetHeightAmplification(
                FerrofluidGrabbable? grabbable,
                FerrofluidGrabbable? hovered,
                VRController? controller,
                float trigger)
            {
                if (grabbable == null)
                    return;

                if (_grabOwnership!.OwnerOf(grabbable) is VRController holder)
                {
                    grabbable.SetMagnetHeightAmplification(FerrofluidGrabPhysics.EvaluateMagnetHeightAmplification(holder.triggerValue));
                    return;
                }

                grabbable.SetMagnetHeightAmplification(
                    ReferenceEquals(grabbable, hovered) && controller != null
                        ? FerrofluidGrabPhysics.EvaluateMagnetHeightAmplification(trigger)
                        : 1f);
            }

            private void ApplyControllerInput(
                FerrofluidGrabbable? grabbable,
                FerrofluidGrabbable? hovered,
                VRController? selected,
                Vector3 playerRight)
            {
                if (grabbable == null)
                    return;

                Vector2 forceStick = Vector2.zero;
                Vector2 spinStick = Vector2.zero;
                Quaternion pointerRotation = Quaternion.identity;
                FerrofluidGrabPhysics.RouteControllerInput(
                    _grabOwnership!.OwnerOf(grabbable),
                    selected,
                    ReferenceEquals(grabbable, hovered),
                    _grabInput!.LeftController,
                    _grabInput.RightController,
                    out object? dominantHand,
                    out object? nonDominantHand);
                if (dominantHand is VRController dominant)
                {
                    forceStick = _grabInput.SampleForceInput(dominant);
                    pointerRotation = FerrofluidMagnetController.GetAttachTransform(dominant).rotation;
                }

                if (nonDominantHand is VRController nonDominant)
                    spinStick = _grabInput.SampleSpinInput(nonDominant);

                grabbable.SetControllerInput(forceStick, spinStick, pointerRotation, playerRight);
                grabbable.SetGrabDepthInput(
                    dominantHand is VRController holder && grabbable.IsHeld
                        ? _grabInput.SampleGrabDepthInput(holder)
                        : 0);
            }

            private void ReleaseDroppedGrip(FerrofluidGrabbable? grabbable)
            {
                if (grabbable == null)
                    return;

                if (_grabOwnership!.OwnerOf(grabbable) is not VRController holder)
                    return;

                if (!holder.active || FerrofluidGrabPhysics.ShouldReleaseGrab(holder.triggerValue))
                {
                    _grabOwnership.ReleaseTarget(grabbable);
                    grabbable.Release();
                }
            }

            internal void RunSpinTest(bool sphere, float rotationsPerSecond)
            {
                FerrofluidPrimitiveEffect? effect = sphere ? _sphere : _cube;
                if (effect == null || effect.Grabbable == null)
                    return;

                effect.Grabbable.StartSpinTest(rotationsPerSecond);
            }

            internal void StopSpinTests()
            {
                if (_cube != null && _cube.Grabbable != null)
                    _cube.Grabbable.StopSpinTest();
                if (_sphere != null && _sphere.Grabbable != null)
                    _sphere.Grabbable.StopSpinTest();
                if (_fluidCube != null && _fluidCube.Grabbable != null)
                    _fluidCube.Grabbable.StopSpinTest();
                if (_fluidSphere != null && _fluidSphere.Grabbable != null)
                    _fluidSphere.Grabbable.StopSpinTest();
            }

            private FerrofluidGrabbable? ResolveHovered(GameObject? hovering)
            {
                if (hovering == null)
                    return null;

                if (HoverMatches(hovering, _cube))
                    return _cube!.Grabbable;
                if (HoverMatches(hovering, _sphere))
                    return _sphere!.Grabbable;
                if (HoverMatches(hovering, _fluidCube))
                    return _fluidCube!.Grabbable;

                return HoverMatches(hovering, _fluidSphere) ? _fluidSphere!.Grabbable : null;
            }

            private static bool HoverMatches(GameObject hovering, FerrofluidPrimitiveEffect? effect)
            {
                return effect != null
                    && (ReferenceEquals(hovering, effect.gameObject) || hovering.transform.IsChildOf(effect.transform));
            }

            private FerrofluidGrabbable? AttachGrabbable(
                FerrofluidPrimitiveEffect effect,
                bool sphere,
                Config config)
            {
                if (!TryResolveEnclosure() || _enclosureMaterial == null)
                    return null;

                effect.InitializeFloorShadow(_enclosure.FloorY, _shadowLightDirection);
                FerrofluidGrabbable grabbable = effect.gameObject.AddComponent<FerrofluidGrabbable>();
                grabbable.Initialize(
                    _enclosure,
                    sphere,
                    _enclosureMaterial,
                    _grabOwnership);
                if (_floorCollider != null)
                    grabbable.ConfigureFloorContact(_floorCollider);
                if (!sphere)
                    grabbable.ConfigureCubeRoundness(FerrofluidEnvironmentObjects.EvaluateRoundness(FerrofluidSurfaceShape.RoundedCube, config));

                grabbable.ConfigureContactPhysics(config);

                return grabbable;
            }

            private bool TryResolveEnclosure()
            {
                if (_enclosureResolved)
                {
                    return true;
                }

                if (!FerrofluidGrabPhysics.TryResolveEnclosure(_playerAnchor, out _enclosure))
                {
                    if (!_enclosureFailedLogged)
                    {
                        _enclosureFailedLogged = true;
                        Plugin.Log.Error("[ChromaGLS Ferrofluid Environment] no active curved menu canvas contains the player and both primitives; grab physics disabled until the next reconcile.");
                    }

                    return false;
                }

                _enclosureResolved = true;
                foreach (FlickeringNeonSign sign in Resources.FindObjectsOfTypeAll<FlickeringNeonSign>())
                {
                    if (!sign.gameObject.activeInHierarchy || !sign.gameObject.scene.IsValid())
                        continue;

                    Vector3 toLight = sign._light.transform.position - new Vector3(0f, _enclosure.FloorY, 1f);
                    if (toLight.normalized.y > 0.1f)
                        _shadowLightDirection = toLight.normalized;
                    break;
                }

                _enclosureMaterial = CreateContactMaterial(floor: false);
                _floorMaterial = CreateContactMaterial(floor: true);
                _grabInput = FerrofluidMagnetController.CreateForGrab();
                _grabOwnership = new FerrofluidGrabOwnership();
                CreateEnclosureFloor();
                CreateCameraCollision();
                InitializeMenuNoteContacts();
                Plugin.Log.Info($"[ChromaGLS Ferrofluid Environment] grab enclosure center={_enclosure.Center} radius={_enclosure.Radius} rear={_enclosure.RearPoint}.");
                return true;
            }

            // The note piles live under the menu environment root, not the player rig, so the
            // scan happens once when the enclosure material exists and is never retried per frame.
            private void InitializeMenuNoteContacts()
            {
                if (_enclosureMaterial == null)
                    return;

                _menuNoteContacts = FerrofluidMenuNoteContacts.Create(
                    SceneManager.GetSceneByName("MainMenu"),
                    _enclosureMaterial);
                _menuNoteContacts?.Apply();
            }

            private void CreateEnclosureFloor()
            {
                GameObject floor = new("ChromaGLS Ferrofluid Floor")
                {
                    layer = FerrofluidGrabPhysics.EnclosureLayer,
                };
                floor.transform.SetParent(transform, false);
                floor.transform.position = new Vector3(
                    _enclosure.Center.x - (_enclosure.RearNormal.x * _enclosure.RearExtension * 0.5f),
                    _enclosure.FloorY - 0.5f,
                    _enclosure.Center.z - (_enclosure.RearNormal.z * _enclosure.RearExtension * 0.5f));
                BoxCollider floorCollider = floor.AddComponent<BoxCollider>();
                floorCollider.size = new Vector3(
                    ((_enclosure.Radius + 1f) * 2f) + (Mathf.Abs(_enclosure.RearNormal.x) * _enclosure.RearExtension),
                    1f,
                    ((_enclosure.Radius + 1f) * 2f) + (Mathf.Abs(_enclosure.RearNormal.z) * _enclosure.RearExtension));
                floorCollider.sharedMaterial = _floorMaterial;
                _floorCollider = floorCollider;
            }

            private void CreateCameraCollision()
            {
                if (_headTransform == null || _enclosureMaterial == null)
                {
                    return;
                }

                GameObject cameraObject = new("ChromaGLS Ferrofluid Camera")
                {
                    layer = FerrofluidGrabPhysics.EnclosureLayer,
                };
                cameraObject.transform.SetParent(transform, true);
                FerrofluidCameraCollision collision = cameraObject.AddComponent<FerrofluidCameraCollision>();
                collision.Initialize(_headTransform, SphereScale.y * 0.5f * 0.85f, _enclosureMaterial);
                _cameraCollision = collision;
            }

            private void DestroyPrimitive(ref FerrofluidPrimitiveEffect? effect)
            {
                if (effect == null)
                {
                    return;
                }

                effect.gameObject.SetActive(false);
                Destroy(effect.gameObject);
                effect = null;
            }

            private void AttachDeferred(FerrofluidPrimitiveEffect? effect, bool isSphere, Config config)
            {
                if (effect != null && effect.Grabbable == null)
                    effect.Grabbable = AttachGrabbable(effect, isSphere, config);
            }

            private void ReconcilePrimitive(
                ref FerrofluidPrimitiveEffect? effect,
                ref bool creationFailed,
                bool enabled,
                FerrofluidSurfaceShape shape,
                bool fluid,
                Config config)
            {
                if (enabled && effect == null && !creationFailed)
                {
                    effect = fluid
                        ? FerrofluidPrimitiveEffect.CreateFluid(_playerAnchor, shape, config)
                        : FerrofluidPrimitiveEffect.Create(_playerAnchor, shape, config);
                    creationFailed = effect == null;
                    if (effect != null)
                        effect.Grabbable = AttachGrabbable(effect, shape == FerrofluidSurfaceShape.Sphere, config);
                }

                if (!enabled)
                {
                    DestroyPrimitive(ref effect);
                    creationFailed = false;
                }
            }

            private void OnEnable()
            {
                Reconcile(_cubeEnabled, _sphereEnabled, _fluidCubeEnabled, _fluidSphereEnabled, _config);
                _menuNoteContacts?.Apply();
                if (_cameraCollision != null)
                {
                    _cameraCollision.SnapToHead();
                    _cameraCollision.gameObject.SetActive(true);
                }
            }

            // The primitives are scene roots, so hiding the menu rig must also stop their simulations.
            private void OnDisable()
            {
                _menuNoteContacts?.Restore();
                DestroyPrimitive(ref _cube);
                DestroyPrimitive(ref _sphere);
                DestroyPrimitive(ref _fluidCube);
                DestroyPrimitive(ref _fluidSphere);
                if (_cameraCollision != null)
                {
                    _cameraCollision.gameObject.SetActive(false);
                }
            }

            private void OnDestroy()
            {
                _menuNoteContacts?.Restore();
                _menuNoteContacts = null;
                DestroyPrimitive(ref _cube);
                DestroyPrimitive(ref _sphere);
                DestroyPrimitive(ref _fluidCube);
                DestroyPrimitive(ref _fluidSphere);
                if (_enclosureMaterial != null)
                {
                    Destroy(_enclosureMaterial);
                    _enclosureMaterial = null;
                }

                if (_floorMaterial != null)
                {
                    Destroy(_floorMaterial);
                    _floorMaterial = null;
                }

                if (ReferenceEquals(_manager, this))
                {
                    _manager = null;
                }
            }
        }
    }

    internal sealed class FerrofluidMenuLifecycle : IInitializable, System.IDisposable
    {
        public void Initialize() => FerrofluidEnvironmentObjects.BeginMenuSession();

        public void Dispose() => FerrofluidEnvironmentObjects.EndMenuSession();
    }

    [DefaultExecutionOrder(1100)]
    internal sealed partial class FerrofluidPrimitiveEffect : MonoBehaviour
    {
        private FerrofluidSurfaceShape _shape;
        private FerrofluidGpuSimulation? _simulation;
        private FerrofluidFloorShadow? _floorShadow;
        private FerrofluidMagnetController _magnetController = null!;
        private Vector3 _magneticFieldPosition;
        private Vector3 _magnetTarget;
        private Vector3 _handPosition;
        private float _handStrength;
        private float _sourceWorldScale;
        private float _roundness;
        private float _chartYawDegrees;
        private float _magnetSurfaceGap;
        private float _lastUpdateAt;
        private float _magneticFieldStrength;
        private bool _hasPointer;
        private bool _hasHand;

        internal FerrofluidGrabbable? Grabbable { get; set; }

        internal static FerrofluidPrimitiveEffect? Create(
            Transform playerAnchor,
            FerrofluidSurfaceShape shape,
            Config config)
            => CreatePrimitive(playerAnchor, shape, config, fluid: false);

        internal static FerrofluidPrimitiveEffect? CreateFluid(
            Transform playerAnchor,
            FerrofluidSurfaceShape shape,
            Config config)
            => CreatePrimitive(playerAnchor, shape, config, fluid: true);

        private static FerrofluidPrimitiveEffect? CreatePrimitive(
            Transform playerAnchor,
            FerrofluidSurfaceShape shape,
            Config config,
            bool fluid)
        {
            GameObject primitiveObject = new($"ChromaGLS {(fluid ? "Fluid" : "Ferrofluid")} {shape}", typeof(MeshFilter), typeof(MeshRenderer));
            Transform primitiveTransform = primitiveObject.transform;
            FerrofluidEnvironmentObjects.ApplyFloorPlacement(primitiveTransform, playerAnchor, shape);
            if (fluid)
            {
                FerrofluidEnvironmentObjects.EvaluateFluidPlacement(shape, out Vector3 fluidCenter, out _);
                primitiveTransform.position = fluidCenter;
            }

            FerrofluidPrimitiveEffect effect = primitiveObject.AddComponent<FerrofluidPrimitiveEffect>();
            effect._shape = shape;
            effect._roundness = FerrofluidEnvironmentObjects.EvaluateRoundness(shape, config);
            effect._chartYawDegrees = FerrofluidEnvironmentObjects.EvaluateChartYawDegrees(shape, config);
            effect._magnetSurfaceGap = FerrofluidEnvironmentObjects.EvaluatePrimitiveSurfaceGap(config);

            effect._sourceWorldScale = FerrofluidEnvironmentObjects.EvaluateSourceWorldScale(primitiveTransform);
            effect._magnetController = FerrofluidMagnetController.Create();
            effect._magneticFieldPosition = primitiveTransform.position;
            effect._simulation = FerrofluidGpuSimulation.TryCreate(
                primitiveObject.GetComponent<MeshFilter>(),
                primitiveObject.GetComponent<MeshRenderer>(),
                config,
                shape);
            if (effect._simulation == null)
            {
                primitiveObject.SetActive(false);
                Destroy(primitiveObject);
                return null;
            }

            if (fluid)
                effect._simulation.DisableSpikes(config);

            effect._lastUpdateAt = Time.unscaledTime;
            primitiveObject.AddComponent<FerrofluidPrimitiveRaycaster>().Initialize(effect);
            Plugin.Log.Info($"[ChromaGLS Ferrofluid Environment] attached shape={shape} worldPosition={primitiveTransform.position} scale={primitiveTransform.localScale} rig={playerAnchor.name} scene={primitiveObject.scene.name} parent={(primitiveTransform.parent == null ? "<root>" : primitiveTransform.parent.name)} root={primitiveTransform.root.name}.");
            return effect;
        }

        internal void InitializeFloorShadow(float floorY, Vector3 surfaceToLight)
        {
            if (_floorShadow != null
                || !FerrofluidGpuResources.TryGet(out FerrofluidGpuResources? resources)
                || resources!.FloorShadowShader == null)
            {
                return;
            }

            _floorShadow = new FerrofluidFloorShadow(transform, _shape, floorY, surfaceToLight, resources.FloorShadowShader, _simulation);
        }

        internal void ApplyConfiguration(Config config)
        {
            // Ray hits and GPU geometry must use the same roundness and chart yaw.
            _roundness = FerrofluidEnvironmentObjects.EvaluateRoundness(_shape, config);
            _chartYawDegrees = FerrofluidEnvironmentObjects.EvaluateChartYawDegrees(_shape, config);
            _magnetSurfaceGap = FerrofluidEnvironmentObjects.EvaluatePrimitiveSurfaceGap(config);
            _simulation?.ApplyConfiguration(config);
            if (Grabbable != null)
            {
                Grabbable.ConfigureCubeRoundness(_roundness);
                Grabbable.ConfigureContactPhysics(config);
            }
        }

        internal bool TryRaycast(Vector3 origin, Vector3 direction, out Vector3 position, out Vector3 normal)
        {
            Vector3 localOrigin = transform.InverseTransformPoint(origin);
            Vector3 localDirection = transform.InverseTransformDirection(direction).normalized;
            Vector3 target = FerrofluidInteraction.EvaluateRayTarget(_shape, localOrigin, localDirection, _roundness, out bool intersects);
            position = transform.TransformPoint(target);
            normal = transform.TransformDirection(FerrofluidEnvironmentObjects.EvaluateSurfaceNormalFromDirection(_shape, target, _roundness));
            return intersects;
        }

        // Smooth world positions before converting to UVs so crossing a chart seam cannot jump the magnet.
        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            float deltaTime = Mathf.Clamp(now - _lastUpdateAt, 0f, 0.1f);
            _lastUpdateAt = now;
            float width = transform.lossyScale.x;

            // Menu transitions can replace the player rig, so use its current transform for the field scale.
            _sourceWorldScale = FerrofluidEnvironmentObjects.EvaluateSourceWorldScale(transform);

            // Contacts can block the wanted cursor, so keep the field on the captured surface patch.
            bool held = Grabbable != null && Grabbable.IsHeld;
            bool hasRay;
            float targetStrength;
            Vector3 magnetPosition;
            Vector3 surfacePosition;
            if (held)
            {
                Vector3 localGrabAnchor = Grabbable!.LocalGrabAnchor;
                surfacePosition = transform.TransformPoint(localGrabAnchor);
                Vector3 normalLocal = FerrofluidEnvironmentObjects.EvaluateSurfaceNormalFromDirection(_shape, localGrabAnchor, _roundness);
                Vector3 normalWorld = transform.TransformDirection(normalLocal).normalized;
                magnetPosition = FerrofluidGpuSimulation.EvaluateMagnetSurfacePosition(
                    surfacePosition,
                    surfacePosition,
                    normalWorld,
                    _sourceWorldScale,
                    _magnetSurfaceGap);
                _magneticFieldPosition = surfacePosition;
                hasRay = true;
                targetStrength = 1f;
            }
            else
            {
                hasRay = _magnetController.TryGetActiveControllerRay(
                    out Vector3 rayOrigin,
                    out Vector3 rayDirection);
                if (hasRay)
                {
                    Vector3 localOrigin = transform.InverseTransformPoint(rayOrigin);
                    Vector3 localDirection = transform.InverseTransformDirection(rayDirection).normalized;
                    Vector3 localTarget = FerrofluidInteraction.EvaluateRayTarget(_shape, localOrigin, localDirection, _roundness, out _);
                    hasRay = Vector3.Dot(localTarget - localOrigin, localDirection) > 0f;
                    if (hasRay)
                    {
                        _magnetTarget = transform.TransformPoint(localTarget);
                        if (!_hasPointer)
                        {
                            _magneticFieldPosition = _magnetTarget;
                        }

                        _hasPointer = true;
                    }
                }

                _magneticFieldPosition = FerrofluidInteraction.TrackPosition(_magneticFieldPosition, _magnetTarget, width, deltaTime);
                MapPointer(_magneticFieldPosition, out surfacePosition, out targetStrength, out magnetPosition);
            }

            float strengthBlend = 1f - Mathf.Exp(-deltaTime / 0.1f);
            _magneticFieldStrength = Mathf.Lerp(_magneticFieldStrength, hasRay ? targetStrength : 0f, strengthBlend);

            bool hasHand = _magnetController.TryGetInactiveHandPosition(out Vector3 handTarget);
            if (hasHand)
            {
                if (!_hasHand)
                {
                    _handPosition = handTarget;
                }

                _hasHand = true;
                _handPosition = FerrofluidInteraction.TrackPosition(_handPosition, handTarget, width, deltaTime);
            }

            _handStrength = Mathf.Lerp(_handStrength, hasHand ? 1f : 0f, strengthBlend);

            _simulation!.UpdatePrimitiveFrame(
                _sourceWorldScale,
                magnetPosition,
                surfacePosition,
                _magneticFieldStrength,
                _handPosition,
                _handStrength,
                Grabbable != null ? Grabbable.BodyVelocity : Vector3.zero,
                Grabbable != null ? Grabbable.BodyAngularVelocity : Vector3.zero,
                Grabbable != null ? Grabbable.MagnetHeightAmplification : 1f);

            _floorShadow?.Update();
        }

        private void OnEnable() => _floorShadow?.SetVisible(true);

        private void OnDisable() => _floorShadow?.SetVisible(false);

        private void OnDestroy()
        {
            _simulation?.Dispose();
            _simulation = null;
            _floorShadow?.Dispose();
            _floorShadow = null;
        }

        // A missed ray still attracts the nearest visible surface, with strength reduced by the physical gap.
        private void MapPointer(
            Vector3 worldTarget,
            out Vector3 surfaceWorld,
            out float targetStrength,
            out Vector3 magnetPosition)
        {
            Vector3 localTarget = transform.InverseTransformPoint(worldTarget);
            Vector3 localSurfaceDirection = localTarget.sqrMagnitude > 0.00000001f
                ? localTarget.normalized
                : Vector3.up;
            Vector2 surfaceUv = FerrofluidEnvironmentObjects.EvaluateSurfaceUv(localSurfaceDirection, _chartYawDegrees);
            Vector3 surfaceLocal = FerrofluidEnvironmentObjects.EvaluateSurfacePosition(_shape, surfaceUv, _roundness, _chartYawDegrees);
            Vector3 normalLocal = FerrofluidEnvironmentObjects.EvaluateSurfaceNormal(_shape, surfaceUv, _roundness, _chartYawDegrees);
            surfaceWorld = transform.TransformPoint(surfaceLocal);
            Vector3 normalWorld = transform.TransformDirection(normalLocal).normalized;

            magnetPosition = FerrofluidGpuSimulation.EvaluateMagnetSurfacePosition(
                worldTarget,
                surfaceWorld,
                normalWorld,
                _sourceWorldScale,
                _magnetSurfaceGap);

            float surfaceDistance = Vector3.Distance(magnetPosition, surfaceWorld);
            float surfaceMissDistance = Vector3.Distance(worldTarget, surfaceWorld);
            targetStrength = FerrofluidEnvironmentObjects.EvaluatePrimitiveTargetStrength(
                surfaceDistance,
                surfaceMissDistance,
                _sourceWorldScale);
        }
    }
}
