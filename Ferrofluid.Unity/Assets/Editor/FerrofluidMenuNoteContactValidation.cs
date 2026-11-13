using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    // Measures a free ferrofluid sphere or cube colliding with a static native-style menu note
    // (0.5m BoxCollider, layer 8, no material override) so wall-grip expectations can be judged
    // against real PhysX contact, not the analytic enclosure wall.
    public static class FerrofluidMenuNoteContactValidation
    {
        private const float NoteSize = 0.5f;
        private const float SphereRadius = 0.275f;
        private static readonly List<string> Failures = new();
        private static readonly List<UnityEngine.Object> Resources = new();
        private static string CsvDir = string.Empty;
        private static Context Ctx = null!;

        private sealed class Context
        {
            internal Type Grabbable = null!;
            internal MethodInfo Initialize = null!;
            internal MethodInfo Advance = null!;
            internal MethodInfo ConfigureFloorContact = null!;
            internal MethodInfo ConfigureCubeRoundness = null!;
            internal Type HelperType = null!;
            internal object Enclosure = null!;
            internal object Ownership = null!;
            internal PhysicsMaterial BodyMaterial = null!;
            internal Collider FloorCollider = null!;
            internal float Gravity;

            internal object? CreateContacts(UnityEngine.SceneManagement.Scene scene)
                => (object?)Ctx.HelperType
                    .GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { scene, Ctx.BodyMaterial });

            internal static void ApplyContacts(object? contacts)
            {
                if (contacts != null)
                    Ctx.HelperType.GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(contacts, null);
            }

            internal static void RestoreContacts(object? contacts)
            {
                if (contacts != null)
                    Ctx.HelperType.GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(contacts, null);
            }
        }

        private sealed class NoteRecorder : MonoBehaviour
        {
            internal List<(Vector3 normal, Vector3 point, Vector3 impulse)> Contacts = new();

            private void OnCollisionEnter(Collision collision) => Record(collision);

            private void OnCollisionStay(Collision collision) => Record(collision);

            private void OnCollisionExit(Collision collision) => InContact = false;

            internal bool InContact;

            private void Record(Collision collision)
            {
                InContact = true;
                if (collision.contactCount > 0)
                {
                    ContactPoint contact = collision.GetContact(0);
                    Contacts.Add((contact.normal, contact.point, collision.impulse));
                }
            }
        }

        public static void Run()
            => FerrofluidValidationRunner.Run(typeof(FerrofluidMenuNoteContactValidation));

        private static IEnumerator RunRuntime()
        {
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Failures.Clear();
            Resources.Clear();
            Ctx = new Context();
            try
            {
                string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
                string pluginPath = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_PLUGIN")
                    ?? Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll");
                CsvDir = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_OUTPUT")
                    ?? Path.Combine(Directory.GetParent(repository)!.FullName, "Scratch", "ferro-note-grip", "validation");
                Debug.Log($"[FerrofluidMenuNoteContactValidation] dll={pluginPath} csv={CsvDir}");
                Assembly plugin = Assembly.LoadFrom(pluginPath);
                Ctx.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Ctx.HelperType = plugin.GetType("ChromaGLS.Settings.FerrofluidMenuNoteContacts", throwOnError: false)!;
                Ctx.Initialize = Ctx.Grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Advance = Ctx.Grabbable.GetMethod("AdvancePhysics", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureFloorContact = Ctx.Grabbable.GetMethod("ConfigureFloorContact", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureCubeRoundness = Ctx.Grabbable.GetMethod("ConfigureCubeRoundness", BindingFlags.Instance | BindingFlags.NonPublic);
                Type physics = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true);
                Ctx.Gravity = (float)physics.GetField("GravityMagnitude", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                ConstructorInfo enclosureCtor = enclosureType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null)!;
                Ctx.Enclosure = enclosureCtor.Invoke(new object[] { Vector3.zero, 100f, new Vector3(0f, 0f, -100f), Vector3.forward, 0f });
                Ctx.Ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true), nonPublic: true);
                Type manager = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true);
                MethodInfo factory = manager.GetMethod("CreateContactMaterial", BindingFlags.Static | BindingFlags.NonPublic);
                PhysicsMaterial floorMaterial = (PhysicsMaterial)factory!.Invoke(null, new object[] { true });
                Ctx.BodyMaterial = (PhysicsMaterial)factory.Invoke(null, new object[] { false });
                Resources.Add(floorMaterial);
                Resources.Add(Ctx.BodyMaterial);

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Resources.Add(floor);
                floor.name = "note contact floor";
                floor.layer = 3;
                floor.transform.localScale = new Vector3(40f, 1f, 40f);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                Ctx.FloorCollider = floor.GetComponent<Collider>();
                Ctx.FloorCollider.material = floorMaterial;

                foreach (bool productionMaterial in new[] { false, true })
                {
                    foreach (float speed in new[] { 2f, 4f })
                    {
                        foreach (float dt in new[] { 0.02f, 1f / 90f })
                        {
                            foreach (float yaw in new[] { 0f, 60f })
                            {
                                IEnumerator sphereCase = CaseNoteContact(true, yaw, speed, dt, productionMaterial);
                                while (sphereCase.MoveNext())
                                    yield return sphereCase.Current;
                            }
                        }
                    }
                }

                IEnumerator binding = CaseNoteBinding();
                while (binding.MoveNext())
                    yield return binding.Current;

                IEnumerator cubeCase = CaseNoteContact(false, 0f, 2f, 0.02f, false);
                while (cubeCase.MoveNext())
                    yield return cubeCase.Current;
            }
            finally
            {
                Time.fixedDeltaTime = previousDelta;
                Physics.simulationMode = previousMode;
                foreach (UnityEngine.Object resource in Resources)
                    if (resource != null)
                        UnityEngine.Object.DestroyImmediate(resource);
                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} note contact cases failed: {string.Join(" | ", Failures)}");
            }

            yield break;
        }

        // Drives the production helper against an artificial PileOfNotes: Apply must bind the
        // body material, Restore must return the originals, and a late third-party material
        // must survive Restore untouched. Missing helper type marks this run pre-implementation.
        private static IEnumerator CaseNoteBinding()
        {
            AssertTrue(
                Ctx.HelperType != null,
                "FerrofluidMenuNoteContacts must exist in the production assembly");
            if (Ctx.HelperType == null)
                yield break;

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.CreateScene("note-binding");
            GameObject environment = new("DefaultMenuEnvironment");
            Resources.Add(environment);
            GameObject pile = new("PileOfNotes");
            Resources.Add(pile);
            pile.transform.SetParent(environment.transform, false);
            GameObject note1 = MakeNote(Quaternion.identity, null);
            GameObject note2 = MakeNote(Quaternion.Euler(0f, 30f, 0f), null);
            PhysicsMaterial original = new("note original");
            Resources.Add(original);
            note2.GetComponent<BoxCollider>().sharedMaterial = original;
            note1.transform.SetParent(pile.transform, false);
            note2.transform.SetParent(pile.transform, false);
            GameObject trigger = MakeNote(Quaternion.identity, null);
            trigger.transform.SetParent(pile.transform, false);
            trigger.GetComponent<BoxCollider>().isTrigger = true;
            GameObject inactive = MakeNote(Quaternion.identity, null);
            inactive.transform.SetParent(pile.transform, false);
            inactive.SetActive(false);
            GameObject floating = MakeNote(Quaternion.identity, null);
            floating.name = "floating decoy";
            GameObject decoyEnvironment = new("DefaultMenuEnvironment");
            Resources.Add(decoyEnvironment);
            GameObject decoyNote = MakeNote(Quaternion.identity, null);
            decoyNote.transform.SetParent(decoyEnvironment.transform, false);

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(environment, scene);
            object? contacts = Ctx.CreateContacts(scene);
            AssertTrue(contacts != null, "Create must bind inside the scene containing the environment");
            Context.ApplyContacts(contacts);
            AssertTrue(
                note1.GetComponent<BoxCollider>().sharedMaterial == Ctx.BodyMaterial,
                "ground note must use the enclosure contact material after Apply");
            AssertTrue(
                inactive.GetComponent<BoxCollider>().sharedMaterial == Ctx.BodyMaterial,
                "inactive pile children must still bind");
            AssertTrue(
                trigger.GetComponent<BoxCollider>().sharedMaterial != Ctx.BodyMaterial,
                "trigger colliders must not be rebound");
            AssertTrue(
                floating.GetComponent<BoxCollider>().sharedMaterial != Ctx.BodyMaterial,
                "colliders outside PileOfNotes must be untouched");
            AssertTrue(
                decoyNote.GetComponent<BoxCollider>().sharedMaterial != Ctx.BodyMaterial,
                "environments in other scenes must be untouched");

            PhysicsMaterial foreign = new("foreign");
            Resources.Add(foreign);
            note1.GetComponent<BoxCollider>().sharedMaterial = foreign;
            Context.RestoreContacts(contacts);
            AssertTrue(
                note1.GetComponent<BoxCollider>().sharedMaterial == foreign,
                "Restore must not overwrite a third-party material installed after Apply");
            note1.GetComponent<BoxCollider>().sharedMaterial = Ctx.BodyMaterial;
            Context.RestoreContacts(contacts);
            AssertTrue(
                note1.GetComponent<BoxCollider>().sharedMaterial == null,
                "Restore must return the original null material");
            AssertTrue(
                note2.GetComponent<BoxCollider>().sharedMaterial == original,
                "Restore must return a distinct non-null original material");
            Context.ApplyContacts(contacts);
            AssertTrue(
                note1.GetComponent<BoxCollider>().sharedMaterial == Ctx.BodyMaterial,
                "Apply must be re-entrant");
            UnityEngine.Object.DestroyImmediate(inactive.GetComponent<BoxCollider>());
            Context.RestoreContacts(contacts);
            AssertTrue(
                note2.GetComponent<BoxCollider>().sharedMaterial == original,
                "Restore must survive a destroyed collider");

            UnityEngine.Object.DestroyImmediate(environment);
            GameObject pileless = new("DefaultMenuEnvironment");
            Resources.Add(pileless);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pileless, scene);
            object? pilelessContacts = Ctx.CreateContacts(scene);
            AssertTrue(pilelessContacts != null, "Create must still produce a helper when the pile is missing");
            Context.ApplyContacts(pilelessContacts);
            Context.RestoreContacts(pilelessContacts);
            Debug.Log("[FerrofluidMenuNoteContactValidation] binding lifecycle passed");
            yield break;
        }

        // Native note shape: a 0.5m box resting on the floor with its face at +z = 0.25, the
        // same footprint the real menu PileOfNotes colliders use (size .5, layer 8, no material).
        private static GameObject MakeNote(Quaternion yaw, PhysicsMaterial material)
        {
            GameObject note = new GameObject("native note");
            Resources.Add(note);
            note.layer = 8;
            note.transform.position = yaw * new Vector3(0f, 0.226f, 0f);
            note.transform.rotation = yaw;
            BoxCollider box = note.AddComponent<BoxCollider>();
            box.size = Vector3.one * NoteSize;
            box.center = Vector3.zero;
            if (material != null)
                box.material = material;
            return note;
        }

        private static IEnumerator CaseNoteContact(bool sphere, float yawDegrees, float speed, float dt, bool productionMaterial)
        {
            Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
            Vector3 approach = yaw * Vector3.forward;
            string label = string.Format(
                CultureInfo.InvariantCulture,
                "note-{0}-{1}-yaw{2}-s{3:0}-dt{4:0.0000}",
                productionMaterial ? "bodymat" : "default",
                sphere ? "sphere" : "cube",
                yawDegrees,
                speed,
                dt);
            GameObject note = MakeNote(yaw, null);
            NoteRecorder recorder = note.AddComponent<NoteRecorder>();
            UnityEngine.SceneManagement.Scene testScene = default;
            object? contacts = null;
            if (productionMaterial && Ctx.HelperType != null)
            {
                testScene = UnityEngine.SceneManagement.SceneManager.CreateScene($"note-contact-{label}");
                GameObject environment = new("DefaultMenuEnvironment");
                Resources.Add(environment);
                GameObject pile = new("PileOfNotes");
                Resources.Add(pile);
                pile.transform.SetParent(environment.transform, false);
                note.transform.SetParent(pile.transform, false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(environment, testScene);
                contacts = Ctx.CreateContacts(testScene);
                AssertTrue(contacts != null, $"{label} helper Create must find the environment in the isolated scene");
                Context.ApplyContacts(contacts);
            }

            GameObject body = new GameObject(label);
            Resources.Add(body);
            body.transform.position = approach * -0.53f + new Vector3(0f, sphere ? SphereRadius + 0.002f : 0.252f, 0f);
            body.transform.localScale = Vector3.one * 0.55f;
            Component component = body.AddComponent(Ctx.Grabbable);
            Ctx.Initialize.Invoke(component, new object[] { Ctx.Enclosure, sphere, Ctx.BodyMaterial, Ctx.Ownership });
            Ctx.ConfigureFloorContact.Invoke(component, new object[] { Ctx.FloorCollider });
            if (!sphere)
                Ctx.ConfigureCubeRoundness.Invoke(component, new object[] { 0.48f });
            ((MonoBehaviour)component).enabled = false;

            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.angularVelocity = Vector3.Cross(Vector3.up, approach) * (speed / SphereRadius);
            rb.linearVelocity = approach * speed;
            Time.fixedDeltaTime = dt;
            int steps = Mathf.RoundToInt(0.7f / dt);
            bool separated = false;
            int lastContacts = 0;
            int impactStep = -1;
            float vyBefore = 0f;
            float wxBefore = 0f;
            float vyAfter = 0f;
            float wxAfter = 0f;
            float vnAfter = speed;
            Vector3 impactPos = Vector3.zero;
            float peakClearance = 0f;
            float exitDistance = 0f;
            bool secondHit = false;
            int stopStep = steps;
            List<string> rows = StartRows();
            for (int step = 0; step <= steps; step++)
            {
                if (step == steps || step > stopStep)
                    break;

                float preVn = Vector3.Dot(rb.linearVelocity, approach);
                float preVy = rb.linearVelocity.y;
                float preWx = Vector3.Dot(rb.angularVelocity, Vector3.Cross(Vector3.up, approach));
                rows.Add(PreRow(step, step * dt, rb));
                Ctx.Advance.Invoke(component, new object[] { dt, step * dt });
                rows[rows.Count - 1] += Triple(rb);
                Physics.Simulate(dt);
                rows[rows.Count - 1] += Triple(rb) + Quad(rb);
                if (impactStep < 0 && Vector3.Dot(rb.linearVelocity, approach) < preVn - 0.05f)
                {
                    impactStep = step;
                    vyBefore = preVy;
                    wxBefore = preWx;
                    vyAfter = rb.linearVelocity.y;
                    wxAfter = Vector3.Dot(rb.angularVelocity, Vector3.Cross(Vector3.up, approach));
                    vnAfter = Vector3.Dot(rb.linearVelocity, approach);
                    impactPos = rb.position;
                    stopStep = impactStep + Mathf.CeilToInt(0.5f / dt);
                }
                else if (impactStep >= 0)
                {
                    if (!recorder.InContact)
                        separated = true;
                    if (separated && recorder.Contacts.Count > lastContacts)
                        secondHit = true;
                    if ((step - impactStep) * dt <= 0.15f)
                        peakClearance = Mathf.Max(peakClearance, rb.position.y - (sphere ? SphereRadius : 0.25f));
                    if ((step - impactStep) * dt <= 0.3f)
                        exitDistance = Mathf.Max(exitDistance, Vector3.Dot(rb.position - impactPos, -approach));
                }

                lastContacts = recorder.Contacts.Count;
                yield return null;
            }

            WriteRows($"contact-{label}.csv", rows);
            string contact = recorder.Contacts.Count > 0
                ? $"n={V3(recorder.Contacts[0].normal)} p={V3(recorder.Contacts[0].point)} i={V3(recorder.Contacts[0].impulse)} hits={recorder.Contacts.Count}"
                : "no contact recorded";
            Debug.Log($"[FerrofluidMenuNoteContactValidation] {label}: step={impactStep} vy={vyBefore:F3}->{vyAfter:F3} wx={wxBefore:F3}->{wxAfter:F3} vn={vnAfter:F3} clear={peakClearance:F4} away={exitDistance:F3} {contact}");
            AssertTrue(impactStep >= 0, $"{label} must reach the note");
            if (sphere)
            {
                float liftImpulse = vyAfter - vyBefore + Ctx.Gravity * dt;
                AssertTrue(
                    liftImpulse >= 0.45f,
                    $"{label} note contact must bite upward >=0.45 impulse (actual={liftImpulse:F3})");
                AssertTrue(
                    wxAfter < wxBefore * 0.6f,
                    $"{label} note contact must consume >=40% of the roll (wx={wxBefore:F2}->{wxAfter:F2})");
                AssertTrue(
                    peakClearance > 0.002f,
                    $"{label} must clear the floor after the bite (peak={peakClearance:F4})");
                if (speed >= 3.9f)
                {
                    AssertTrue(vnAfter < 0f, $"{label} must reflect off the note (vn={vnAfter:F3})");
                    AssertTrue(
                        exitDistance >= 0.03f,
                        $"{label} must move away from the note within 0.3s (away={exitDistance:F3})");
                    AssertTrue(separated, $"{label} must separate from the note before the re-contact check");
                    AssertTrue(!secondHit, $"{label} must not strike the note a second time within 0.5s");
                    if (productionMaterial)
                        AssertTrue(
                            Mathf.Abs(vnAfter + (0.468f * speed)) < 0.02f,
                            $"{label} enclosure material must give 0.468 restitution (vn={vnAfter:F3} expected={-0.468f * speed:F2})");
                }
            }

            Context.RestoreContacts(contacts);
            component.gameObject.SetActive(false);
            note.SetActive(false);
        }

        private static string V3(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

        private static List<string> StartRows()
            => new()
            {
                "step,time,prepx,prepy,prepz,prevx,prevy,prevz,prewx,prewy,prewz,preqx,preqy,preqz,preqw,advpx,advpy,advpz,advvx,advvy,advvz,advwx,advwy,advwz,postpx,postpy,postpz,postvx,postvy,postvz,postwx,postwy,postwz,postqx,postqy,postqz,postqw",
            };

        private static string PreRow(int step, float time, Rigidbody rb)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Vector3 p = rb.position;
            Vector3 v = rb.linearVelocity;
            Vector3 w = rb.angularVelocity;
            Quaternion q = rb.rotation;
            return string.Join(",", new[]
            {
                step.ToString(c), time.ToString(c),
                p.x.ToString(c), p.y.ToString(c), p.z.ToString(c),
                v.x.ToString(c), v.y.ToString(c), v.z.ToString(c),
                w.x.ToString(c), w.y.ToString(c), w.z.ToString(c),
                q.x.ToString(c), q.y.ToString(c), q.z.ToString(c), q.w.ToString(c),
            });
        }

        private static string Triple(Rigidbody rb)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Vector3 p = rb.position;
            Vector3 v = rb.linearVelocity;
            Vector3 w = rb.angularVelocity;
            return "," + string.Join(",", new[]
            {
                p.x.ToString(c), p.y.ToString(c), p.z.ToString(c),
                v.x.ToString(c), v.y.ToString(c), v.z.ToString(c),
                w.x.ToString(c), w.y.ToString(c), w.z.ToString(c),
            });
        }

        private static string Quad(Rigidbody rb)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Quaternion q = rb.rotation;
            return "," + string.Join(",", new[] { q.x.ToString(c), q.y.ToString(c), q.z.ToString(c), q.w.ToString(c) });
        }

        private static void WriteRows(string name, List<string> rows)
        {
            Directory.CreateDirectory(CsvDir);
            File.WriteAllLines(Path.Combine(CsvDir, name), rows);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                Failures.Add(message);
        }
    }
}
