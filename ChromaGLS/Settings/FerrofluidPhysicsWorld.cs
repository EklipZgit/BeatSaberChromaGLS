using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRUIControls;

namespace ChromaGLS.Settings
{
    // Owns the isolated local physics scene that simulates the ferrofluid bodies so fast-spinning
    // cubes can be integrated at substep rates without changing the shared fixed timestep.
    internal sealed class FerrofluidPhysicsWorld : System.IDisposable
    {
        private const int MaximumBodyCount = 4;

        private readonly Scene _scene;
        private readonly PhysicsScene _physicsScene;
        private readonly FerrofluidGrabbable?[] _bodies = new FerrofluidGrabbable?[MaximumBodyCount];
        private readonly List<PhysicsRaycasterWithCache> _trackedRaycasters = new(4);
        private int _bodyCount;
        private bool _disposed;
        private FerrofluidCameraCollision? _cameraCollision;
        private bool _cameraCollisionConfigured;
        private FerrofluidMenuNoteContacts? _noteContacts;

        internal FerrofluidPhysicsWorld()
        {
            _scene = SceneManager.CreateScene(
                "ChromaGLS Ferrofluid Physics",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physicsScene = _scene.GetPhysicsScene();
        }

        // Null whenever no ferrofluid bodies are simulated so the UI raycast patch stays inert.
        internal static FerrofluidPhysicsWorld? Active { get; private set; }

        internal Scene Scene => _scene;

        // Only roots created for this world may move; visual and raycast roots never do.
        internal void MoveRootToScene(GameObject root)
            => SceneManager.MoveGameObjectToScene(root, _scene);

        internal void Register(FerrofluidGrabbable body)
        {
            if (_bodyCount >= MaximumBodyCount)
                return;

            _bodies[_bodyCount++] = body;
            Active = this;
            ClearTrackedRaycasterCaches();
        }

        internal void Unregister(FerrofluidGrabbable body)
        {
            for (int i = 0; i < _bodyCount; i++)
            {
                if (!ReferenceEquals(_bodies[i], body))
                    continue;

                for (int j = i; j + 1 < _bodyCount; j++)
                    _bodies[j] = _bodies[j + 1];
                _bodyCount--;
                _bodies[_bodyCount] = null;
                if (_bodyCount == 0 && ReferenceEquals(Active, this))
                    Active = null;

                ClearTrackedRaycasterCaches();
                return;
            }
        }

        internal void SetCameraCollision(FerrofluidCameraCollision camera)
        {
            camera.ConfigurePhysicsWorld(this);
            _cameraCollision = camera;
            _cameraCollisionConfigured = true;
        }

        internal void SetNoteContacts(FerrofluidMenuNoteContacts contacts)
        {
            contacts.CreateProxies(this);
            _noteContacts = contacts;
        }

        internal void Step(float deltaTime, float unscaledDeltaTime, float sampleTime)
        {
            if (_bodyCount == 0)
                return;

            for (int i = 0; i < _bodyCount; i++)
                _bodies[i]!.CapturePreviousPhysicsPose();

            _noteContacts?.SyncProxies();
            if (_cameraCollisionConfigured)
                _cameraCollision!.SampleMotionEndpoints();

            int stepCount = 1;
            for (int i = 0; i < _bodyCount; i++)
                stepCount = Mathf.Max(stepCount, _bodies[i]!.EvaluatePhysicsSubsteps(deltaTime));

            float subDt = deltaTime / stepCount;
            float subUnscaledDt = unscaledDeltaTime / stepCount;
            float stepStart = sampleTime - unscaledDeltaTime;
            for (int i = 0; i < stepCount; i++)
            {
                if (_cameraCollisionConfigured)
                    _cameraCollision!.AdvanceSubstepPosition((i + 1f) / stepCount);
                float subSampleTime = stepStart + (subUnscaledDt * (i + 1));
                for (int b = 0; b < _bodyCount; b++)
                    _bodies[b]!.AdvancePhysics(subDt, subSampleTime);
                _physicsScene.Simulate(subDt);
            }

            for (int i = 0; i < _bodyCount; i++)
                _bodies[i]!.CaptureCurrentPhysicsPose();
        }

        internal void Render(float alpha)
        {
            for (int i = 0; i < _bodyCount; i++)
                _bodies[i]!.RenderPhysicsPose(alpha);
        }

        internal bool Raycast(Ray ray, out RaycastHit hit, float distance, int mask)
            => _physicsScene.Raycast(
                ray.origin,
                ray.direction,
                out hit,
                distance,
                mask,
                QueryTriggerInteraction.UseGlobal);

        internal void TrackRaycaster(PhysicsRaycasterWithCache instance)
        {
            foreach (PhysicsRaycasterWithCache tracked in _trackedRaycasters)
            {
                if (ReferenceEquals(tracked, instance))
                    return;
            }

            _trackedRaycasters.Add(instance);
        }

        // Cached UI hits keep pointing at colliders that have moved scenes or ceased to exist.
        internal void ClearTrackedRaycasterCaches()
        {
            foreach (PhysicsRaycasterWithCache instance in _trackedRaycasters)
            {
                instance._cachedRaycasts.Clear();
                instance._lastFrameCount = -1;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(Active, this))
                Active = null;

            ClearTrackedRaycasterCaches();
            _trackedRaycasters.Clear();
            for (int i = 0; i < _bodyCount; i++)
                _bodies[i] = null;
            _bodyCount = 0;
            _cameraCollision = null;
            _cameraCollisionConfigured = false;
            _noteContacts = null;
            if (_scene.IsValid() && _scene.isLoaded)
            {
                foreach (GameObject root in _scene.GetRootGameObjects())
                    root.SetActive(false);
                SceneManager.UnloadSceneAsync(_scene);
            }
        }
    }

    // Publishes interpolated visual poses between parent physics steps; must run before
    // EventSystem so hovered poses and the rendered pose come from the same interpolation.
    // Disabled whenever unbound, so a missing world can never be rendered.
    [DefaultExecutionOrder(-1000)]
    internal sealed class FerrofluidPhysicsRenderDriver : MonoBehaviour
    {
        private FerrofluidPhysicsWorld _world = null!;

        internal void Bind(FerrofluidPhysicsWorld world)
            => _world = world;

        internal void Unbind()
            => _world = null!;

        private void Update()
            => _world.Render(Mathf.Clamp01(
                (Time.time - Time.fixedTime) / Time.fixedDeltaTime));
    }
}
