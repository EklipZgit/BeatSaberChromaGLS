using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidOrbitConstraintProbe
    {
        public static void RunCenterOfMass()
        {
            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (bool sphere in new[] { false, true })
                {
                    GameObject gameObject = new("COM probe");
                    try
                    {
                        gameObject.transform.position = Vector3.up * 3f;
                        gameObject.transform.localScale = Vector3.one * 0.55f;
                        if (sphere)
                            gameObject.AddComponent<SphereCollider>().radius = 0.5f;
                        else
                            gameObject.AddComponent<BoxCollider>().size = Vector3.one;

                        Rigidbody body = gameObject.AddComponent<Rigidbody>();
                        body.useGravity = false;
                        body.maxAngularVelocity = 100f;
                        Vector3 originalCenter = body.centerOfMass;
                        Vector3 originalInertia = body.inertiaTensor;
                        Quaternion originalInertiaRotation = body.inertiaTensorRotation;
                        Vector3 anchor = new(0.275f, 0f, 0f);
                        Vector3 grabWorld = body.position + (body.rotation * anchor);
                        body.centerOfMass = anchor;
                        body.ResetInertiaTensor();
                        Debug.Log($"[COMProbe] sphere{sphere} oldI={originalInertia.ToString("F6")} newI={body.inertiaTensor.ToString("F6")} qI={body.inertiaTensorRotation} COM={body.worldCenterOfMass} wanted={grabWorld}");
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = new Vector3(20f, 40f, 30f);
                        Physics.SyncTransforms();
                        for (int step = 0; step < 20; step++)
                            Physics.Simulate(0.02f);

                        Vector3 clicked = body.position + (body.rotation * anchor);
                        Vector3 originVelocity = body.GetPointVelocity(body.position);
                        Vector3 omega = body.angularVelocity;
                        Vector3 rootPosition = body.position;
                        body.centerOfMass = originalCenter;
                        body.inertiaTensor = originalInertia;
                        body.inertiaTensorRotation = originalInertiaRotation;
                        body.linearVelocity = originVelocity;
                        body.angularVelocity = omega;
                        Debug.Log($"[COMProbe] sphere{sphere} clickedError={Vector3.Distance(clicked, grabWorld):F8} restorePositionError={Vector3.Distance(rootPosition, body.position):F8} releaseVelocityError={Vector3.Distance(originVelocity, body.GetPointVelocity(body.position)):F8}");
                    }
                    finally
                    {
                        Object.DestroyImmediate(gameObject);
                    }
                }
            }
            finally
            {
                Physics.simulationMode = previousMode;
            }
        }

        public static void Run()
        {
            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                    Debug.Log($"[AxisProbeCollider] {collider.name} pos={collider.transform.position} type={collider.GetType().Name}");

                foreach (bool swapBodies in new[] { false, true })
                {
                    foreach (bool worldSpace in new[] { false, true })
                    {
                        GameObject bodyObject = new("axis probe body");
                        GameObject anchorObject = new("axis probe anchor");
                        try
                        {
                            bodyObject.transform.position = Vector3.up * 3f;
                            anchorObject.transform.position = bodyObject.transform.position;
                            bodyObject.AddComponent<SphereCollider>().radius = 0.2f;
                            Rigidbody body = bodyObject.AddComponent<Rigidbody>();
                            body.useGravity = false;
                            Rigidbody anchor = anchorObject.AddComponent<Rigidbody>();
                            anchor.isKinematic = true;
                            ConfigurableJoint joint = bodyObject.AddComponent<ConfigurableJoint>();
                            joint.connectedBody = anchor;
                            joint.swapBodies = swapBodies;
                            joint.autoConfigureConnectedAnchor = false;
                            joint.anchor = Vector3.zero;
                            joint.connectedAnchor = Vector3.zero;
                            joint.xMotion = ConfigurableJointMotion.Free;
                            joint.yMotion = ConfigurableJointMotion.Locked;
                            joint.zMotion = ConfigurableJointMotion.Locked;
                            joint.angularXMotion = ConfigurableJointMotion.Free;
                            joint.angularYMotion = ConfigurableJointMotion.Free;
                            joint.angularZMotion = ConfigurableJointMotion.Free;
                            joint.configuredInWorldSpace = worldSpace;
                            body.rotation = Quaternion.Euler(0f, 73f, 0f);
                            body.PublishTransform();
                            Quaternion inverseReference = Quaternion.Inverse(swapBodies ? anchor.rotation : body.rotation);
                            joint.axis = inverseReference * Vector3.right;
                            joint.secondaryAxis = inverseReference * Vector3.up;
                            anchor.position = (Vector3.up * 3f) + (Vector3.right * 0.5f);
                            body.linearVelocity = new Vector3(2f, 0f, 2f);
                            Physics.SyncTransforms();
                            for (int step = 0; step < 5; step++)
                            {
                                Physics.Simulate(0.02f);
                                Debug.Log($"[AxisProbe] step{step} swap{swapBodies} world{worldSpace} localAxis={joint.axis} ownerWorldAxis={body.rotation * joint.axis} connectedWorldAxis={anchor.rotation * joint.axis} position={body.position} velocity={body.linearVelocity} omega={body.angularVelocity} COM={body.worldCenterOfMass} anchorA={joint.anchor} anchorB={joint.connectedAnchor}");
                            }
                        }
                        finally
                        {
                            Object.DestroyImmediate(bodyObject);
                            Object.DestroyImmediate(anchorObject);
                        }
                    }
                }
            }
            finally
            {
                Physics.simulationMode = previousMode;
            }
        }
    }
}
