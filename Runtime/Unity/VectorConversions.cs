namespace Mosaic.Ui
{
    public static class VectorConversions
    {
        public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector2 value, float z = 0) =>
            new UnityEngine.Vector3(value.X, value.Y, z);

        public static System.Numerics.Vector2 ToNumerics(this UnityEngine.Vector2 value) =>
            new System.Numerics.Vector2(value.x, value.y);
    }
}
