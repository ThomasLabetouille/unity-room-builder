// Doublures minimales des types UnityEngine utilises par PolygonTriangulator.
//
// Elles n'existent que pour compiler et executer la geometrie HORS d'Unity. Si un jour
// PolygonTriangulator.cs se met a utiliser un type Unity absent d'ici, la compilation de ce
// projet casse : c'est voulu. Ce fichier est le garde-fou qui maintient la triangulation
// independante de l'editeur, seule condition pour la tester par milliers de cas en quelques
// secondes au lieu d'un aller-retour dans Unity.
//
// ATTENTION : ce dossier doit rester HORS de Assets/, sinon Unity compilerait ces types en
// double avec les vrais et le projet ne compilerait plus.

using System;

namespace UnityEngine
{
    public struct Vector2 : IEquatable<Vector2>
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero { get { return new Vector2(0f, 0f); } }
        public static Vector2 one { get { return new Vector2(1f, 1f); } }

        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }

        public static float Distance(Vector2 a, Vector2 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator *(Vector2 a, float f) { return new Vector2(a.x * f, a.y * f); }
        public static Vector2 operator *(float f, Vector2 a) { return new Vector2(a.x * f, a.y * f); }

        public bool Equals(Vector2 other) { return x == other.x && y == other.y; }
        public override bool Equals(object o) { return o is Vector2 && Equals((Vector2)o); }
        public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2); }
        public override string ToString() { return string.Format("({0:R}, {1:R})", x, y); }
    }

    /// <summary>
    /// Vector3 reduit au strict necessaire pour PanelMeshBuilder. L'indexeur est indispensable :
    /// c'est par lui que le code passe d'un repere (U,V,W) a des coordonnees XYZ.
    /// </summary>
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float this[int i]
        {
            get
            {
                if (i == 0) return x;
                if (i == 1) return y;
                if (i == 2) return z;
                throw new IndexOutOfRangeException("Vector3 : index " + i);
            }
            set
            {
                if (i == 0) x = value;
                else if (i == 1) y = value;
                else if (i == 2) z = value;
                else throw new IndexOutOfRangeException("Vector3 : index " + i);
            }
        }

        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }
        public static Vector3 one { get { return new Vector3(1f, 1f, 1f); } }

        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float f) { return new Vector3(a.x * f, a.y * f, a.z * f); }

        public static float Distance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public bool Equals(Vector3 o) { return x == o.x && y == o.y && z == o.z; }
        public override bool Equals(object o) { return o is Vector3 && Equals((Vector3)o); }
        public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2); }
        public override string ToString() { return string.Format("({0:R}, {1:R}, {2:R})", x, y, z); }
    }

    public static class Mathf
    {
        public const float Epsilon = 1.401298E-45f;

        public static float Abs(float v) { return Math.Abs(v); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float InverseLerp(float a, float b, float v)
        {
            if (a == b) return 0f;
            float t = (v - a) / (b - a);
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }
    }
}
