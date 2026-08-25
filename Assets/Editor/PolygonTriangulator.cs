using System.Collections.Generic;
using UnityEngine;

namespace LevelDesignTools
{
    /// <summary>
    /// Triangulation par ear-clipping d'un polygone simple (non auto-intersectant),
    /// exprimé dans un plan 2D (Vector2). Utilisé par le Room Builder pour générer
    /// le sol/plafond d'une salle dessinée à main levée (contour libre).
    /// </summary>
    public static class PolygonTriangulator
    {
        // L'ear-clipping qui vivait ici (Triangulate, SignedArea, IsConvex, PointInTriangle) a ete
        // supprime : plus rien ne l'appelait depuis que les faces passent par la decomposition en
        // tranches et que les sols a main levee sont devenus des panneaux comme les autres. Ce
        // n'est pas la relecture qui l'a vu, c'est l'injection de defauts : on peut casser du code
        // mort autant qu'on veut, aucun test ne bronche, et le mutant survit sans rien dire de la
        // qualite des tests. Un survivant inexplicable vaut la peine qu'on aille voir.

        private const float Epsilon = 1e-5f;

        /// <summary>
        /// Triangule un contour exterieur perce d'un nombre quelconque de trous simples et
        /// disjoints (portes, fenetres... percees dans un meme mur).
        ///
        /// Methode : decomposition en tranches horizontales. Les frontieres des tranches sont
        /// toutes les ordonnees de sommets ; a l'interieur d'une tranche aucun sommet ne se
        /// trouve, donc chaque region interieure y est un simple trapeze, obtenu en triant les
        /// aretes qui traversent la tranche et en les appariant deux a deux (regle pair/impair).
        ///
        /// C'est volontairement plus terre-a-terre qu'un ear-clipping avec ponts entre les trous :
        /// le pontage produit un polygone "faiblement simple" (couloirs d'epaisseur nulle) sur
        /// lequel l'ear-clipping echoue des que plusieurs trous sont proches ou alignes, alors que
        /// la decomposition en tranches ne peut pas echouer et reste exacte. Elle genere un peu
        /// plus de triangles, ce qui n'a aucune importance pour un mur de blockout.
        ///
        /// Renvoie les sommets des triangles a plat : 3 sommets consecutifs = 1 triangle
        /// (liste vide si la surface est degeneree).
        /// </summary>
        public static List<Vector2> TriangulateFace(IList<Vector2> outer, IList<List<Vector2>> holes)
        {
            var result = new List<Vector2>();
            var slabLines = new List<float>();
            if (outer == null || outer.Count < 3) return result;

            var edges = new List<Edge>();
            var ys = new List<float>();

            AppendLoop(outer, edges, ys);
            if (holes != null)
            {
                foreach (var hole in holes)
                {
                    if (hole != null && hole.Count >= 3) AppendLoop(hole, edges, ys);
                }
            }
            if (edges.Count < 2) return result;

            // Seuil d'aire en dessous duquel un triangle est considere comme plat. Il est
            // PROPORTIONNEL a la taille du panneau : un seuil absolu ferait dependre le maillage
            // genere de l'unite et de la position du mur — un meme mur decoupe a 40 m de
            // l'origine, ou modelise a une autre echelle, ne rendrait pas les memes triangles.
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in outer)
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            float areaEpsilon = Mathf.Max((maxX - minX) * (maxY - minY), 1e-12f) * 1e-9f;

            ys.Sort();
            foreach (float y in ys)
            {
                if (slabLines.Count == 0 || y - slabLines[slabLines.Count - 1] > Epsilon) slabLines.Add(y);
            }
            if (slabLines.Count < 2) return result;

            // Points de coupure sur chaque ligne : en subdivisant les bords des trapezes sur ces
            // memes points, deux tranches voisines partagent exactement les memes sommets le long
            // de leur frontiere commune — aucune T-jonction, donc aucune fissure a l'affichage.
            var splits = new List<float>[slabLines.Count];
            for (int k = 0; k < slabLines.Count; k++) splits[k] = BuildSplitLine(edges, slabLines[k]);

            var crossings = new List<Crossing>();
            var bottom = new List<float>();
            var top = new List<float>();

            for (int k = 0; k < slabLines.Count - 1; k++)
            {
                float y0 = slabLines[k];
                float y1 = slabLines[k + 1];
                if (y1 - y0 <= Epsilon) continue;
                float yMid = (y0 + y1) * 0.5f;

                crossings.Clear();
                foreach (var edge in edges)
                {
                    if (edge.MinY <= y0 + Epsilon && edge.MaxY >= y1 - Epsilon)
                    {
                        crossings.Add(new Crossing(edge.XAt(yMid), edge));
                    }
                }
                if (crossings.Count < 2) continue;
                crossings.Sort((a, b) => a.X.CompareTo(b.X));

                // Regle pair/impair : entre la 1re et la 2e arete on est dedans, entre la 2e et
                // la 3e on est dehors (dans un trou), etc.
                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    Edge left = crossings[i].Edge;
                    Edge right = crossings[i + 1].Edge;

                    float xL0 = left.XAt(y0), xR0 = right.XAt(y0);
                    float xL1 = left.XAt(y1), xR1 = right.XAt(y1);
                    if (xR0 < xL0) { float s = xL0; xL0 = xR0; xR0 = s; }
                    if (xR1 < xL1) { float s = xL1; xL1 = xR1; xR1 = s; }
                    if (xR0 - xL0 <= Epsilon && xR1 - xL1 <= Epsilon) continue;

                    BuildChain(bottom, splits[k], xL0, xR0);
                    BuildChain(top, splits[k + 1], xL1, xR1);
                    EmitStrip(result, bottom, y0, top, y1, areaEpsilon);
                }
            }

            return result;
        }

        private readonly struct Edge
        {
            public readonly Vector2 A;
            public readonly Vector2 B;
            public readonly float MinY;
            public readonly float MaxY;

            public Edge(Vector2 a, Vector2 b)
            {
                A = a;
                B = b;
                MinY = Mathf.Min(a.y, b.y);
                MaxY = Mathf.Max(a.y, b.y);
            }

            /// <summary>
            /// Abscisse de l'arete a l'ordonnee y. y est RAMENE dans l'etendue de l'arete : les
            /// appelants l'evaluent parfois jusqu'a Epsilon en dehors (tolerance sur les bornes de
            /// tranche), et sur une arete presque horizontale cette extrapolation minuscule en y
            /// se traduit par un ecart enorme en x — assez pour poser un sommet a l'interieur d'un
            /// trou voisin. Le bornage supprime le probleme sans rien changer au cas normal, ou y
            /// est deja dans l'etendue.
            /// </summary>
            public float XAt(float y)
            {
                float dy = B.y - A.y;
                if (Mathf.Abs(dy) < 1e-9f) return A.x;
                if (y <= MinY) return A.y < B.y ? A.x : B.x;
                if (y >= MaxY) return A.y < B.y ? B.x : A.x;
                return A.x + (y - A.y) / dy * (B.x - A.x);
            }
        }

        private readonly struct Crossing
        {
            public readonly float X;
            public readonly Edge Edge;

            public Crossing(float x, Edge edge)
            {
                X = x;
                Edge = edge;
            }
        }

        private static void AppendLoop(IList<Vector2> loop, List<Edge> edges, List<float> ys)
        {
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = loop[i];
                Vector2 b = loop[(i + 1) % n];
                ys.Add(a.y);
                // Les aretes horizontales ne traversent aucune tranche : elles n'apportent rien
                // au balayage (leurs sommets, eux, sont bien pris en compte comme frontieres).
                if (Mathf.Abs(a.y - b.y) > Epsilon) edges.Add(new Edge(a, b));
            }
        }

        private static List<float> BuildSplitLine(List<Edge> edges, float y)
        {
            var xs = new List<float>();
            foreach (var edge in edges)
            {
                if (edge.MinY - Epsilon <= y && y <= edge.MaxY + Epsilon) xs.Add(edge.XAt(y));
            }
            xs.Sort();

            var unique = new List<float>(xs.Count);
            foreach (float x in xs)
            {
                if (unique.Count == 0 || x - unique[unique.Count - 1] > Epsilon) unique.Add(x);
            }
            return unique;
        }

        private static void BuildChain(List<float> chain, List<float> splits, float xMin, float xMax)
        {
            chain.Clear();
            chain.Add(xMin);
            for (int i = 0; i < splits.Count; i++)
            {
                float x = splits[i];
                if (x > xMin + Epsilon && x < xMax - Epsilon) chain.Add(x);
            }
            chain.Add(xMax);
        }

        /// <summary>
        /// Triangule la bande comprise entre deux chaines horizontales (bas et haut d'un trapeze,
        /// eventuellement subdivisees). Le trapeze etant convexe, un simple balayage de gauche a
        /// droite suffit.
        /// </summary>
        private static void EmitStrip(List<Vector2> result, List<float> bottom, float y0, List<float> top, float y1,
            float areaEpsilon)
        {
            int bi = 0;
            int ti = 0;
            int guard = 0;
            int maxSteps = bottom.Count + top.Count + 4;

            while ((bi < bottom.Count - 1 || ti < top.Count - 1) && guard++ < maxSteps)
            {
                Vector2 a, b, c;
                bool advanceBottom = ti >= top.Count - 1 ||
                                     (bi < bottom.Count - 1 && bottom[bi + 1] <= top[ti + 1]);
                if (advanceBottom)
                {
                    a = new Vector2(bottom[bi], y0);
                    b = new Vector2(bottom[bi + 1], y0);
                    c = new Vector2(top[ti], y1);
                    bi++;
                }
                else
                {
                    a = new Vector2(bottom[bi], y0);
                    b = new Vector2(top[ti + 1], y1);
                    c = new Vector2(top[ti], y1);
                    ti++;
                }

                float area2 = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area2) <= areaEpsilon) continue; // triangle plat (pointe du trapeze)
                result.Add(a);
                result.Add(b);
                result.Add(c);
            }
        }

        // ===================== TESTS GEOMETRIQUES (validation des contours) =====================

        /// <summary>
        /// Vrai si le contour ferme est simple : au moins 3 sommets, aucun sommet duplique et
        /// aucune paire d'aretes non adjacentes qui se croisent. Un contour auto-intersectant
        /// produit une surface aberrante : mieux vaut le refuser en amont et expliquer pourquoi
        /// a l'utilisateur.
        /// </summary>
        public static bool IsSimplePolygon(IList<Vector2> points)
        {
            int n = points == null ? 0 : points.Count;
            if (n < 3) return false;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (ApproximatelyEqual(points[i], points[j])) return false;
                }
            }

            for (int i = 0; i < n; i++)
            {
                Vector2 a0 = points[i];
                Vector2 a1 = points[(i + 1) % n];

                for (int j = i + 1; j < n; j++)
                {
                    // Aretes adjacentes : elles partagent un sommet, ce n'est pas un croisement.
                    if ((j + 1) % n == i || (i + 1) % n == j) continue;

                    if (SegmentsIntersect(a0, a1, points[j], points[(j + 1) % n])) return false;
                }
            }

            return true;
        }

        /// <summary>Vrai si le point est a l'interieur du contour ferme (lancer de rayon).</summary>
        public static bool PointInPolygon(Vector2 point, IList<Vector2> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 pi = polygon[i];
                Vector2 pj = polygon[j];

                if ((pi.y > point.y) != (pj.y > point.y))
                {
                    float x = pi.x + (point.y - pi.y) / (pj.y - pi.y) * (pj.x - pi.x);
                    if (x > point.x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Vrai si deux contours fermes se chevauchent : aretes qui se croisent, ou l'un
        /// entierement contenu dans l'autre. Sert a refuser un trou qui empieterait sur un trou
        /// deja perce (la decomposition en tranches suppose des trous disjoints).
        /// </summary>
        public static bool PolygonsOverlap(IList<Vector2> a, IList<Vector2> b)
        {
            int na = a.Count;
            int nb = b.Count;
            if (na < 3 || nb < 3) return false;

            for (int i = 0; i < na; i++)
            {
                Vector2 a0 = a[i];
                Vector2 a1 = a[(i + 1) % na];
                for (int j = 0; j < nb; j++)
                {
                    if (SegmentsIntersect(a0, a1, b[j], b[(j + 1) % nb])) return true;
                }
            }

            return PointInPolygon(a[0], b) || PointInPolygon(b[0], a);
        }

        /// <summary>
        /// Vrai si "inner" est entierement contenu dans "outer", sans le toucher ni s'en approcher
        /// a moins de "margin". Sert a valider un contour de trou : affleurer le bord du panneau
        /// ferait fusionner le trou avec le contour exterieur, et le chant genere autour du trou
        /// ferait double emploi avec celui du panneau.
        /// </summary>
        public static bool ContainsWithMargin(IList<Vector2> outer, IList<Vector2> inner, float margin)
        {
            int no = outer.Count;
            int ni = inner.Count;
            if (no < 3 || ni < 3) return false;

            foreach (var p in inner)
            {
                if (!PointInPolygon(p, outer)) return false;
                if (DistanceToLoop(p, outer) <= margin) return false;
            }

            for (int i = 0; i < ni; i++)
            {
                Vector2 a0 = inner[i];
                Vector2 a1 = inner[(i + 1) % ni];
                for (int j = 0; j < no; j++)
                {
                    if (SegmentsIntersect(a0, a1, outer[j], outer[(j + 1) % no])) return false;
                }
            }

            return true;
        }

        /// <summary>Distance du point au contour ferme (a son bord, pas a son interieur).</summary>
        public static float DistanceToLoop(Vector2 p, IList<Vector2> loop)
        {
            float best = float.MaxValue;
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                float d = DistanceToSegment(p, loop[i], loop[(i + 1) % n]);
                if (d < best) best = d;
            }
            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            float length2 = dx * dx + dy * dy;
            if (length2 < 1e-18f) return Vector2.Distance(p, a);

            float t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / length2;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return Vector2.Distance(p, new Vector2(a.x + t * dx, a.y + t * dy));
        }

        private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = Cross(p1, p3, p4);
            float d2 = Cross(p2, p3, p4);
            float d3 = Cross(p3, p1, p2);
            float d4 = Cross(p4, p1, p2);

            if (((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon)) &&
                ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon)))
            {
                return true;
            }

            if (Mathf.Abs(d1) <= Epsilon && OnSegment(p3, p4, p1)) return true;
            if (Mathf.Abs(d2) <= Epsilon && OnSegment(p3, p4, p2)) return true;
            if (Mathf.Abs(d3) <= Epsilon && OnSegment(p1, p2, p3)) return true;
            if (Mathf.Abs(d4) <= Epsilon && OnSegment(p1, p2, p4)) return true;

            return false;
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.x >= Mathf.Min(a.x, b.x) - Epsilon && p.x <= Mathf.Max(a.x, b.x) + Epsilon &&
                   p.y >= Mathf.Min(a.y, b.y) - Epsilon && p.y <= Mathf.Max(a.y, b.y) + Epsilon;
        }

        private static bool ApproximatelyEqual(Vector2 p, Vector2 q)
        {
            return Mathf.Abs(p.x - q.x) <= Epsilon && Mathf.Abs(p.y - q.y) <= Epsilon;
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b)
        {
            return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }
    }
}
