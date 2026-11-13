using Newtonsoft.Json;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal enum MenuLightType
    {
        Parabola,
        Square,
        Cube,
        Sphere,
        Circle,
        Floor,
    }

    internal sealed class MenuLightDefinition
    {
        internal const float FloorHeight = 0.02f;

        [JsonProperty]
        public string Id { get; set; } = string.Empty;

        [JsonProperty]
        public MenuLightType Type { get; set; }

        [JsonProperty]
        public bool Enabled { get; set; } = true;

        [JsonProperty]
        public ParabolicFloorLightSettings Appearance { get; set; }

        [JsonProperty]
        public float PositionX { get; set; }

        [JsonProperty]
        public float PositionZ { get; set; }

        [JsonProperty]
        public float Height { get; set; } = ParabolicFloorLightGeometry.TopHeight;

        [JsonProperty]
        public float Depth { get; set; } = 0.02f;

        [JsonProperty]
        public float Size { get; set; } = 1f;

        [JsonProperty]
        public float EndLength { get; set; }

        [JsonProperty]
        public float FadeStart { get; set; } = 0.45f;

        [JsonProperty]
        public float QuaternionX { get; set; }

        [JsonProperty]
        public float QuaternionY { get; set; }

        [JsonProperty]
        public float QuaternionZ { get; set; }

        [JsonProperty]
        public float QuaternionW { get; set; } = 1f;

        // Persisted slider positions only; never recomputed from the quaternion.
        [JsonProperty]
        public float RotationX { get; set; }

        [JsonProperty]
        public float RotationY { get; set; }

        [JsonProperty]
        public float RotationZ { get; set; }

        internal float GetHeight()
            => Type == MenuLightType.Floor ? Mathf.Clamp(Height, 0.001f, 2f) : Mathf.Clamp(Height, 0f, 7f);

        internal void ConstrainPose()
        {
            Height = GetHeight();
            if (Type != MenuLightType.Floor)
                return;

            Depth = Height;
            RotationX = 0f;
            RotationZ = 0f;
            float yaw = RotationY * Mathf.Deg2Rad;
            QuaternionX = 0f;
            QuaternionZ = 0f;
            QuaternionY = Mathf.Sin(yaw * 0.5f);
            QuaternionW = Mathf.Cos(yaw * 0.5f);
        }

        internal Quaternion GetRotation()
        {
            if (Type == MenuLightType.Floor)
                return Quaternion.Euler(0f, RotationY, 0f);

            Quaternion rotation = new(QuaternionX, QuaternionY, QuaternionZ, QuaternionW);
            return rotation.normalized;
        }

        internal void RotateAxis(int axis, float newSliderValue)
        {
            if (Type == MenuLightType.Floor && axis != 1)
                return;

            float previous = axis switch { 0 => RotationX, 1 => RotationY, _ => RotationZ };
            float delta = newSliderValue - previous;
            Quaternion rotation = GetRotation();
            Vector3 localAxis = axis switch { 0 => Vector3.right, 1 => Vector3.up, _ => Vector3.forward };
            Quaternion step = rotation * Quaternion.AngleAxis(delta, localAxis);
            QuaternionX = step.x;
            QuaternionY = step.y;
            QuaternionZ = step.z;
            QuaternionW = step.w;
            switch (axis)
            {
                case 0: RotationX = newSliderValue; break;
                case 1: RotationY = newSliderValue; break;
                default: RotationZ = newSliderValue; break;
            }
        }

        internal MenuLightDefinition Clone()
            => (MenuLightDefinition)MemberwiseClone();
    }
}
