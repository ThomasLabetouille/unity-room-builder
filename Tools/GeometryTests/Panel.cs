using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace LevelDesignTools.Tests
{
    /// <summary>
    /// Un cas de test : le contour exterieur d'un panneau et les trous qu'on y perce.
    /// "Profile" et "Seed" ne servent qu'a rendre un echec reproductible.
    /// </summary>
    public sealed class Panel
    {
        public List<Vector2> Outer = new List<Vector2>();
        public List<List<Vector2>> Holes = new List<List<Vector2>>();
        public string Profile = "?";
        public int Seed;

        public string Name
        {
            get { return string.Format("{0} ({1} trou(s), seed {2})", Profile, Holes.Count, Seed); }
        }

        public Panel Transformed(Vector2 offset, float scale)
        {
            var copy = new Panel { Profile = Profile, Seed = Seed };
            foreach (var p in Outer) copy.Outer.Add(new Vector2(p.x * scale + offset.x, p.y * scale + offset.y));
            foreach (var hole in Holes)
            {
                var moved = new List<Vector2>(hole.Count);
                foreach (var p in hole) moved.Add(new Vector2(p.x * scale + offset.x, p.y * scale + offset.y));
                copy.Holes.Add(moved);
            }
            return copy;
        }

        /// <summary>
        /// Emet le cas sous forme de code C# collable tel quel dans Regressions.cs. Un fuzzer qui
        /// trouve un bug qu'on ne sait pas rejouer ne sert a rien : c'est cette methode qui
        /// transforme un echec aleatoire en cas de test permanent.
        /// </summary>
        public string ToCSharp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("        // " + Name);
            sb.AppendLine("        yield return new Panel");
            sb.AppendLine("        {");
            sb.AppendLine("            Profile = \"regression: " + Profile + "\",");
            sb.AppendLine("            Outer = " + Loop(Outer) + ",");
            sb.AppendLine("            Holes = new List<List<Vector2>>");
            sb.AppendLine("            {");
            foreach (var hole in Holes) sb.AppendLine("                " + Loop(hole) + ",");
            sb.AppendLine("            },");
            sb.AppendLine("        };");
            return sb.ToString();
        }

        private static string Loop(List<Vector2> points)
        {
            var sb = new StringBuilder("new List<Vector2> { ");
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("new Vector2(")
                  .Append(points[i].x.ToString("R", CultureInfo.InvariantCulture)).Append("f, ")
                  .Append(points[i].y.ToString("R", CultureInfo.InvariantCulture)).Append("f)");
            }
            sb.Append(" }");
            return sb.ToString();
        }

        public static double SignedArea(IList<Vector2> loop)
        {
            double a = 0.0;
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = loop[i];
                Vector2 q = loop[(i + 1) % n];
                a += (double)p.x * q.y - (double)q.x * p.y;
            }
            return a * 0.5;
        }

        public double ExpectedArea()
        {
            double area = Math.Abs(SignedArea(Outer));
            foreach (var hole in Holes) area -= Math.Abs(SignedArea(hole));
            return area;
        }
    }
}
