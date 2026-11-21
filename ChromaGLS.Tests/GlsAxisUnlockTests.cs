using System;
using System.Collections.Generic;
using System.Reflection;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using NUnit.Framework;
using Tweening;
using UnityEngine;
using Zenject;

namespace ChromaGLS.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GlsAxisUnlockTests
{
    private const int GroupId = 17;

    private static readonly Action<IntPtr, bool> IgnoreBehaviourEnabled = (_, _) => { };
    private static readonly Func<int> FrameCount = () => 0;

    private static readonly List<(Transform Target, Quaternion Orientation)> RotationWrites = new();
    private static LightTranslationGroupEffect.InitData? _translation;
    private static LightRotationGroupEffect.InitData? _rotation;

    private Harmony _harmony = null!;

    [OneTimeSetUp]
    public void RegisterTweeningStubs()
    {
        Program.RegisterInternalCall("UnityEngine.Behaviour::set_enabled", IgnoreBehaviourEnabled);
        Program.RegisterInternalCall("UnityEngine.Behaviour::set_enabled_Injected", IgnoreBehaviourEnabled);
        Program.RegisterInternalCall("UnityEngine.Time::get_frameCount", FrameCount);
    }

    [SetUp]
    public void SetUp()
    {
        _harmony = new Harmony("com.eklipz.ChromaGLS.axis-unlock-test");
        Assembly plugin = Assembly.Load("ChromaGLS");
        foreach (string name in new[] { "GlsAxisUnlockScope", "GlsTranslationAxisPatch", "GlsRotationAxisPatch" })
        {
            Type patch = plugin.GetType($"ChromaGLS.HarmonyPatches.{name}", throwOnError: true)!;
            _harmony.CreateClassProcessor(patch).Patch();
        }
    }

    [TearDown]
    public void TearDown()
    {
        _harmony.UnpatchSelf();
        _translation = null;
        _rotation = null;
        RotationWrites.Clear();
    }

    [TestCase(null, null, false)]
    [TestCase("_requirements", "Chroma", false)]
    [TestCase("_suggestions", "OtherMod", false)]
    [TestCase("_requirements", "ChromaGLS", true)]
    [TestCase("_suggestions", "chromagls", true)]
    [TestCase("requirements", "ChromaGLS", true)]
    [TestCase("suggestions", "ChromaGLS", true)]
    public void ManagersUnlockOnlyWhenTheDifficultyDeclaresChromaGls(string? key, string? capability, bool unlock)
    {
        ObserveEffect<LightTranslationGroupEffect, LightTranslationGroupEffect.InitData>(nameof(ObserveTranslation));
        ObserveEffect<LightRotationGroupEffect, LightRotationGroupEffect.InitData>(nameof(ObserveRotation));

        CustomData metadata = new();
        if (key != null)
        {
            metadata[key] = new List<object> { capability! };
        }

        AssertManagerSetup(new CustomBeatmapData(4, metadata, new CustomData(), new CustomData(), new Version(3, 0)), unlock);

        if (unlock)
        {
            // A subsequent stock level must use its own metadata after an opted-in custom difficulty.
            AssertManagerSetup(new BeatmapData(4), unlock: false);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnwiredZComposesWithXThroughSnapsAndTweens(bool mirrored)
    {
        Type rotationPatch = Assembly.Load("ChromaGLS").GetType("ChromaGLS.HarmonyPatches.GlsRotationAxisPatch", true)!;
        _harmony.CreateClassProcessor(rotationPatch.GetNestedType("RotationEffectPatch", BindingFlags.NonPublic)!).Patch();
        _harmony.Patch(AccessTools.PropertySetter(typeof(Transform), nameof(Transform.localRotation)),
            prefix: new HarmonyMethod(typeof(GlsAxisUnlockTests), nameof(CaptureRotation)));
        _harmony.Patch(AccessTools.DeclaredMethod(typeof(Quaternion), nameof(Quaternion.AngleAxis)),
            prefix: new HarmonyMethod(typeof(GlsAxisUnlockTests), nameof(ComputeAngleAxis)));

        CustomData metadata = new() { ["_suggestions"] = new List<object> { "ChromaGLS" } };
        CustomBeatmapData beatmap = new(4, metadata, new CustomData(), new CustomData(), new Version(3, 0));
        using BeatmapCallbacksController callbacks = new(new BeatmapCallbacksController.InitData(beatmap, 0f, false));
        TestTweeningManager tweening = new();
        SetField(tweening, "m_CachedPtr", new IntPtr(5));
        DiContainer container = new();
        container.BindInstance<IReadonlyBeatmapData>(beatmap);
        container.BindInstance<SongTimeTweeningManager>(tweening);
        container.BindInstance(callbacks);

        Transform x = Uninitialized<Transform>();
        Transform y = Uninitialized<Transform>();
        SetField(x, "m_CachedPtr", new IntPtr(1));
        SetField(y, "m_CachedPtr", new IntPtr(3));
        LightRotationGroup group = MakeGroup<LightRotationGroup>(MakeLightGroup(), x);
        SetField(group, "_yTransforms", new List<Transform> { y });
        SetField(group, "_mirrorX", mirrored);
        LightRotationGroupEffectManager manager = StartRotation(group, container);

        float sign = mirrored ? -1f : 1f;
        float quarterTurnComponent = (float)Math.Sqrt(0.5);
        Quaternion xTilt = new(sign * quarterTurnComponent, 0f, 0f, quarterTurnComponent);
        Quaternion yTilt = new(0f, quarterTurnComponent, 0f, quarterTurnComponent);
        Quaternion xAndZ = new(sign * 0.5f, -0.5f, sign * 0.5f, 0.5f);

        try
        {
            callbacks.TriggerBeatmapEvent(RotationCue(0f, LightAxis.Y, 90f));
            callbacks.TriggerBeatmapEvent(RotationCue(0f, LightAxis.X, 90f));
            callbacks.TriggerBeatmapEvent(RotationCue(0f, LightAxis.Z, 90f));
            AssertRotation(x, xAndZ, "Z snap must compose on the X target.");
            AssertRotation(y, yTilt, "The wired Y target keeps its own rotation.");

            LightRotationBeatmapEventData start = RotationCue(1f, LightAxis.Z, 0f);
            start.__ConnectWithNextSameTypeEventData(RotationCue(3f, LightAxis.Z, 180f, EaseType.Linear));
            callbacks.TriggerBeatmapEvent(start);
            AssertRotation(x, xTilt, "Starting the Z tween keeps the X tilt.");
            Tick(tweening, 2f);
            AssertRotation(x, xAndZ, "Halfway through the tween, Z should be 90 degrees.");
            AssertRotation(y, yTilt, "The Z tween leaves the wired Y target alone.");

            callbacks.TriggerBeatmapEvent(RotationCue(2.5f, LightAxis.Z, 0f));
            Tick(tweening, 2.75f);
            AssertRotation(x, xTilt, "A snap must cancel the active Z tween.");
        }
        finally
        {
            AccessTools.DeclaredMethod(typeof(LightRotationGroupEffectManager), "OnDestroy").Invoke(manager, null);
        }

        int writesBeforeCleanupCue = RotationWrites.Count;
        callbacks.TriggerBeatmapEvent(RotationCue(3f, LightAxis.Z, 90f));
        Assert.That(RotationWrites.Count, Is.EqualTo(writesBeforeCleanupCue), "Destroying the manager removes the unlocked callback.");
    }

    private static LightRotationBeatmapEventData RotationCue(float time, LightAxis axis, float angle, EaseType ease = EaseType.None) =>
        new(time, GroupId, 0, false, ease, axis, angle, 0, LightRotationDirection.Automatic);

    private static void Tick(TestTweeningManager tweening, float time)
    {
        tweening.SongTime = time;
        AccessTools.DeclaredMethod(typeof(TweeningManager), "LateUpdate").Invoke(tweening, null);
    }

    private static void AssertRotation(Transform target, Quaternion expected, string message)
    {
        int write = RotationWrites.FindLastIndex(entry => ReferenceEquals(entry.Target, target));
        Assert.That(write, Is.GreaterThanOrEqualTo(0), message);
        // A quaternion and its negation represent the same orientation.
        Assert.That(Math.Abs(Quaternion.Dot(RotationWrites[write].Orientation, expected)), Is.EqualTo(1f).Within(0.00001f), message);
    }

    private static bool CaptureRotation(Transform __instance, Quaternion value)
    {
        RotationWrites.Add((__instance, value));
        return false;
    }

    private static bool ComputeAngleAxis(float angle, Vector3 axis, ref Quaternion __result)
    {
        double half = angle * Math.PI / 360d;
        float sine = (float)Math.Sin(half);
        __result = new Quaternion(axis.x * sine, axis.y * sine, axis.z * sine, (float)Math.Cos(half));
        return false;
    }

    private static void AssertManagerSetup(IReadonlyBeatmapData beatmap, bool unlock)
    {
        DiContainer container = new();
        container.BindInstance<IReadonlyBeatmapData>(beatmap);
        container.BindInstance(Uninitialized<SongTimeTweeningManager>());
        container.BindInstance(Uninitialized<BeatmapCallbacksController>());

        Transform target = Uninitialized<Transform>();
        SetField(target, "m_CachedPtr", new IntPtr(1));
        LightGroup group = MakeLightGroup();

        _translation = null;
        LightTranslationGroup translationGroup = MakeGroup<LightTranslationGroup>(group, target);
        LightTranslationGroupEffectManager translationManager = Uninitialized<LightTranslationGroupEffectManager>();
        SetField(translationManager, "_lightTranslationGroups", new[] { translationGroup });
        SetField(translationManager, "_lightTranslationGroupEffects", new List<LightTranslationGroupEffect>());
        Start(translationManager, container);

        _rotation = null;
        LightRotationGroup rotationGroup = MakeGroup<LightRotationGroup>(group, target);
        StartRotation(rotationGroup, container);

        Assert.That(_translation, Is.Not.Null);
        Assert.That(_rotation, Is.Not.Null);
        Transform? expectedAlias = unlock ? target : null;
        Assert.Multiple(() =>
        {
            Assert.That(ReferenceEquals(_translation!.xTransform, target), Is.True, "Translation keeps the wired X target.");
            Assert.That(ReferenceEquals(_translation.yTransform, expectedAlias), Is.True, "Translation alias on Y.");
            Assert.That(ReferenceEquals(_translation.zTransform, expectedAlias), Is.True, "Translation alias on Z.");
            Assert.That(_rotation!.axis, Is.EqualTo(LightAxis.X));
            Assert.That(ReferenceEquals(_rotation.transform, target), Is.True, "Rotation keeps the wired X target.");
            if (unlock)
            {
                MethodInfo subtypeIds = AccessTools.DeclaredMethod(_rotation.GetType(), "GetSubtypeIdentifiers");
                Assert.That(subtypeIds.Invoke(_rotation, null), Is.EquivalentTo(new[]
                {
                    LightRotationBeatmapEventData.SubtypeIdentifier(GroupId, 0, LightAxis.X),
                    LightRotationBeatmapEventData.SubtypeIdentifier(GroupId, 0, LightAxis.Y),
                    LightRotationBeatmapEventData.SubtypeIdentifier(GroupId, 0, LightAxis.Z),
                }));
            }
            else
            {
                Assert.That(_rotation, Is.TypeOf<LightRotationGroupEffect.InitData>(), "Rotation should keep the stock single-axis effect.");
            }
        });
    }

    private void ObserveEffect<TEffect, TInitData>(string observer)
    {
        // Capture the result of the native manager's setup before the constructor calls the Unity engine.
        ConstructorInfo ctor = AccessTools.DeclaredConstructor(
            typeof(TEffect), [typeof(TInitData), typeof(SongTimeTweeningManager), typeof(BeatmapCallbacksController)]);
        _harmony.Patch(ctor, prefix: new HarmonyMethod(typeof(GlsAxisUnlockTests), observer));
    }

    private static bool ObserveTranslation(LightTranslationGroupEffect.InitData initData)
    {
        _translation = initData;
        return false;
    }

    private static bool ObserveRotation(LightRotationGroupEffect.InitData initData)
    {
        _rotation = initData;
        return false;
    }

    private static LightGroup MakeLightGroup()
    {
        LightGroupSO definition = Uninitialized<LightGroupSO>();
        SetField(definition, "m_CachedPtr", new IntPtr(2));
        SetField(definition, "_groupId", GroupId);
        SetField(definition, "_numberOfElements", 1);
        LightGroup group = Uninitialized<LightGroup>();
        SetField(group, "_lightGroupSO", definition);
        return group;
    }

    private static LightRotationGroupEffectManager StartRotation(LightRotationGroup rotationGroup, DiContainer container)
    {
        LightRotationGroupEffectManager rotationManager = Uninitialized<LightRotationGroupEffectManager>();
        SetField(rotationManager, "_lightRotationGroups", new[] { rotationGroup });
        SetField(rotationManager, "_lightRotationGroupEffects", new List<LightRotationGroupEffect>());
        Start(rotationManager, container);
        return rotationManager;
    }

    private static T MakeGroup<T>(LightGroup group, Transform target)
        where T : LightGroupSubsystem
    {
        T effectGroup = Uninitialized<T>();
        SetField(effectGroup, "_lightGroup", group);
        SetField(effectGroup, "_xTransforms", new List<Transform> { target });
        SetField(effectGroup, "_yTransforms", new List<Transform>());
        SetField(effectGroup, "_zTransforms", new List<Transform>());
        return effectGroup;
    }

    private static void Start(object manager, DiContainer container)
    {
        SetField(manager, "_container", container);
        AccessTools.DeclaredMethod(manager.GetType(), "Start").Invoke(manager, null);
    }

    private static T Uninitialized<T>() => (T)TestHostCompat.GetUninitializedObject(typeof(T));

    private static void SetField(object instance, string name, object value) =>
        AccessTools.Field(instance.GetType(), name).SetValue(instance, value);

    private sealed class TestTweeningManager : SongTimeTweeningManager
    {
        internal float SongTime { get; set; }

        protected override float GetTime() => SongTime;
    }
}
