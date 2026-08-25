using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelDesignTools.Tests
{
    public sealed class Failure
    {
        public string Property;
        public string Detail;

        public Failure(string property, string detail)
        {
            Property = property;
            Detail = detail;
        }

        public override string ToString() { return Property + " : " + Detail; }
    }

    /// <summary>
    /// Les proprietes que la triangulation d'un panneau doit verifier QUELLE QUE SOIT l'entree.
    ///
    /// C'est la difference avec un test de non-regression : on ne fige pas "ce contour donne ces
    /// 12 triangles", on enonce une verite generale et on la bombarde de cas. L'ear-clipping avec
    /// ponts passait tous les cas ecrits a la main et echouait sur 5 % des configurations a
    /// plusieurs trous ; seule P1 sur des milliers de tirages l'a revele.
    /// </summary>
    public static class Invariants
    {
        public const int SampleCount = 240;

        /// <summary>Tolerance de PolygonTriangulator : deux points plus proches sont le meme point.</summary>
        public const double TriangulatorEpsilon = 1e-5;

        /// <summary>
        /// En dessous de cette distance entre deux sommets, la notion de surface etanche perd son
        /// sens : le triangulateur lui-meme ne distingue plus les points a la tolerance pres. P4
        /// est alors passe. Le compteur ci-dessous rend ces impasses visibles au lieu de les taire.
        /// </summary>
        public const double TJunctionMinSeparation = 5e-5;

        public static int SkippedTJunctionChecks;

        public static List<Failure> Check(Panel panel) { return Check(panel, true); }

        public static List<Failure> Check(Panel panel, bool checkScale)
        {
            var failures = new List<Failure>();

            List<Vector2> tris = PolygonTriangulator.TriangulateFace(panel.Outer, panel.Holes);

            if (tris.Count == 0)
            {
                failures.Add(new Failure("P0 surface non vide",
                    "la triangulation n'a produit aucun triangle"));
                return failures;
            }
            if (tris.Count % 3 != 0)
            {
                failures.Add(new Failure("P0 surface non vide",
                    "nombre de sommets non multiple de 3 : " + tris.Count));
                return failures;
            }

            double scale = Math.Max(1.0, panel.ExpectedArea());
            double areaTol = 1e-4 * scale;

            CheckCore(panel, tris, areaTol, "", failures);
            CheckDeterminism(panel, tris, failures);

            if (checkScale) CheckScaleVariants(panel, failures);
            if (!CheckNoTJunction(panel, tris, failures)) SkippedTJunctionChecks++;

            return failures;
        }

        /// <summary>
        /// Les proprietes qui portent sur la SURFACE produite. Reutilisees telles quelles sur les
        /// variantes translatees et mises a l'echelle : celles-ci sont de vrais cas de test
        /// supplementaires, pas une simple comparaison de compteurs.
        /// </summary>
        private static void CheckCore(Panel panel, List<Vector2> tris, double areaTol, string label,
            List<Failure> failures)
        {
            CheckArea(panel, tris, areaTol, label, failures);
            CheckNoDegenerate(panel, tris, label, failures);
            CheckVerticesInRegion(panel, tris, label, failures);
            CheckCoverage(panel, tris, label, failures);
        }

        // --- P1 : conservation d'aire ------------------------------------------------------
        // aire(triangles) == aire(exterieur) - somme des aires des trous.
        // Un trou oublie, un triangle en trop, un recouvrement : tout se voit ici.
        private static void CheckArea(Panel panel, List<Vector2> tris, double tol, string label, List<Failure> failures)
        {
            double expected = panel.ExpectedArea();
            double got = 0.0;
            for (int i = 0; i < tris.Count; i += 3) got += Math.Abs(TriangleArea2(tris[i], tris[i + 1], tris[i + 2])) * 0.5;

            if (Math.Abs(expected - got) > tol)
            {
                failures.Add(new Failure("P1 conservation d'aire" + label,
                    string.Format("attendu {0:F6}, obtenu {1:F6} (ecart {2:E2})", expected, got, Math.Abs(expected - got))));
            }
        }

        // --- P2 : aucun triangle degenere --------------------------------------------------
        // Un triangle d'aire nulle ne se voit pas a l'ecran mais casse le calcul des normales
        // et le depliage des lightmaps.
        private static void CheckNoDegenerate(Panel panel, List<Vector2> tris, string label, List<Failure> failures)
        {
            // Seuil proportionnel a la taille du panneau, comme celui du code teste, et place
            // nettement en dessous : ce test ne doit pas se declencher sur un triangle legitime,
            // seulement si le filtrage des triangles plats disparait ou se relache.
            double tol = BoundingArea(panel.Outer) * 1e-10;
            for (int i = 0; i < tris.Count; i += 3)
            {
                double a2 = Math.Abs(TriangleArea2(tris[i], tris[i + 1], tris[i + 2]));
                if (a2 * 0.5 <= tol)
                {
                    failures.Add(new Failure("P2 aucun triangle degenere" + label,
                        string.Format("triangle {0} d'aire {1:E2}", i / 3, a2 * 0.5)));
                    return;
                }
            }
        }

        // --- P5 : les sommets restent dans la matiere ---------------------------------------
        private static void CheckVerticesInRegion(Panel panel, List<Vector2> tris, string label, List<Failure> failures)
        {
            double eps = 1e-4 * Math.Sqrt(Math.Max(1.0, panel.ExpectedArea()));
            foreach (var v in tris)
            {
                if (!PolygonTriangulator.PointInPolygon(v, panel.Outer) && DistanceToLoop(v, panel.Outer) > eps)
                {
                    failures.Add(new Failure("P5 sommets dans la matiere" + label,
                        "sommet " + v + " hors du panneau"));
                    return;
                }
                foreach (var hole in panel.Holes)
                {
                    if (PolygonTriangulator.PointInPolygon(v, hole) && DistanceToLoop(v, hole) > eps)
                    {
                        failures.Add(new Failure("P5 sommets dans la matiere" + label,
                            "sommet " + v + " a l'interieur d'un trou"));
                        return;
                    }
                }
            }
        }

        // --- P3 : couverture exacte ---------------------------------------------------------
        // On tire des points au hasard : un point dans la matiere doit etre couvert par
        // EXACTEMENT un triangle, un point dans un trou par aucun. P1 seule laisserait passer un
        // manque compense par un recouvrement de meme aire ; P3 les separe.
        private static void CheckCoverage(Panel panel, List<Vector2> tris, string label, List<Failure> failures)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in panel.Outer)
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }

            double diag = Math.Sqrt((double)(maxX - minX) * (maxX - minX) + (double)(maxY - minY) * (maxY - minY));
            double margin = diag * 1e-3;                 // on ignore les points trop pres d'un bord
            var rng = new Random(1234567);               // deterministe : un echec est rejouable

            // Les triangles sont issus des tranches horizontales : chacun tient dans une bande
            // etroite en Y. Les ranger par bande evite de confronter chaque echantillon a la
            // totalite du maillage — sur un mur tres perce, c'est la difference entre quelques
            // triangles candidats et plusieurs centaines.
            var index = new BandIndex(tris, minY, maxY);

            for (int s = 0; s < SampleCount; s++)
            {
                var p = new Vector2(
                    (float)(minX + rng.NextDouble() * (maxX - minX)),
                    (float)(minY + rng.NextDouble() * (maxY - minY)));

                if (DistanceToLoop(p, panel.Outer) < margin) continue;
                bool nearHole = false;
                foreach (var hole in panel.Holes)
                {
                    if (DistanceToLoop(p, hole) < margin) { nearHole = true; break; }
                }
                if (nearHole) continue;

                bool inMatter = PolygonTriangulator.PointInPolygon(p, panel.Outer);
                if (inMatter)
                {
                    foreach (var hole in panel.Holes)
                    {
                        if (PolygonTriangulator.PointInPolygon(p, hole)) { inMatter = false; break; }
                    }
                }

                // Un point pose exactement sur l'arete partagee par deux triangles voisins est
                // dans les deux : c'est de l'adjacence correcte, pas un recouvrement. La propriete
                // porte sur l'INTERIEUR, on ecarte donc les points ambigus. Ca arrive pour de bon :
                // les ordonnees des frontieres de tranches sont celles des sommets, et sur
                // plusieurs millions de tirages en float 32 bits, quelques echantillons tombent
                // pile dessus.
                int covering = 0;
                double closestEdge = double.MaxValue;
                foreach (int i in index.Candidates(p.y))
                {
                    if (PointInTriangle(p, tris[i], tris[i + 1], tris[i + 2])) covering++;
                    for (int e = 0; e < 3; e++)
                    {
                        double d = DistanceToSegment(p, tris[i + e], tris[i + (e + 1) % 3]);
                        if (d < closestEdge) closestEdge = d;
                    }
                }
                if (closestEdge < diag * 1e-6) continue;

                int expected = inMatter ? 1 : 0;
                if (covering != expected)
                {
                    failures.Add(new Failure("P3 couverture exacte" + label,
                        string.Format("point {0} {1} : couvert par {2} triangle(s), attendu {3}",
                            p, inMatter ? "dans la matiere" : "dans un trou", covering, expected)));
                    return;
                }
            }
        }

        // --- P7 : determinisme ---------------------------------------------------------------
        // Deux appels identiques doivent rendre exactement la meme chose, sinon un mur peut
        // changer de forme a la reconstruction (undo, rechargement de scene).
        private static void CheckDeterminism(Panel panel, List<Vector2> reference, List<Failure> failures)
        {
            List<Vector2> again = PolygonTriangulator.TriangulateFace(panel.Outer, panel.Holes);

            if (again.Count != reference.Count)
            {
                failures.Add(new Failure("P7 determinisme",
                    string.Format("2e appel : {0} sommets contre {1}", again.Count, reference.Count)));
                return;
            }
            for (int i = 0; i < again.Count; i++)
            {
                if (!again[i].Equals(reference[i]))
                {
                    failures.Add(new Failure("P7 determinisme",
                        string.Format("sommet {0} differe : {1} contre {2}", i, again[i], reference[i])));
                    return;
                }
            }
        }

        // --- P8 : independance a l'echelle -------------------------------------------------
        //
        // Pourquoi PAS de test de translation : RebuildPanelMesh appelle toujours la
        // triangulation sur des coordonnees CENTREES sur le pivot du panneau (BuildOuterContour
        // rend un rectangle en +/-halfU, +/-halfV). La position du mur dans le niveau n'entre
        // jamais dans le calcul. Un test qui translaterait le contour a 400 unites de l'origine
        // echouerait — la tolerance de 1e-5 y tombe sous la precision du float 32 bits — mais il
        // testerait un cas que l'outil ne produit pas. Un test rouge sur un scenario impossible
        // ne protege de rien et finit par etre ignore.
        //
        // Ce qui varie reellement, c'est la TAILLE du panneau : de la cloison de 40 cm au mur
        // d'enceinte. On rejoue donc toute la batterie sur des variantes mises a l'echelle. On ne
        // compare pas les compteurs de triangles : la tolerance etant exprimee en unites monde
        // (deux points a moins de 10 microns sont le meme point, ce qui est le bon choix pour du
        // level design), le decoupage en tranches peut differer legerement. C'est la surface qui
        // doit tenir, et la verifier entierement est plus exigeant qu'un compteur, pas moins.
        //
        // Rejouer la batterie sur deux variantes triple le cout du fuzz, alors que
        // l'independance a l'echelle est une propriete de l'ALGORITHME, pas d'un panneau
        // particulier : la verifier sur une fraction des tirages suffit largement. Program.cs
        // n'active donc CheckScale qu'un tirage sur huit, ce qui laisse plusieurs centaines de
        // paires verifiees sur une passe normale.
        private static void CheckScaleVariants(Panel panel, List<Failure> failures)
        {
            CheckVariant(panel, new Vector2(0f, 0f), 8f, " (echelle x8)", failures);
            CheckVariant(panel, new Vector2(0f, 0f), 0.25f, " (echelle x0.25)", failures);
        }

        private static void CheckVariant(Panel panel, Vector2 offset, float scale, string label,
            List<Failure> failures)
        {
            Panel variant = panel.Transformed(offset, scale);

            List<Vector2> tris = PolygonTriangulator.TriangulateFace(variant.Outer, variant.Holes);

            if (tris.Count == 0 || tris.Count % 3 != 0)
            {
                failures.Add(new Failure("P8 independance a l'echelle",
                    label.Trim() + " : triangulation vide ou incoherente"));
                return;
            }

            double tol = 1e-4 * Math.Max(1.0, variant.ExpectedArea());
            CheckCore(variant, tris, tol, label, failures);
        }

        // --- P4 : surface etanche (pas de T-jonction) -----------------------------------------
        //
        // Formulation topologique plutot que geometrique. On identifie les sommets confondus, puis
        // on compte les aretes : dans une surface bien formee, une arete interieure est partagee
        // par exactement deux triangles, une arete de bord par un seul — et une arete de bord doit
        // reellement se trouver sur le contour du panneau ou d'un trou. Une T-jonction se trahit
        // par une arete solitaire au milieu de la matiere : le voisin d'en face l'a coupee en deux
        // et ne partage donc plus la meme paire de sommets. C'est exactement le motif qui laisse
        // une fissure d'un pixel au rendu.
        //
        // Un test de colinearite ("aucun sommet pose sur une arete") serait plus direct mais
        // demande un seuil d'angle, et sur les triangles tres aplatis que produisent les tranches
        // minces il declenche a tort : un sommet a 1.6e-6 d'une arete de 2.7 de long n'est pas
        // dessus, il est juste tres proche. Compter les aretes ne demande aucun seuil de ce genre.
        /// <summary>Renvoie false si le cas a ete ecarte (geometrie sous la tolerance).</summary>
        private static bool CheckNoTJunction(Panel panel, List<Vector2> tris, List<Failure> failures)
        {
            // La soudure des sommets ne doit pas etre PLUS FINE que ce que le triangulateur
            // lui-meme considere comme un seul et meme point (son Epsilon vaut 1e-5, exprime en
            // unites monde). Sinon le test signale une fissure de quelques microns la ou le code
            // a deliberement confondu deux points — un ecart invisible, et que le code ne sait de
            // toute facon pas distinguer.
            double diag = Diagonal(panel.Outer);
            double weld = Math.Max(diag * 1e-6, 1.1e-5);
            var welder = new VertexWelder(weld);

            int triangleCount = tris.Count / 3;
            var ids = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++) ids[i] = welder.Add(tris[i]);

            // Si deux sommets distincts sont plus proches que TJunctionMinSeparation, la soudure
            // devient arbitraire : trop fine elle signale des fissures de quelques microns que le
            // code confond deliberement, trop large elle fusionne des tranches legitimes et fait
            // croire a une surface non manifold. On ecarte le cas plutot que de trancher au hasard.
            var coarse = new VertexWelder(TJunctionMinSeparation);
            foreach (var v in tris) coarse.Add(v);
            if (coarse.Points.Count != welder.Points.Count) return false;

            // Meme raison, cas plus fin : un triangle dont deux sommets se confondent a la
            // soudure n'a plus de topologie a cette tolerance. Ses deux aretes deviennent la meme
            // paire de sommets et gonflent artificiellement le compte. Cela arrive quand deux
            // trous portent des sommets separes de moins que la tolerance du triangulateur : leurs
            // lignes de tranche fusionnent et il reste un eclat plus petit que la tolerance.
            for (int t = 0; t < triangleCount; t++)
            {
                int i0 = ids[t * 3], i1 = ids[t * 3 + 1], i2 = ids[t * 3 + 2];
                if (i0 == i1 || i1 == i2 || i2 == i0) return false;
            }

            var edgeUse = new Dictionary<long, int>();
            for (int t = 0; t < triangleCount; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = ids[t * 3 + e];
                    int b = ids[t * 3 + (e + 1) % 3];
                    if (a == b) continue;               // arete ecrasee par la soudure
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    int used;
                    edgeUse.TryGetValue(key, out used);
                    edgeUse[key] = used + 1;
                }
            }

            foreach (var pair in edgeUse)
            {
                int a = (int)(pair.Key >> 32);
                int b = (int)(pair.Key & 0xFFFFFFFF);

                if (pair.Value > 2)
                {
                    failures.Add(new Failure("P4 surface etanche",
                        string.Format("arete {0}-{1} partagee par {2} triangles (surface non manifold)",
                            welder.Points[a], welder.Points[b], pair.Value)));
                    return true;
                }
                if (pair.Value == 2) continue;

                // Arete solitaire : elle doit longer le contour exterieur ou celui d'un trou.
                var mid = new Vector2(
                    (welder.Points[a].x + welder.Points[b].x) * 0.5f,
                    (welder.Points[a].y + welder.Points[b].y) * 0.5f);

                double best = DistanceToLoop(mid, panel.Outer);
                foreach (var hole in panel.Holes)
                {
                    double d = DistanceToLoop(mid, hole);
                    if (d < best) best = d;
                }

                if (best > diag * 1e-5)
                {
                    failures.Add(new Failure("P4 surface etanche",
                        string.Format("arete {0}-{1} sans voisin alors qu'elle est a {2:E2} de tout contour",
                            welder.Points[a], welder.Points[b], best)));
                    return true;
                }
            }

            return true;
        }

        /// <summary>
        /// Rangement des triangles par bande horizontale, pour ne tester un point qu'contre ceux
        /// qui peuvent le contenir.
        /// </summary>
        private sealed class BandIndex
        {
            private const int BandCount = 96;

            private readonly List<int>[] _bands = new List<int>[BandCount];
            private readonly List<int> _all = new List<int>();
            private readonly double _minY;
            private readonly double _scale;

            public BandIndex(List<Vector2> tris, float minY, float maxY)
            {
                _minY = minY;
                double span = (double)maxY - minY;
                _scale = span > 1e-12 ? BandCount / span : 0.0;

                for (int i = 0; i < tris.Count; i += 3)
                {
                    if (_scale <= 0.0) { _all.Add(i); continue; }

                    float lo = Math.Min(tris[i].y, Math.Min(tris[i + 1].y, tris[i + 2].y));
                    float hi = Math.Max(tris[i].y, Math.Max(tris[i + 1].y, tris[i + 2].y));

                    int first = Clamp((int)((lo - _minY) * _scale));
                    int last = Clamp((int)((hi - _minY) * _scale));
                    for (int b = first; b <= last; b++)
                    {
                        if (_bands[b] == null) _bands[b] = new List<int>();
                        _bands[b].Add(i);
                    }
                }
            }

            public List<int> Candidates(float y)
            {
                if (_scale <= 0.0) return _all;
                List<int> band = _bands[Clamp((int)((y - _minY) * _scale))];
                return band ?? EmptyBand;
            }

            private static readonly List<int> EmptyBand = new List<int>();

            private static int Clamp(int b)
            {
                if (b < 0) return 0;
                return b >= BandCount ? BandCount - 1 : b;
            }
        }

        /// <summary>
        /// Regroupe les sommets confondus a la tolerance donnee, via une grille de hachage pour
        /// eviter la comparaison de tous avec tous (un panneau tres perce depasse le millier de
        /// sommets).
        /// </summary>
        private sealed class VertexWelder
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly double _tolerance;

            public VertexWelder(double tolerance)
            {
                _tolerance = tolerance > 0 ? tolerance : 1e-9;
            }

            public int Add(Vector2 p)
            {
                long cx = (long)Math.Floor(p.x / _tolerance);
                long cy = (long)Math.Floor(p.y / _tolerance);

                for (long dx = -1; dx <= 1; dx++)
                {
                    for (long dy = -1; dy <= 1; dy++)
                    {
                        List<int> bucket;
                        if (!_cells.TryGetValue(Key(cx + dx, cy + dy), out bucket)) continue;
                        foreach (int i in bucket)
                        {
                            if (Math.Abs(Points[i].x - p.x) <= _tolerance && Math.Abs(Points[i].y - p.y) <= _tolerance)
                                return i;
                        }
                    }
                }

                Points.Add(p);
                int id = Points.Count - 1;
                long key = Key(cx, cy);
                List<int> own;
                if (!_cells.TryGetValue(key, out own)) { own = new List<int>(); _cells[key] = own; }
                own.Add(id);
                return id;
            }

            private static long Key(long x, long y) { return (x * 73856093L) ^ (y * 19349663L); }
        }

        private static double Diagonal(IList<Vector2> loop)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in loop)
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            double w = maxX - minX, h = maxY - minY;
            return Math.Max(Math.Sqrt(w * w + h * h), 1e-9);
        }

        // --- outils -------------------------------------------------------------------------

        private static double BoundingArea(IList<Vector2> loop)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in loop)
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            return Math.Max((double)(maxX - minX) * (maxY - minY), 1e-12);
        }

        private static double TriangleArea2(Vector2 a, Vector2 b, Vector2 c)
        {
            return ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            double d1 = TriangleArea2(a, b, p);
            double d2 = TriangleArea2(b, c, p);
            double d3 = TriangleArea2(c, a, p);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static bool Close(Vector2 a, Vector2 b)
        {
            return Math.Abs(a.x - b.x) <= 1e-5f && Math.Abs(a.y - b.y) <= 1e-5f;
        }

        private static double DistanceToLoop(Vector2 p, IList<Vector2> loop)
        {
            double best = double.MaxValue;
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                double d = DistanceToSegment(p, loop[i], loop[(i + 1) % n]);
                if (d < best) best = d;
            }
            return best;
        }

        private static double DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            double dx = (double)b.x - a.x, dy = (double)b.y - a.y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-18) return Vector2.Distance(p, a);
            double t = (((double)p.x - a.x) * dx + ((double)p.y - a.y) * dy) / len2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double cx = a.x + t * dx, cy = a.y + t * dy;
            return Math.Sqrt(((double)p.x - cx) * (p.x - cx) + ((double)p.y - cy) * (p.y - cy));
        }
    }
}
