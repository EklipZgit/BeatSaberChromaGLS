using System;
using System.IO;
using Unity.XR.MockHMD;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace ChromaGLS.EditorShared
{
    // Any shader bundle this project ships must be built with an XR loader enabled: Unity's
    // automatic variant stripping removes STEREO_INSTANCING_ON / UNITY_SINGLE_PASS_STEREO /
    // STEREO_MULTIVIEW_ON / STEREO_CUBEMAP_RENDER_ON variants from builds (including asset bundles)
    // when no XR provider is configured, which made the ferrofluid surface render in the left eye
    // only under single-pass instanced rendering. Assigning the MockHMD loader for Standalone keeps
    // those variants in every build from this project; MockHMD is Unity's headless XR provider and
    // doubles as the stereo test harness provider.
    internal static class MockHmdProjectSetup
    {
        internal static void EnsureConfigured()
        {
            XRGeneralSettingsPerBuildTarget buildTargetSettings = null;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out buildTargetSettings);
            if (buildTargetSettings == null)
            {
                Directory.CreateDirectory("Assets/XR");
                buildTargetSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(buildTargetSettings, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, buildTargetSettings, true);
            }

            const BuildTargetGroup group = BuildTargetGroup.Standalone;
            if (!buildTargetSettings.HasSettingsForBuildTarget(group))
            {
                buildTargetSettings.CreateDefaultSettingsForBuildTarget(group);
            }

            XRGeneralSettings settings = buildTargetSettings.SettingsForBuildTarget(group);
            if (!buildTargetSettings.HasManagerSettingsForBuildTarget(group))
            {
                buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(group);
            }

            XRManagerSettings manager = settings.Manager;
            manager.automaticLoading = true;
            manager.automaticRunning = true;
            bool assigned = XRPackageMetadataStore.AssignLoader(
                manager,
                "Unity.XR.MockHMD.MockHMDLoader",
                group);
            if (!assigned)
            {
                throw new InvalidOperationException("Failed to assign the MockHMD loader for Standalone.");
            }

            MockHMDBuildSettings mockSettings = MockHMDBuildSettings.Instance;
            if (mockSettings == null)
            {
                const string settingsPath = "Assets/XR/Settings/MockHMDBuildSettings.asset";
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                mockSettings = AssetDatabase.LoadAssetAtPath<MockHMDBuildSettings>(settingsPath);
                if (mockSettings == null)
                {
                    mockSettings = ScriptableObject.CreateInstance<MockHMDBuildSettings>();
                    AssetDatabase.CreateAsset(mockSettings, settingsPath);
                }

                // EditorBuildSettings config objects must be persistent, so the settings scriptable
                // has to be saved as an asset before AddConfigObject accepts it.
                EditorBuildSettings.AddConfigObject(MockHMDBuildSettings.BuildSettingsKey, mockSettings, true);
            }

            mockSettings.renderMode = MockHMDBuildSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(mockSettings);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(buildTargetSettings);
            AssetDatabase.SaveAssets();
            Debug.Log("[ChromaGLS] MockHMD loader assigned, render mode = SinglePassInstanced");
        }
    }
}
