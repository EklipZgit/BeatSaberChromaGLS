using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

#pragma warning disable SA1201

namespace ChromaGLS.HarmonyPatches
{
    internal static class GlsColorDistribution
    {
        [Flags]
        internal enum DistributionTargets
        {
            None = 0,
            Hue = 1 << 0,
            Saturation = 1 << 1,
            Value = 1 << 2,
            Red = 1 << 3,
            Green = 1 << 4,
            Blue = 1 << 5,
            Brightness = 1 << 6,
        }

        internal sealed class Instruction
        {
            public Instruction(DistributionTargets targets, float offset, Func<float, float> easing, bool perLight)
            {
                Targets = targets;
                Offset = offset;
                Easing = easing;
                PerLight = perLight;
            }

            public DistributionTargets Targets { get; }

            public float Offset { get; }

            public Func<float, float> Easing { get; }

            public bool PerLight { get; }
        }

        internal readonly struct Progress
        {
            public Progress(float chunk, float light)
            {
                Chunk = chunk;
                Light = light;
            }

            public float Chunk { get; }

            public float Light { get; }
        }

        private const DistributionTargets HsvTargets =
            DistributionTargets.Hue | DistributionTargets.Saturation | DistributionTargets.Value;

        private static readonly IReadOnlyList<Instruction> EmptyInstructions = Array.Empty<Instruction>();

        private static readonly Dictionary<string, Func<float, float>> Easings = new(StringComparer.OrdinalIgnoreCase)
        {
            { "L", Linear },
            { "easeLinear", Linear },
            { "I^2", QuadraticIn },
            { "easeInQuad", QuadraticIn },
            { "O^2", QuadraticOut },
            { "easeOutQuad", QuadraticOut },
            { "IO^2", QuadraticInOut },
            { "easeInOutQuad", QuadraticInOut },
            { "I^3", CubicIn },
            { "easeInCubic", CubicIn },
            { "O^3", CubicOut },
            { "easeOutCubic", CubicOut },
            { "IO^3", CubicInOut },
            { "easeInOutCubic", CubicInOut },
            { "I^4", QuarticIn },
            { "easeInQuart", QuarticIn },
            { "O^4", QuarticOut },
            { "easeOutQuart", QuarticOut },
            { "IO^4", QuarticInOut },
            { "easeInOutQuart", QuarticInOut },
            { "I^5", QuinticIn },
            { "easeInQuint", QuinticIn },
            { "O^5", QuinticOut },
            { "easeOutQuint", QuinticOut },
            { "IO^5", QuinticInOut },
            { "easeInOutQuint", QuinticInOut },
            { "ISn", SinusoidalIn },
            { "easeInSine", SinusoidalIn },
            { "OSn", SinusoidalOut },
            { "easeOutSine", SinusoidalOut },
            { "IOSn", SinusoidalInOut },
            { "easeInOutSine", SinusoidalInOut },
            { "IEx", ExponentialIn },
            { "easeInExpo", ExponentialIn },
            { "OEx", ExponentialOut },
            { "easeOutExpo", ExponentialOut },
            { "IOEx", ExponentialInOut },
            { "easeInOutExpo", ExponentialInOut },
            { "ICr", CircularIn },
            { "easeInCirc", CircularIn },
            { "OCr", CircularOut },
            { "easeOutCirc", CircularOut },
            { "IOCr", CircularInOut },
            { "easeInOutCirc", CircularInOut },
            { "IBk", BackIn },
            { "easeInBack", BackIn },
            { "OBk", BackOut },
            { "easeOutBack", BackOut },
            { "IOTBk", BackInOut },
            { "easeInOutBack", BackInOut },
            { "IEl", ElasticIn },
            { "easeInElastic", ElasticIn },
            { "OEl", ElasticOut },
            { "easeOutElastic", ElasticOut },
            { "IOTEl", ElasticInOut },
            { "easeInOutElastic", ElasticInOut },
            { "IBo", BounceIn },
            { "easeInBounce", BounceIn },
            { "OBo", BounceOut },
            { "easeOutBounce", BounceOut },
            { "IOTBo", BounceInOut },
            { "easeInOutBounce", BounceInOut },
            { "IOBk", BeatSaberBackInOut },
            { "easeBeatSaberInOutBack", BeatSaberBackInOut },
            { "IOEl", BeatSaberElasticInOut },
            { "easeBeatSaberInOutElastic", BeatSaberElasticInOut },
            { "IOBo", BeatSaberBounceInOut },
            { "easeBeatSaberInOutBounce", BeatSaberBounceInOut },
            { "N", Step },
            { "easeStep", Step },
        };

        // Ignore unknown fields so newer distribution formats can still use the parts this version understands.
        internal static IReadOnlyList<Instruction> Parse(object? rawInstructions)
        {
            if (rawInstructions is not IEnumerable values || rawInstructions is string)
            {
                return EmptyInstructions;
            }

            List<Instruction> instructions = new();
            foreach (object? rawValue in values)
            {
                if (rawValue is not string value)
                {
                    continue;
                }

                string[] parts = value.Split(',');
                if (parts.Length < 3
                    || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float offset)
                    || float.IsNaN(offset)
                    || float.IsInfinity(offset)
                    || !Easings.TryGetValue(parts[2].Trim(), out Func<float, float> easing))
                {
                    continue;
                }

                DistributionTargets targets = ParseTargets(parts[0]);
                if (targets == DistributionTargets.None)
                {
                    continue;
                }

                string mode = parts.Length >= 4
                    ? parts[3].Trim()
                    : string.Empty;
                bool perLight = string.Equals(mode, "l", StringComparison.OrdinalIgnoreCase);
                instructions.Add(new Instruction(targets, offset, easing, perLight));
            }

            return instructions.Count == 0 ? EmptyInstructions : instructions;
        }

        internal static Color Apply(
            Color color,
            IReadOnlyList<Instruction>? boxInstructions,
            IReadOnlyList<Instruction>? eventInstructions,
            Progress progress)
        {
            Evaluator evaluator = new(color);
            evaluator.Apply(boxInstructions, progress);
            evaluator.Apply(eventInstructions, progress);
            return evaluator.Result;
        }

        // Count only selected lights when assigning progress within each native filter chunk.
        internal static Dictionary<int, Progress> BuildProgress(int[] elementIds, int[] distributionOrders)
        {
            Dictionary<int, Progress> result = new(elementIds.Length);
            if (elementIds.Length == 0 || elementIds.Length != distributionOrders.Length)
            {
                return result;
            }

            int[] denseChunkOrders = new int[elementIds.Length];
            int chunkOrder = -1;
            int previousDistributionOrder = 0;
            for (int index = 0; index < elementIds.Length; index++)
            {
                if (index == 0 || distributionOrders[index] != previousDistributionOrder)
                {
                    chunkOrder++;
                    previousDistributionOrder = distributionOrders[index];
                }

                denseChunkOrders[index] = chunkOrder;
            }

            int chunkCount = chunkOrder + 1;
            for (int index = 0; index < elementIds.Length; index++)
            {
                float chunkProgress = denseChunkOrders[index] / (float)Mathf.Max(chunkCount - 1, 1);
                float lightProgress = index / (float)Mathf.Max(elementIds.Length - 1, 1);
                result[elementIds[index]] = new Progress(chunkProgress, lightProgress);
            }

            return result;
        }

        private static Color EvaluateForTesting(
            Color color,
            object boxInstructions,
            object eventInstructions,
            float chunkProgress,
            float lightProgress)
        {
            return Apply(
                color,
                Parse(boxInstructions),
                Parse(eventInstructions),
                new Progress(chunkProgress, lightProgress));
        }

        private static Vector2[] BuildProgressForTesting(int[] elementIds, int[] distributionOrders)
        {
            Dictionary<int, Progress> progressByElement = BuildProgress(elementIds, distributionOrders);
            Vector2[] result = new Vector2[elementIds.Length];
            for (int index = 0; index < elementIds.Length; index++)
            {
                Progress progress = progressByElement[elementIds[index]];
                result[index] = new Vector2(progress.Chunk, progress.Light);
            }

            return result;
        }

        // Keep HSV offsets unclamped until all instructions for the channel have run.
        // Clamping after each instruction would lose saturation and hue at intermediate gray values.
        private ref struct Evaluator
        {
            private Color _color;
            private PendingHsv _hsv;

            public Evaluator(Color color)
            {
                _color = color;
                _hsv = default;
            }

            public Color Result
            {
                get
                {
                    MaterializeHsv();
                    return _color;
                }
            }

            public void Apply(IReadOnlyList<Instruction>? instructions, Progress progress)
            {
                if (instructions == null)
                {
                    return;
                }

                for (int index = 0; index < instructions.Count; index++)
                {
                    Instruction instruction = instructions[index];
                    float distributionProgress = instruction.PerLight ? progress.Light : progress.Chunk;
                    float offset = instruction.Offset * instruction.Easing(distributionProgress);
                    DistributionTargets targets = instruction.Targets;
                    if ((targets & HsvTargets) != 0)
                    {
                        EnsureHsv();
                        if ((targets & DistributionTargets.Hue) != 0)
                        {
                            _hsv.Hue += offset;
                        }

                        if ((targets & DistributionTargets.Saturation) != 0)
                        {
                            _hsv.Saturation += offset;
                        }

                        if ((targets & DistributionTargets.Value) != 0)
                        {
                            _hsv.Value += offset;
                        }
                    }
                    else if ((targets & (DistributionTargets.Red | DistributionTargets.Green | DistributionTargets.Blue)) != 0)
                    {
                        // Convert pending HSV changes to RGB before applying an RGB offset.
                        MaterializeHsv();
                        if ((targets & DistributionTargets.Red) != 0)
                        {
                            _color.r += offset;
                        }

                        if ((targets & DistributionTargets.Green) != 0)
                        {
                            _color.g += offset;
                        }

                        if ((targets & DistributionTargets.Blue) != 0)
                        {
                            _color.b += offset;
                        }
                    }

                    if ((targets & DistributionTargets.Brightness) != 0)
                    {
                        _color.a += offset;
                    }
                }
            }

            private void EnsureHsv()
            {
                if (_hsv.Pending)
                {
                    return;
                }

                Color.RGBToHSV(_color, out _hsv.Hue, out _hsv.Saturation, out _hsv.Value);
                _hsv.Pending = true;
            }

            private void MaterializeHsv()
            {
                if (!_hsv.Pending)
                {
                    return;
                }

                float alpha = _color.a;
                _color = Color.HSVToRGB(Mathf.Repeat(_hsv.Hue, 1f), Mathf.Clamp01(_hsv.Saturation), _hsv.Value, true);
                _color.a = alpha;
                _hsv.Pending = false;
            }
        }

        private struct PendingHsv
        {
            public bool Pending;
            public float Hue;
            public float Saturation;
            public float Value;
        }

        private static DistributionTargets ParseTargets(string value)
        {
            DistributionTargets targets = DistributionTargets.None;
            ColorModel model = ColorModel.None;
            foreach (char target in value ?? string.Empty)
            {
                switch (char.ToLowerInvariant(target))
                {
                    case 'h' when model != ColorModel.Rgb:
                        model = ColorModel.Hsv;
                        targets |= DistributionTargets.Hue;
                        break;
                    case 's' when model != ColorModel.Rgb:
                        model = ColorModel.Hsv;
                        targets |= DistributionTargets.Saturation;
                        break;
                    case 'v' when model != ColorModel.Rgb:
                        model = ColorModel.Hsv;
                        targets |= DistributionTargets.Value;
                        break;
                    case 'r' when model != ColorModel.Hsv:
                        model = ColorModel.Rgb;
                        targets |= DistributionTargets.Red;
                        break;
                    case 'g' when model != ColorModel.Hsv:
                        model = ColorModel.Rgb;
                        targets |= DistributionTargets.Green;
                        break;
                    case 'b' when model != ColorModel.Hsv:
                        model = ColorModel.Rgb;
                        targets |= DistributionTargets.Blue;
                        break;
                    case 'f':
                        targets |= DistributionTargets.Brightness;
                        break;
                }
            }

            return targets;
        }

        private static float Linear(float value) => value;

        private static float Step(float value) => Mathf.Floor(value);

        private static float QuadraticIn(float value) => value * value;

        private static float QuadraticOut(float value) => value * (2f - value);

        private static float QuadraticInOut(float value) => (value *= 2f) < 1f
            ? 0.5f * value * value
            : -0.5f * (((value -= 1f) * (value - 2f)) - 1f);

        private static float CubicIn(float value) => value * value * value;

        private static float CubicOut(float value) => 1f + ((value -= 1f) * value * value);

        private static float CubicInOut(float value) => (value *= 2f) < 1f
            ? 0.5f * value * value * value
            : 0.5f * (((value -= 2f) * value * value) + 2f);

        private static float QuarticIn(float value) => value * value * value * value;

        private static float QuarticOut(float value) => 1f - ((value -= 1f) * value * value * value);

        private static float QuarticInOut(float value) => (value *= 2f) < 1f
            ? 0.5f * value * value * value * value
            : -0.5f * (((value -= 2f) * value * value * value) - 2f);

        private static float QuinticIn(float value) => value * value * value * value * value;

        private static float QuinticOut(float value) => 1f + ((value -= 1f) * value * value * value * value);

        private static float QuinticInOut(float value) => (value *= 2f) < 1f
            ? 0.5f * value * value * value * value * value
            : 0.5f * (((value -= 2f) * value * value * value * value) + 2f);

        private static float SinusoidalIn(float value) => 1f - Mathf.Cos(value * Mathf.PI / 2f);

        private static float SinusoidalOut(float value) => Mathf.Sin(value * Mathf.PI / 2f);

        private static float SinusoidalInOut(float value) => 0.5f * (1f - Mathf.Cos(Mathf.PI * value));

        private static float ExponentialIn(float value) => value == 0f ? 0f : Mathf.Pow(1024f, value - 1f);

        private static float ExponentialOut(float value) => value == 1f ? 1f : 1f - Mathf.Pow(2f, -10f * value);

        private static float ExponentialInOut(float value)
        {
            if (value == 0f || value == 1f)
            {
                return value;
            }

            return (value *= 2f) < 1f
                ? 0.5f * Mathf.Pow(1024f, value - 1f)
                : 0.5f * (-Mathf.Pow(2f, -10f * (value - 1f)) + 2f);
        }

        private static float CircularIn(float value) => 1f - Mathf.Sqrt(1f - (value * value));

        private static float CircularOut(float value) => Mathf.Sqrt(1f - ((value -= 1f) * value));

        private static float CircularInOut(float value) => (value *= 2f) < 1f
            ? -0.5f * (Mathf.Sqrt(1f - (value * value)) - 1f)
            : 0.5f * (Mathf.Sqrt(1f - ((value -= 2f) * value)) + 1f);

        private static float BackIn(float value) => value * value * ((2.70158f * value) - 1.70158f);

        private static float BackOut(float value) => ((value -= 1f) * value * ((2.70158f * value) + 1.70158f)) + 1f;

        private static float BackInOut(float value) => (value *= 2f) < 1f
            ? 0.5f * value * value * ((3.5949095f * value) - 2.5949095f)
            : 0.5f * (((value -= 2f) * value * ((3.5949095f * value) + 2.5949095f)) + 2f);

        // Beat Saber's InOut Back curve is asymmetric.
        private static float BeatSaberBackInOut(float value)
        {
            if (value < 0.517f)
            {
                return 5.014f * value * value * value;
            }

            value = (1.665f * (value - 0.4f)) - 1f;
            return 1f + (2.70158f * Mathf.Pow(value, 3f)) + (1.70158f * Mathf.Pow(value, 2f));
        }

        private static float ElasticIn(float value)
        {
            if (value == 0f || value == 1f)
            {
                return value;
            }

            value -= 1f;
            return -Mathf.Pow(2f, 10f * value) * Mathf.Sin((value - 0.1f) * (2f * Mathf.PI) / 0.4f);
        }

        private static float ElasticOut(float value)
        {
            if (value == 0f || value == 1f)
            {
                return value;
            }

            return (Mathf.Pow(2f, -10f * value) * Mathf.Sin((value - 0.1f) * (2f * Mathf.PI) / 0.4f)) + 1f;
        }

        private static float ElasticInOut(float value)
        {
            value *= 2f;
            if (value < 1f)
            {
                value -= 1f;
                return -0.5f * Mathf.Pow(2f, 10f * value) * Mathf.Sin((value - 0.1f) * (2f * Mathf.PI) / 0.4f);
            }

            value -= 1f;
            return (Mathf.Pow(2f, -10f * value) * Mathf.Sin((value - 0.1f) * (2f * Mathf.PI) / 0.4f) * 0.5f) + 1f;
        }

        // Beat Saber's elastic curve starts with a cubic segment.
        private static float BeatSaberElasticInOut(float value) => value < 0.3f
            ? 37.037f * value * value * value
            : (Mathf.Pow(2f, -10f * (value - 0.2f)) * Mathf.Sin(value * 10f * (Mathf.PI * 2f / 3f))) + 1f;

        private static float BounceIn(float value) => 1f - BounceOut(1f - value);

        private static float BounceOut(float value)
        {
            if (value < 1f / 2.75f)
            {
                return 7.5625f * value * value;
            }

            if (value < 2f / 2.75f)
            {
                value -= 1.5f / 2.75f;
                return (7.5625f * value * value) + 0.75f;
            }

            if (value < 2.5f / 2.75f)
            {
                value -= 2.25f / 2.75f;
                return (7.5625f * value * value) + 0.9375f;
            }

            value -= 2.625f / 2.75f;
            return (7.5625f * value * value) + 0.984375f;
        }

        private static float BounceInOut(float value) => value < 0.5f
            ? BounceIn(value * 2f) * 0.5f
            : (BounceOut((value * 2f) - 1f) * 0.5f) + 0.5f;

        // Beat Saber's bounce curve starts with a cubic segment.
        private static float BeatSaberBounceInOut(float value)
        {
            if (value < 0.72727275f)
            {
                if (value < 0.36363637f)
                {
                    return 20.796f * value * value * value;
                }

                value -= 0.54545456f;
                return (7.5625f * value * value) + 0.75f;
            }

            if (value < 0.90909094f)
            {
                value -= 0.8181818f;
                return (7.5625f * value * value) + 0.9375f;
            }

            value -= 21f / 22f;
            return (7.5625f * value * value) + (63f / 64f);
        }

        private enum ColorModel
        {
            None,
            Hsv,
            Rgb,
        }
    }
}
