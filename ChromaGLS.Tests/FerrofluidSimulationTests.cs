using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidSimulationTests
{
    // DensityTuningFitsExistingStateTextures keeps both lattice axes proportional and bounds every live count to the preallocated 32-by-32 GPU storage.
    [Test]
    public void DensityTuningFitsExistingStateTextures()
    {
        Type simulation = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        // EnvironmentPrimitiveSpikesAreRoundCones added a shape-aware overload, so the torus-only entry point must be selected by its single-parameter signature.
        MethodInfo? countsMethod = simulation.GetMethod(
            "EvaluateDensityLattice",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(float)],
            null);
        Assert.That(countsMethod, Is.Not.Null, "Density must change the active lattice, not merely the pressure/mass setting.");
        Vector2Int defaults = (Vector2Int)countsMethod!.Invoke(null, [1f])!;
        Assert.That(defaults, Is.EqualTo(new Vector2Int(48, 12)));
        for (int step = 0; step <= 125; step++)
        {
            Vector2Int counts = (Vector2Int)countsMethod.Invoke(null, [0.25f + (step * 0.01f)])!;
            Assert.That(counts.x * counts.y, Is.InRange(144, 1024));
        }

        Assert.That(countsMethod.Invoke(null, [-1f]), Is.EqualTo(new Vector2Int(24, 6)));
        Assert.That(countsMethod.Invoke(null, [100f]), Is.EqualTo(countsMethod.Invoke(null, [1.5f])));
    }

    // SpikeShapeMultipliersPreserveDefaultAndActIndependently verifies that widening divides the inverse cone width while tallness multiplies only the extrusion, not the reservoir or magnetic cap.
    [Test]
    public void SpikeShapeMultipliersPreserveDefaultAndActIndependently()
    {
        Type simulation = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        MethodInfo? shape = simulation.GetMethod("EvaluateSpikeShape", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(shape, Is.Not.Null, "Base radius and overall tallness need separate controls from the maximum magnetic response.");
        Vector2 defaults = (Vector2)shape!.Invoke(null, [1f, 1f])!;
        Vector2 wide = (Vector2)shape.Invoke(null, [2f, 1f])!;
        Vector2 tall = (Vector2)shape.Invoke(null, [1f, 2f])!;
        Assert.That(defaults.x, Is.EqualTo(16.25f));
        Assert.That(defaults.y, Is.EqualTo(0.42f).Within(0.00001f));
        Assert.That(wide.x, Is.EqualTo(defaults.x / 2f));
        Assert.That(wide.y, Is.EqualTo(defaults.y));
        Assert.That(tall.x, Is.EqualTo(defaults.x));
        Assert.That(tall.y, Is.EqualTo(defaults.y * 2f));
    }

    // SpikeBaseCutoffSuppressesVoronoiBoundaryWebbing requires the low residual floor between widened cones to reach zero without flattening their apexes.
    [Test]
    public void SpikeBaseCutoffSuppressesVoronoiBoundaryWebbing()
    {
        Type simulation = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        MethodInfo? cutoff = simulation.GetMethod("EvaluateSpikeBaseCutoff", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(cutoff, Is.Not.Null, "A bounded base cutoff must collapse only the residual membrane between spike cells.");
        Assert.That((float)cutoff!.Invoke(null, [-1f])!, Is.EqualTo(0f));
        Assert.That((float)cutoff.Invoke(null, [0.015f])!, Is.EqualTo(0.015f).Within(0.0001f));
        Assert.That((float)cutoff.Invoke(null, [1f])!, Is.EqualTo(0.1f));
    }

    // MagnetPlaneFollowsRenderedCurvedRectangle rejects an uncurved source plane while preserving the flat-preview normal, including mirrored viewer-facing depth.
    [Test]
    public void MagnetPlaneFollowsRenderedCurvedRectangle()
    {
        Type effect = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        MethodInfo? frame = effect.GetMethod("EvaluateRenderedMagnetPlane", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(frame, Is.Not.Null, "The magnet plane must be derived from rendered geometry, not the uncurved RectTransform.");
        Rect rect = new(-6f, -3.5f, 12f, 7f);
        Matrix4x4 localToCanvas = Matrix4x4.identity;
        localToCanvas.m03 = 70f;
        Matrix4x4 canvasToLocal = Matrix4x4.identity;
        canvasToLocal.m03 = -70f;
        object[] parameters = [rect, new Vector2(0.5f, 0.5f), 0.18f, true, localToCanvas, canvasToLocal, 140f, Matrix4x4.identity, -1f, 0.0784f, Vector3.zero, Vector3.zero];
        frame!.Invoke(null, parameters);
        Vector3 point = (Vector3)parameters[10];
        Vector3 normal = (Vector3)parameters[11];
        Vector3 expectedNormal = new(-Mathf.Sin(0.5f), 0f, -Mathf.Cos(0.5f));
        Vector3 expectedCenter = new((140f * Mathf.Sin(0.5f)) - 70f, 0f, (140f * Mathf.Cos(0.5f)) - 140f);
        Assert.That(Vector3.Distance(normal, expectedNormal), Is.LessThan(0.002f));
        Assert.That(Vector3.Distance(point, expectedCenter + (expectedNormal * 0.0784f)), Is.LessThan(0.002f));
        parameters[3] = false;
        frame.Invoke(null, parameters);
        Assert.That(Vector3.Distance((Vector3)parameters[10], new Vector3(0f, 0f, -0.0784f)), Is.LessThan(0.0001f));
        Assert.That(Vector3.Distance((Vector3)parameters[11], Vector3.back), Is.LessThan(0.0001f));
    }

    // MagnetPlaneFallbackPreservesPreviewIntersection covers the live preview's degenerate transformed X/Y basis: a zero cross product must fall back to the viewer-facing forward axis instead of disabling every saber ray.
    [Test]
    public void MagnetPlaneFallbackPreservesPreviewIntersection()
    {
        Type effect = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        MethodInfo? frame = effect.GetMethod("EvaluateRenderedMagnetPlane", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(frame, Is.Not.Null);
        Rect rect = new(-18f, -4f, 36f, 8f);
        Matrix4x4 degenerateWorld = Matrix4x4.identity;
        degenerateWorld.m01 = 1f;
        degenerateWorld.m11 = 0f;
        object[] parameters = [rect, new Vector2(0.5f, 0.5f), 0f, false, Matrix4x4.identity, Matrix4x4.identity, 0f, degenerateWorld, -1f, 0.0784f, Vector3.zero, Vector3.zero];
        frame!.Invoke(null, parameters);

        Assert.That(Vector3.Distance((Vector3)parameters[10], new Vector3(0f, 0f, -0.0784f)), Is.LessThan(0.0001f));
        Assert.That(Vector3.Distance((Vector3)parameters[11], Vector3.back), Is.LessThan(0.0001f));
    }

    // RequirementMagnetUsesSourceSurfaceGap prevents the curved button's shallow local depth from leaving the physical field 0.09-0.14 m away while the deeper preview gets a strong near-surface spike.
    [Test]
    public void RequirementMagnetUsesSourceSurfaceGap()
    {
        Type effect = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        MethodInfo? gap = effect.GetMethod("EvaluateMagnetSurfaceGapWorldDistance", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(gap, Is.Not.Null, "The shader field point must sit a source-equivalent distance above the mapped torus surface, not on the pointer-intersection plane.");

        Assert.That((float)gap!.Invoke(null, [0.28f])!, Is.EqualTo(0.14f * 0.28f).Within(0.0001f));
        Assert.That((float)gap.Invoke(null, [-1f])!, Is.EqualTo(0f));

        MethodInfo? position = effect.GetMethod("EvaluateMagnetSurfacePosition", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(position, Is.Not.Null, "Tangent pointer offset must remain intact while the configured intersection plane's role-specific depth is replaced with the shared source gap.");
        Vector3 field = (Vector3)position!.Invoke(null, [new Vector3(1.1f, 2f, 3.2f), new Vector3(1f, 2f, 3f), Vector3.forward, 0.28f])!;
        Assert.That(Vector3.Distance(field, new Vector3(1.1f, 2f, 3.0392f)), Is.LessThan(0.0001f));
    }

    // RequirementTorusRecessesBehindButtonEdge keeps the requested 0.1 world-unit recession exact on the scaled HMUI canvas: the effect transform must shift backward in world units, not local canvas units, and a degenerate parent scale must not NaN the placement.
    [Test]
    public void RequirementTorusRecessesBehindButtonEdge()
    {
        Type effect = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        MethodInfo? offset = effect.GetMethod("EvaluateRecessionLocalOffset", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(offset, Is.Not.Null, "The world-unit recession must convert through the parent's forward scale so the torus actually sits WorldRecessionDepth world units behind the button face.");
        FieldInfo? depth = effect.GetField("WorldRecessionDepth", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(depth, Is.Not.Null, "The recession depth must remain a single named constant so the placement cannot drift between code paths.");
        float worldDepth = (float)depth!.GetValue(null)!;

        Assert.That((float)offset!.Invoke(null, [1f])!, Is.EqualTo(worldDepth).Within(0.0001f));
        Assert.That((float)offset.Invoke(null, [0.01f])!, Is.EqualTo(worldDepth * 100f).Within(0.001f), "A typical 0.01-scaled canvas needs a hundred local units per world unit of recession.");
        Assert.That(float.IsInfinity((float)offset.Invoke(null, [0f])!), Is.False, "A degenerate parent scale must clamp instead of producing an infinite offset.");
        Assert.That((float)offset.Invoke(null, [-0.02f])!, Is.GreaterThan(0f), "Negative scales report magnitude only; the caller still owes the viewer-facing sign.");
    }

    // RequirementTorusHidesWithButtonAlpha keeps the torus's fade target tied to the button's actual rendered state: a SetActive(false) kill already propagates through the hierarchy, but the ~0.5s content/screen fades only animate CanvasGroup alpha, which the 3D mesh ignores entirely and left the torus hanging in mid-air after the button's graphics vanished. PlayPracticeFadeDissolvesOnScreenDoor binds the dissolve to the inherited alpha itself so the torus fades out while the menu slides and fades back in while it slides back, rather than starting a separately-timed dissolve that reads as a late fade.
    [Test]
    public void RequirementTorusHidesWithButtonAlpha()
    {
        Type effect = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        MethodInfo? fadeTarget = effect.GetMethod(
            "EvaluateFadeTargetAlpha",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(bool), typeof(float)],
            null);
        Assert.That(fadeTarget, Is.Not.Null, "The per-frame fade gate must combine SongCore's button flag with the BG graphic's inherited canvas alpha.");

        Assert.That((float)fadeTarget!.Invoke(null, [true, 1f])!, Is.EqualTo(1f), "A fully rendered flagged button keeps the torus fully opaque.");
        Assert.That((float)fadeTarget.Invoke(null, [true, 0f])!, Is.EqualTo(0f), "A fully faded canvas group must dissolve the torus to zero even while every GameObject stays active.");
        Assert.That((float)fadeTarget.Invoke(null, [false, 1f])!, Is.EqualTo(0f), "SongCore clearing ButtonGlowColor must fade the torus out without waiting for the metadata-writer postfix.");
        Assert.That((float)fadeTarget.Invoke(null, [false, 0f])!, Is.EqualTo(0f));
        // PlayPracticeFadeDissolvesOnScreenDoor alpha binding: mid-transition alpha maps straight through so the dissolve tracks the slide in both directions and a frozen state cannot linger.
        Assert.That((float)fadeTarget.Invoke(null, [true, 0.99f])!, Is.EqualTo(0.99f).Within(0.0001f), "The first frame of the screen fade must start the dissolve instead of letting the torus ghost-slide through the transition.");
        Assert.That((float)fadeTarget.Invoke(null, [true, 0.5f])!, Is.EqualTo(0.5f).Within(0.0001f), "Mid-slide alpha must drive a mid-dissolve so the fade tracks the slide rather than waiting for the button's final position.");
        Assert.That((float)fadeTarget.Invoke(null, [true, 0.0005f])!, Is.LessThan(0.001f), "A hairline of alpha keeps the torus under the renderer threshold until the button's graphics meaningfully return.");

        MethodInfo? onEnable = effect.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(onEnable, Is.Not.Null, "Reactivating the effect GameObject must resync the dissolve to the current alpha so a mid-fade SetActive(false) cannot ghost a solid torus in at slide-in.");

        // PlayPracticeFadeDissolvesOnScreenDoor front-loads only the fade-out: a linear screen-door dissolve still reads solid until roughly half the fragments clip, so the shader value runs through a cubic while the target is falling.
        MethodInfo? shaderFade = effect.GetMethod("EvaluateShaderFadeAlpha", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(shaderFade, Is.Not.Null, "The fade-out needs a front-loaded shader curve so the dissolve reads from the click's first frames instead of the slide's midpoint.");
        Assert.That((float)shaderFade!.Invoke(null, [0.8f, 0.5f])!, Is.EqualTo(0.512f).Within(0.0001f), "Fading out must cube the dissolve level so the torus visibly thins immediately.");
        Assert.That((float)shaderFade.Invoke(null, [0.5f, 0.8f])!, Is.EqualTo(0.5f).Within(0.0001f), "Fading in must stay linear — the slide-in dissolve already reads correctly.");
        Assert.That((float)shaderFade.Invoke(null, [0.5f, 0.5f])!, Is.EqualTo(0.5f).Within(0.0001f), "Steady alpha must pass through unmodified.");
    }

    [Test]
    public void TuningMenuKeepsRequirementTorusVisibleWithoutMetadata()
    {
        Type effect = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        MethodInfo? shouldShow = effect.GetMethod(
            "ShouldShowEffect",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(bool), typeof(bool), typeof(bool)],
            null);
        Assert.That(shouldShow, Is.Not.Null, "The tuning tab needs one shared visibility decision so the button-hosted torus stays up without map metadata.");
        Assert.That(shouldShow!.Invoke(null, [false, true, true]), Is.EqualTo(false), "The feature toggle must win over the tuning override.");
        Assert.That(shouldShow.Invoke(null, [false, false, true]), Is.EqualTo(false), "A disabled ferrofluid effect must stay off even inside the tuning tab.");
        Assert.That(shouldShow.Invoke(null, [false, false, false]), Is.EqualTo(false));
        Assert.That(shouldShow.Invoke(null, [true, true, false]), Is.EqualTo(true), "Normal metadata gating must keep working outside the tuning tab.");
        Assert.That(shouldShow.Invoke(null, [true, true, true]), Is.EqualTo(true));
        Assert.That(shouldShow.Invoke(null, [true, false, true]), Is.EqualTo(true), "Opening the tuning tab must keep the torus visible on maps without ChromaGLS metadata.");
        Assert.That(shouldShow.Invoke(null, [true, false, false]), Is.EqualTo(false), "Closing the tuning tab must restore metadata gating.");

        MethodInfo? shouldShowButton = effect.GetMethod(
            "ShouldShowInfoButton",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(bool), typeof(bool), typeof(bool)],
            null);
        Assert.That(shouldShowButton, Is.Not.Null, "SongCore deactivates the whole info button on metadata-free maps, so the tuning tab must re-activate it while the torus needs the host.");
        Assert.That(shouldShowButton!.Invoke(null, [false, true, true]), Is.EqualTo(true), "The tuning tab must activate the info button when the torus needs it on a metadata-free map.");
        Assert.That(shouldShowButton.Invoke(null, [false, false, true]), Is.EqualTo(false), "A disabled ferrofluid effect must not force SongCore's button active.");
        Assert.That(shouldShowButton.Invoke(null, [false, true, false]), Is.EqualTo(false), "Outside the tuning tab, SongCore's native deactivation must stand.");
        Assert.That(shouldShowButton.Invoke(null, [true, false, false]), Is.EqualTo(true), "Native button visibility must always pass through.");
        Assert.That(shouldShowButton.Invoke(null, [true, false, true]), Is.EqualTo(true));
        Assert.That(shouldShowButton.Invoke(null, [true, true, true]), Is.EqualTo(true));
    }

    [Test]
    public void TuningMenuFadeIgnoresMissingGlowFlagButKeepsCanvasAlpha()
    {
        Type effect = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        MethodInfo? fadeTarget = effect.GetMethod(
            "EvaluateFadeTargetAlpha",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(bool), typeof(float), typeof(bool)],
            null);
        Assert.That(fadeTarget, Is.Not.Null, "The tuning tab must treat the torus as flagged-visible while still honoring the button's canvas fade.");
        Assert.That((float)fadeTarget!.Invoke(null, [false, 0.6f, true])!, Is.EqualTo(0.6f).Within(0.0001f), "SongCore's unglowed flag on a metadata-free map must not hide the tuning torus.");
        Assert.That((float)fadeTarget.Invoke(null, [false, 0f, true])!, Is.EqualTo(0f), "A faded menu canvas must still hide the torus inside the tuning tab.");
        Assert.That((float)fadeTarget.Invoke(null, [false, 1f, false])!, Is.EqualTo(0f), "Without the tuning tab the glow flag must still gate the fade.");
        Assert.That((float)fadeTarget.Invoke(null, [true, 0.4f, false])!, Is.EqualTo(0.4f).Within(0.0001f), "A flagged button outside the tab must keep passing canvas alpha through.");
        Assert.That((float)fadeTarget.Invoke(null, [true, 0.4f, true])!, Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That((float)fadeTarget.Invoke(null, [false, 1.4f, true])!, Is.EqualTo(1f), "The tuning override must keep the fade target clamped.");
    }

    // Parallel and backward rays cannot move the pointer magnet behind the rectangle.
    [Test]
    public void VirtualSaberRayRejectsParallelAndBackwardPlaneIntersections()
    {
        Type magnetType = GetPluginType("ChromaGLS.Settings.FerrofluidMagnetController");
        MethodInfo distance = magnetType.GetMethod("EvaluateForwardPlaneIntersectionDistance", BindingFlags.Static | BindingFlags.NonPublic)!;
        Vector3 planePoint = new(0f, 0f, 2f);
        Vector3 planeNormal = Vector3.back;

        float leftDistance = (float)distance.Invoke(null, [Vector3.zero, Vector3.forward, planePoint, planeNormal])!;
        float rightDistance = (float)distance.Invoke(null, [new Vector3(0f, 0f, 1f), Vector3.forward, planePoint, planeNormal])!;
        float parallelDistance = (float)distance.Invoke(null, [Vector3.zero, Vector3.right, planePoint, planeNormal])!;
        float backwardDistance = (float)distance.Invoke(null, [Vector3.zero, Vector3.back, planePoint, planeNormal])!;

        Assert.That(leftDistance, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(rightDistance, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(float.IsPositiveInfinity(parallelDistance), Is.True);
        Assert.That(float.IsPositiveInfinity(backwardDistance), Is.True);
    }

    // VirtualPointerUsesViewAnchorRay matches Beat Saber's laser-pointer ray so the distant SongCore button follows the same cursor source as the nearby settings preview.
    [Test]
    public void VirtualPointerUsesViewAnchorRay()
    {
        Type magnetType = GetPluginType("ChromaGLS.Settings.FerrofluidMagnetController");
        MethodInfo? resolve = magnetType.GetMethod("ResolveVirtualPointerTransform", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(resolve, Is.Not.Null, "The magnet must resolve each VRController's viewAnchorTransform instead of using the controller root.");
    }

    // RequirementRectanglePlacementIncludesButtonPadding verifies the requested ten-percent-height border offset while retaining the established deep tube radii.
    [Test]
    public void RequirementRectanglePlacementIncludesButtonPadding()
    {
        Type simulationType = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        MethodInfo evaluateGeometry = simulationType.GetMethod("EvaluateGeometry", BindingFlags.Static | BindingFlags.NonPublic)!;
        object geometry = evaluateGeometry.Invoke(null, [new Rect(-17.9f, -3.9f, 35.8f, 7.8f)])!;
        Type geometryType = geometry.GetType();
        Vector2 halfExtents = (Vector2)geometryType.GetProperty("HalfExtents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(geometry)!;
        float cornerRadius = (float)geometryType.GetProperty("CornerRadius", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(geometry)!;
        float radialRadius = GetConstant<float>(simulationType, "RadialRadius");

        Assert.That(halfExtents.x, Is.EqualTo(18.58f).Within(0.0001f), "The GPU centerline must expand horizontally by 0.78 units on each side.");
        Assert.That(halfExtents.y, Is.EqualTo(4.58f).Within(0.0001f), "The GPU centerline must expand vertically by 0.78 units on each side.");
        Assert.That(cornerRadius / halfExtents.y, Is.EqualTo(0.11f / 0.2625f).Within(0.0001f), "Only the source rounded-corner ratio should replace the old superellipse shape.");
        Assert.That(radialRadius, Is.EqualTo(1.38f).Within(0.0001f), "The already-correct ChromaGLS tube radius must remain unchanged.");
    }

    // RequirementBorderPaddingMovesCenterlineOutsideButtonFace keeps the same rendered tube but shifts its centerline outward by ten percent of the button height so the fluid stops covering the button face.
    [Test]
    public void RequirementBorderPaddingMovesCenterlineOutsideButtonFace()
    {
        Type simulationType = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        MethodInfo evaluateGeometry = simulationType.GetMethod("EvaluateGeometry", BindingFlags.Static | BindingFlags.NonPublic)!;
        object geometry = evaluateGeometry.Invoke(null, [new Rect(-6f, -3.5f, 12f, 7f)])!;
        Vector2 halfExtents = (Vector2)geometry.GetType().GetProperty("HalfExtents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(geometry)!;

        Assert.That(halfExtents.x, Is.EqualTo(6.6f).Within(0.0001f), "The horizontal centerline radius must move outward by 0.7 units, ten percent of a seven-unit button height.");
        Assert.That(halfExtents.y, Is.EqualTo(4.1f).Within(0.0001f), "The vertical centerline radius must move outward by the same 0.7 units on both sides.");
    }

    // RoundedRectangleCenterlineIsPeriodicAndArcLengthOrdered protects the shared CPU/HLSL topology that keeps particles, height samples, and ray mapping continuous through all four corners.
    [Test]
    public void RoundedRectangleCenterlineIsPeriodicAndArcLengthOrdered()
    {
        Type simulationType = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        MethodInfo sample = simulationType.GetMethod("EvaluateRoundedRectangleCenterline", BindingFlags.Static | BindingFlags.NonPublic)!;
        Vector2 center = new(1.5f, -0.25f);
        Vector2 halfExtents = new(5f, 2f);
        const float cornerRadius = 0.75f;
        float horizontalLength = (halfExtents.x - cornerRadius) * 2f;
        float verticalLength = (halfExtents.y - cornerRadius) * 2f;
        float cornerLength = cornerRadius * Mathf.PI * 0.5f;
        float perimeterLength = (horizontalLength + verticalLength + (cornerLength * 2f)) * 2f;

        Vector2 start = InvokeVector2(sample, 0f, center, halfExtents, cornerRadius);
        Vector2 wrapped = InvokeVector2(sample, 1f, center, halfExtents, cornerRadius);
        Vector2 topRightCornerStart = InvokeVector2(sample, horizontalLength / perimeterLength, center, halfExtents, cornerRadius);
        Vector2 rightEdgeStart = InvokeVector2(sample, (horizontalLength + cornerLength) / perimeterLength, center, halfExtents, cornerRadius);

        Assert.That(Vector2.Distance(start, wrapped), Is.LessThan(0.0001f), "The perimeter seam must close exactly.");
        Assert.That(start.x, Is.EqualTo(center.x - halfExtents.x + cornerRadius).Within(0.0001f));
        Assert.That(start.y, Is.EqualTo(center.y + halfExtents.y).Within(0.0001f));
        Assert.That(topRightCornerStart.x, Is.EqualTo(center.x + halfExtents.x - cornerRadius).Within(0.0001f));
        Assert.That(topRightCornerStart.y, Is.EqualTo(center.y + halfExtents.y).Within(0.0001f));
        Assert.That(rightEdgeStart.x, Is.EqualTo(center.x + halfExtents.x).Within(0.0001f));
        Assert.That(rightEdgeStart.y, Is.EqualTo(center.y + halfExtents.y - cornerRadius).Within(0.0001f));
    }

    // RoundedRectangleOutwardMatchesStraightEdgesAndCorners prevents saber picking from using a different tube cross-section than the surface shader.
    [Test]
    public void RoundedRectangleOutwardMatchesStraightEdgesAndCorners()
    {
        Type simulationType = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        MethodInfo outward = simulationType.GetMethod("EvaluateRoundedRectangleOutward", BindingFlags.Static | BindingFlags.NonPublic)!;
        Vector2 center = Vector2.zero;
        Vector2 halfExtents = new(5f, 2f);
        const float cornerRadius = 0.75f;

        Vector2 top = InvokeVector2(outward, new Vector2(0f, 2f), center, halfExtents, cornerRadius);
        Vector2 right = InvokeVector2(outward, new Vector2(5f, 0f), center, halfExtents, cornerRadius);
        Vector2 corner = InvokeVector2(outward, new Vector2(5f, 2f), center, halfExtents, cornerRadius);

        Assert.That(Vector2.Angle(top, Vector2.up), Is.LessThan(0.01f));
        Assert.That(Vector2.Angle(right, Vector2.right), Is.LessThan(0.01f));
        Assert.That(Vector2.Angle(corner, new Vector2(1f, 1f)), Is.LessThan(0.01f));
    }

    // MagnetForwardDistanceScalesWithRenderedTorus keeps the source z=0.28 cursor plane proportionate after fitting the prototype onto either the SongCore button or its settings preview.
    [Test]
    public void MagnetForwardDistanceScalesWithRenderedTorus()
    {
        Type effectType = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        MethodInfo evaluate = effectType.GetMethod("EvaluateMagnetForwardWorldDistance", BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.That((float)evaluate.Invoke(null, [0.28f, 0.28f])!, Is.EqualTo(0.0784f).Within(0.0001f));
        Assert.That((float)evaluate.Invoke(null, [0.28f, 0.142f])!, Is.EqualTo(0.03976f).Within(0.0001f));
        Assert.That((float)evaluate.Invoke(null, [-1f, 0.28f])!, Is.EqualTo(0f));
    }

    // CrossSectionMappingRemovesTheRetainedRadialCenterOffset prevents the saber intersection from measuring the target tube against a centerline displaced from its actual rendered ellipse.
    [Test]
    public void CrossSectionMappingRemovesTheRetainedRadialCenterOffset()
    {
        Type simulationType = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        MethodInfo evaluate = simulationType.GetMethod("EvaluateCrossSectionAngle", BindingFlags.Static | BindingFlags.NonPublic)!;
        const float radialCenter = 0.2f;
        const float radialRadius = 1.38f;

        float outer = (float)evaluate.Invoke(null, [radialCenter + (radialRadius * 2f), radialCenter, radialRadius])!;
        float middle = (float)evaluate.Invoke(null, [radialCenter, radialCenter, radialRadius])!;
        float inner = (float)evaluate.Invoke(null, [radialCenter - (radialRadius * 2f), radialCenter, radialRadius])!;

        Assert.That(outer, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(middle, Is.EqualTo(Mathf.PI * 0.5f).Within(0.0001f));
        Assert.That(inner, Is.EqualTo(Mathf.PI).Within(0.0001f));
    }

    // TorusSurfaceMeshUsesLowerDefaultCost locks the inexpensive opt-out torus grid separately from the deliberately denser opt-in primitive grid.
    [Test]
    public void SourceParticleAndHeightMapTopologyIsFixed()
    {
        Type simulationType = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");

        Assert.That(GetConstant<int>(simulationType, "ParticleCount"), Is.EqualTo(576));
        Assert.That(GetConstant<int>(simulationType, "FieldColumns"), Is.EqualTo(48));
        Assert.That(GetConstant<int>(simulationType, "FieldRows"), Is.EqualTo(12));
        Assert.That(GetConstant<int>(simulationType, "FieldWindings"), Is.EqualTo(2));
        Assert.That(GetConstant<int>(simulationType, "HeightMapSize"), Is.EqualTo(256));
        Assert.That(GetConstant<int>(simulationType, "TorusSurfaceResolution"), Is.EqualTo(191));
        Assert.That(GetConstant<int>(simulationType, "PrimitiveSurfaceResolution"), Is.EqualTo(448));
        Assert.That(GetConstant<int>(simulationType, "CoarseCandidateMapSize"), Is.EqualTo(16));
    }

    // FerrofluidSimulationRunsAtOneHundredTwentyHertz proves each checkpoint advances half of the tuned 16 ms duration and no rendered frame can accumulate more than the former total response.
    [Test]
    public void SimulationCadenceRunsAtOneHundredTwentyHertzWithoutSpeedingUp()
    {
        Type simulationType = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        float interval = GetConstant<float>(simulationType, "SimulationInterval");
        float deltaTimeMilliseconds = GetConstant<float>(simulationType, "SimulationDeltaTimeMilliseconds");
        int maximumTicksPerFrame = GetConstant<int>(simulationType, "MaximumSimulationTicksPerFrame");

        Assert.That(interval, Is.EqualTo(1f / 120f).Within(0.000001f));
        Assert.That(deltaTimeMilliseconds, Is.EqualTo(8f).Within(0.000001f));
        Assert.That(Mathf.FloorToInt((1f / 60f) / interval), Is.EqualTo(2));
        Assert.That(maximumTicksPerFrame, Is.EqualTo(2));
        Assert.That(deltaTimeMilliseconds * maximumTicksPerFrame, Is.EqualTo(16f).Within(0.000001f));
    }

    // SpikeDisplacementBlendKeepsCellBordersContinuous proves the reported corrosion: every quantity that multiplies spike height must stay continuous across Voronoi texel borders while a magnet sweeps the surface, or the displaced surface cracks open between cells. The CPU mirror evaluates the exact shader deformation at one-sided limits on each side of every border edge, so a point-fetched axis or sharpening factor fails by producing a position jump orders of magnitude above the blended residual.
    [Test]
    public void PrimitiveSpikeDisplacementStaysContinuousWhileMagnetSweeps()
    {
        Type environmentType = GetPluginType("ChromaGLS.Settings.FerrofluidEnvironmentObjects");
        Type deformationType = GetPluginType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation");
        Type simulationType = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        Type configType = GetPluginType("ChromaGLS.Settings.Config");
        Type shapeType = GetPluginType("ChromaGLS.Settings.FerrofluidSurfaceShape");
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        object sphere = Enum.ToObject(shapeType, 2);
        object cube = Enum.ToObject(shapeType, 1);
        MethodInfo evaluateSettings = deformationType.GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateRoundness = environmentType.GetMethod("EvaluateRoundness", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateChartYaw = environmentType.GetMethod("EvaluateChartYawDegrees", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateRestPositions = simulationType.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateSites = environmentType.GetMethod("EvaluatePrimitiveSites", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateSurfacePosition = environmentType.GetMethod("EvaluateSurfacePosition", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateOwner = deformationType.GetMethod("EvaluateTexelSiteOwner", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateSiteDeformation = deformationType.GetMethod("EvaluateSiteDeformation", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateDisplaced = deformationType.GetMethod("EvaluateDisplacedPosition", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateRenderedNormal = deformationType.GetMethod("EvaluateRenderedNormal", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateRawFiniteNormal = deformationType.GetMethod("EvaluateRawFiniteNormal", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateRenderedWinding = deformationType.GetMethod("EvaluateRenderedWinding", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateDensityLattice = simulationType.GetMethod(
            "EvaluateDensityLattice",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(float), shapeType, typeof(float)],
            null)!;
        MethodInfo evaluateShapeDensityScale = simulationType.GetMethod("EvaluateShapeDensityScale", BindingFlags.Static | BindingFlags.NonPublic)!;
        int mapSize = GetConstant<int>(simulationType, "PrimitiveHeightMapSize");

        foreach (object shape in new[] { sphere, cube })
        {
            object settings = evaluateSettings.Invoke(null, [shape, config, 1f])!;
            float roundness = (float)evaluateRoundness.Invoke(null, [shape, config])!;
            float yawDegrees = (float)evaluateChartYaw.Invoke(null, [shape, config])!;
            float density = (float)configType.GetProperty("FerrofluidSpikeDensity")!.GetValue(config)!;
            float shapeDensityScale = (float)evaluateShapeDensityScale.Invoke(null, [shape, config])!;
            Vector2Int lattice = (Vector2Int)evaluateDensityLattice.Invoke(null, [density, shape, shapeDensityScale])!;
            int siteCount = lattice.x * lattice.y;
            Vector2[] restPositions = (Vector2[])evaluateRestPositions.Invoke(null, [shape, siteCount, roundness])!;
            Vector4[] sites = (Vector4[])evaluateSites.Invoke(null, [shape, restPositions, roundness, yawDegrees])!;

            int[] texelOwners = new int[mapSize * mapSize];
            for (int y = 0; y < mapSize; y++)
            {
                for (int x = 0; x < mapSize; x++)
                {
                    texelOwners[(y * mapSize) + x] = (int)evaluateOwner.Invoke(null, [settings, shape, x, y, sites])!;
                }
            }

            var edges = new List<(Vector2 Boundary, Vector2 Offset, Vector3 SurfacePoint)>();
            for (int y = 0; y < mapSize; y++)
            {
                for (int x = 0; x < mapSize; x++)
                {
                    int owner = texelOwners[(y * mapSize) + x];
                    if (x + 1 < mapSize && texelOwners[(y * mapSize) + x + 1] != owner)
                    {
                        Vector2 boundary = new((x + 1f) / mapSize, (y + 0.5f) / mapSize);
                        Vector3 surfacePoint = (Vector3)evaluateSurfacePosition.Invoke(null, [shape, boundary, roundness, yawDegrees])!;
                        edges.Add((boundary, new Vector2(0.0001f / mapSize, 0f), surfacePoint));
                    }

                    if (y + 1 < mapSize && texelOwners[((y + 1) * mapSize) + x] != owner)
                    {
                        Vector2 boundary = new((x + 0.5f) / mapSize, (y + 1f) / mapSize);
                        Vector3 surfacePoint = (Vector3)evaluateSurfacePosition.Invoke(null, [shape, boundary, roundness, yawDegrees])!;
                        edges.Add((boundary, new Vector2(0f, 0.0001f / mapSize), surfacePoint));
                    }
                }
            }

            Assert.That(edges.Count, Is.GreaterThan(200), "The test lattice must produce real Voronoi borders to measure.");

            Vector4[] deformations = new Vector4[siteCount];
            float[] sharpenings = new float[siteCount];
            float[] springScales = new float[siteCount];
            Vector2[] springStates = new Vector2[siteCount];
            for (int index = 0; index < siteCount; index++)
            {
                object[] restArguments = [settings, shape, sites[index], new Vector3(0f, 0f, 100f), 0f, 0f];
                Vector4 restDeformation = (Vector4)evaluateSiteDeformation.Invoke(null, restArguments)!;
                springStates[index] = new Vector2(restDeformation.w, 0f);
                springScales[index] = restDeformation.w;
            }

            float bulgeRadius = (float)settings.GetType().GetField("BulgeRadius", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;

            float maximumGap = 0f;
            float minimumNormalAlignment = 1f;
            Vector2 worstBoundary = Vector2.zero;
            Vector2 worstNormalBoundary = Vector2.zero;
            const int sweepSteps = 48;
            for (int step = 0; step < sweepSteps; step++)
            {
                float angle = step * 0.9f;
                float elevation = Mathf.Sin(step * 0.37f) * 0.8f;
                Vector3 magnetDirection = new(
                    Mathf.Cos(angle) * Mathf.Cos(elevation),
                    Mathf.Sin(elevation),
                    Mathf.Sin(angle) * Mathf.Cos(elevation));
                Vector3 magnet = magnetDirection * 0.64f;
                Vector3 magnetSurface = magnetDirection * 0.5f;
                for (int index = 0; index < siteCount; index++)
                {
                    object[] arguments = [settings, shape, sites[index], magnet, 1.2f, 0f];
                    deformations[index] = (Vector4)evaluateSiteDeformation.Invoke(null, arguments)!;
                    sharpenings[index] = (float)arguments[5]!;

                    // Converging the springs to their targets exercises the worst-case height difference between neighboring cells — the full-elongation regime where the artifacts were reported.
                    springStates[index] = new Vector2(deformations[index].w, 0f);
                    springScales[index] = springStates[index].x;
                }

                foreach ((Vector2 boundary, Vector2 offset, Vector3 surfacePoint) in edges)
                {
                    if (Vector3.Distance(surfacePoint, magnetSurface) > bulgeRadius * 2.5f)
                    {
                        continue;
                    }

                    Vector3 left = (Vector3)evaluateDisplaced.Invoke(
                        null,
                        [settings, shape, boundary - offset, sites, deformations, sharpenings, springScales, texelOwners, magnet, 1.2f, config])!;
                    Vector3 right = (Vector3)evaluateDisplaced.Invoke(
                        null,
                        [settings, shape, boundary + offset, sites, deformations, sharpenings, springScales, texelOwners, magnet, 1.2f, config])!;
                    float gap = Vector3.Distance(left, right);
                    if (gap > maximumGap)
                    {
                        maximumGap = gap;
                        worstBoundary = boundary;
                    }

                    // Only driven cells can shade wrong: resting borders share near-identical frames, so skip edges where neither side is meaningfully elongated.
                    float restScale = 0.44f;
                    float driven = Mathf.Max(
                        springScales[texelOwners[OwnerTexel(boundary - offset)]],
                        springScales[texelOwners[OwnerTexel(boundary + offset)]]);
                    if (driven < restScale * 1.5f)
                    {
                        continue;
                    }

                    foreach (Vector2 sideUv in new[] { boundary - offset, boundary + offset })
                    {
                        Vector3 rendered = (Vector3)evaluateRenderedNormal.Invoke(
                            null,
                            [settings, shape, sideUv, sites, deformations, sharpenings, springScales, texelOwners, magnet, 1.2f, config])!;
                        Vector3 raw = (Vector3)evaluateRawFiniteNormal.Invoke(
                            null,
                            [settings, shape, sideUv, sites, deformations, sharpenings, springScales, texelOwners, magnet, 1.2f, config])!;
                        if (raw.sqrMagnitude < 0.000000001f)
                        {
                            continue;
                        }

                        // The rendered normal must agree with the displaced surface's own gradient modulo the chart's fixed winding; a displaced-vs-analytic flip inverts it exactly on overhanging spike undersides.
                        float winding = (float)evaluateRenderedWinding.Invoke(
                            null,
                            [settings, shape, sideUv, sites, deformations, sharpenings, springScales, texelOwners, magnet, 1.2f, config])!;
                        float alignment = Vector3.Dot(rendered, raw.normalized) * winding;
                        if (alignment < minimumNormalAlignment)
                        {
                            minimumNormalAlignment = alignment;
                            worstNormalBoundary = sideUv;
                        }
                    }
                }
            }

            Assert.That(
                maximumGap,
                Is.LessThan(0.001f),
                $"{shape} opened a {maximumGap:F4}-unit crack at chart uv {worstBoundary} while the magnet swept the surface; every per-site displacement factor must blend across Voronoi texel borders like the spring scale does.");
            Assert.That(
                minimumNormalAlignment,
                Is.GreaterThan(0.5f),
                $"{shape} inverted the rendered normal (alignment {minimumNormalAlignment:F2}) at chart uv {worstNormalBoundary} on a driven spike's underside; orientation must come from the chart's static winding, not a displaced-vs-analytic flip.");
        }

        int OwnerTexel(Vector2 uv)
        {
            int map = mapSize;
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * map), 0, map - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * map), 0, map - 1);
            return (y * map) + x;
        }
    }

    // Reflection keeps tests independent of internal visibility while still exercising the built plugin selected by the versioned test configuration.
    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: true)!;
    }

    // Typed constant reads ensure tests follow the actual production topology rather than duplicating numbers in a test-only helper.
    private static T GetConstant<T>(Type type, string name)
    {
        return (T)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    }

    // This wrapper avoids repeating reflection argument construction in the centerline and outward-vector parity tests.
    private static Vector2 InvokeVector2(MethodInfo method, params object[] arguments)
    {
        return (Vector2)method.Invoke(null, arguments)!;
    }
}
