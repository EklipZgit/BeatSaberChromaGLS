using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChromaGLS.Settings;
using CustomJSONData;
using CustomJSONData.CustomBeatmap;
using JetBrains.Annotations;
using UnityEngine;
using Zenject;

namespace ChromaGLS
{
    [UsedImplicitly]
    internal class ChromaGLSPlayerInstaller : Installer
    {
        public override void InstallBindings()
        {
            // The referenced Zenject assembly lacks the injection API, so assign these dependencies in the installer.
            Container.Bind<GradientBackgroundRemoval>()
                .FromMethod(ctx =>
                {
                    var removal = new GameObject(nameof(GradientBackgroundRemoval))
                        .AddComponent<GradientBackgroundRemoval>();
                    removal.SceneSetupData = ctx.Container.Resolve<GameplayCoreSceneSetupData>();
                    removal.Config = ctx.Container.Resolve<Config>();
                    return removal;
                })
                .AsSingle()
                .NonLazy();
        }
    }

    // Remove the environment gradient on ChromaGLS maps. Let Chroma handle maps that require it.
    // (This is cloned from Chroma and prevents the horrible OE gradient from showing up on any maps it might)
    internal class GradientBackgroundRemoval : MonoBehaviour
    {
        internal GameplayCoreSceneSetupData? SceneSetupData { get; set; }

        internal Config? Config { get; set; }

        [UsedImplicitly]
        private IEnumerator Start()
        {
            // The environment must finish initializing before its gradient can be hidden.
            yield return new WaitForEndOfFrame();

            if (!MapNeedsGlsRemoval())
            {
                yield break;
            }

            var gradientBackground = GameObject.Find("/Environment/GradientBackground");
            if (gradientBackground != null)
            {
                gradientBackground.SetActive(false);
            }
        }

        private bool MapNeedsGlsRemoval()
        {
            var sceneSetupData = SceneSetupData;
            var config = Config;
            if (sceneSetupData == null || config is not { Enabled: true })
            {
                return false;
            }

#if PRE_V1_37_1
            CustomData? customData = sceneSetupData.difficultyBeatmap?.GetBeatmapCustomData();
#else
            CustomData? customData = sceneSetupData.beatmapLevel?.GetBeatmapCustomData(sceneSetupData.beatmapKey);
#endif
            if (customData == null)
            {
                return false;
            }

            var requirements =
                customData.Get<List<object>>("_requirements")?.Cast<string>().ToArray()
                ?? Array.Empty<string>();
            if (requirements.Any(n => string.Equals(n, "Chroma", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var suggestions =
                customData.Get<List<object>>("_suggestions")?.Cast<string>().ToArray()
                ?? Array.Empty<string>();
            return requirements
                .Concat(suggestions)
                .Any(n => string.Equals(n, "ChromaGLS", StringComparison.OrdinalIgnoreCase));
        }
    }
}
