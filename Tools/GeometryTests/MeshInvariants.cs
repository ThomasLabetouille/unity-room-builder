using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelDesignTools.Tests
{
    /// <summary>
    /// Proprietes de l'assemblage 3D — la couche au-dessus de la triangulation, celle qui pose
    /// les deux faces a plus ou moins l'epaisseur, ferme les chants et pose les UV.
    ///
    /// La triangulation 2D etait deja couverte ; ceci ne l'etait pas, et c'est pourtant la que se
    /// logent les erreurs de repere : un axe interverti, une face a la mauvaise profondeur, un
    /// chant qui ne rejoint pas la face. Aucune ne se voit sur une capture d'ecran tant qu'on ne
    /// regarde pas le mur sous le bon angle.
    /// </summary>
    public static class MeshInvariants
    {
        /// <summary>
        /// Les trois reperages possibles d'un panneau : l'epaisseur porte sur Z (mur), sur Y
        /// (sol / plafond) ou sur X. Les trois sont exerces, sinon une interversion d'axes ne
        /// serait detectee que sur les murs.
        /// </summary>
        private static readonly int[][] AxisLayouts =
        {
            new[] { 0, 1, 2 },
            new[] { 0, 2, 1 },
            new[] { 1, 2, 0 },
        };

        /// <summary>
        /// Panneaux dont la face se pince : deux portions de bord s'y rejoignent en un point.
        /// Le maillage reste etanche, mais il cesse d'etre une variete au sens strict et la
        /// propagation d'orientation n'y a plus de sens. Comptes plutot que tus.
        /// </summary>
        public static int PinchedPanels;

        public static List<Failure> Check(Panel panel)
        {
            var failures = new List<Failure>();

            float width = Extent(panel.Outer, true);
            float height = Extent(panel.Outer, false);
            if (width <= 0f || height <= 0f) return failures;

            // Epaisseur deterministe, tiree du cas : toujours le plus petit des trois cotes, comme
            // un vrai mur.
            var rng = new Random(panel.Seed == 0 ? 1 : panel.Seed);
            float thickness = (float)(0.05 + rng.NextDouble() * 0.35) * Math.Min(width, height) * 0.4f;
            if (thickness <= 1e-4f) return failures;

            int[] layout = AxisLayouts[Math.Abs(panel.Seed) % AxisLayouts.Length];
            int uAxis = layout[0], vAxis = layout[1], wAxis = layout[2];

            // Dimensions declarees : uniquement pour verifier le reperage automatique des axes.
            var size = new Vector3();
            size[uAxis] = width;
            size[vAxis] = height;
            size[wAxis] = thickness;

            // M0 : le reperage automatique doit designer le plus petit cote comme epaisseur.
            int au, av, aw;
            PanelMeshBuilder.GetFlatAxes(size, out au, out av, out aw);
            if (aw != wAxis)
            {
                failures.Add(new Failure("M0 reperage des axes",
                    string.Format("epaisseur sur l'axe {0} attendue, GetFlatAxes designe {1}", wAxis, aw)));
                return failures;
            }

            PanelMesh mesh = PanelMeshBuilder.Build(panel.Outer, thickness, uAxis, vAxis, wAxis, panel.Holes);
            if (mesh == null || mesh.TriangleCount == 0)
            {
                failures.Add(new Failure("M1 maillage produit", "PanelMeshBuilder n'a rien renvoye"));
                return failures;
            }
            if (mesh.Vertices.Count != mesh.UVs.Count)
            {
                failures.Add(new Failure("M1 maillage produit",
                    string.Format("{0} sommets pour {1} UV", mesh.Vertices.Count, mesh.UVs.Count)));
                return failures;
            }

            var ids = Weld(mesh.Vertices);

            List<int[]> singleSided;
            CheckMirrorPairs(mesh, ids, failures, out singleSided);
            if (failures.Count > 0) return failures;

            if (FacePinches(panel)) PinchedPanels++;
            else CheckClosedAndOriented(panel, mesh, ids, singleSided, thickness, failures);
            CheckBounds(mesh, panel.Outer, thickness, uAxis, vAxis, wAxis, failures);
            CheckUVs(mesh, failures);
            CheckDeterminism(panel.Outer, thickness, uAxis, vAxis, wAxis, panel.Holes, mesh, failures);

            return failures;
        }

        // --- M2 : chaque triangle est emis exactement deux fois, en enroulements opposes --------
        //
        // C'est le choix de conception actuel : la face est generee recto ET verso pour ne
        // dependre d'aucune convention d'enroulement. Le verifier a deux vertus — une face emise
        // une seule fois serait invisible d'un cote, et le jour ou l'on supprimera le doublage,
        // ce test dira exactement ce qui a change.
        private static void CheckMirrorPairs(PanelMesh mesh, int[] ids, List<Failure> failures,
            out List<int[]> singleSided)
        {
            singleSided = new List<int[]>();
            var groups = new Dictionary<long, List<int>>();

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int a = ids[mesh.Triangles[t * 3]];
                int b = ids[mesh.Triangles[t * 3 + 1]];
                int c = ids[mesh.Triangles[t * 3 + 2]];

                if (a == b || b == c || c == a)
                {
                    failures.Add(new Failure("M2 doublage recto/verso",
                        string.Format("triangle {0} degenere : deux sommets confondus", t)));
                    return;
                }

                long key = TriangleKey(a, b, c);
                List<int> group;
                if (!groups.TryGetValue(key, out group)) { group = new List<int>(); groups[key] = group; }
                group.Add(t);
            }

            foreach (var pair in groups)
            {
                List<int> group = pair.Value;
                if (group.Count != 2)
                {
                    failures.Add(new Failure("M2 doublage recto/verso",
                        string.Format("un triangle apparait {0} fois au lieu de 2", group.Count)));
                    return;
                }

                int t0 = group[0], t1 = group[1];
                if (Parity(ids, mesh.Triangles, t0) == Parity(ids, mesh.Triangles, t1))
                {
                    failures.Add(new Failure("M2 doublage recto/verso",
                        "les deux copies d'un triangle ont le meme enroulement (face invisible d'un cote)"));
                    return;
                }

                singleSided.Add(new[]
                {
                    ids[mesh.Triangles[t0 * 3]], ids[mesh.Triangles[t0 * 3 + 1]], ids[mesh.Triangles[t0 * 3 + 2]],
                });
            }
        }

        // --- M3 : le maillage deduplique est une surface fermee, orientable, du bon volume ------
        //
        // On retire une copie sur deux, puis on verifie que ce qui reste est un prisme ferme :
        // chaque arete partagee par exactement deux triangles, une orientation coherente
        // propageable de proche en proche, et un volume signe egal a aire x epaisseur.
        //
        // C'est la propriete qui rendra la suppression du doublage sure : elle etablit qu'une
        // surface simple orientee existe bel et bien, et de quel cote elle regarde.
        private static void CheckClosedAndOriented(Panel panel, PanelMesh mesh, int[] ids,
            List<int[]> tris, float thickness, List<Failure> failures)
        {
            var edgeOwners = new Dictionary<long, List<int>>();
            for (int t = 0; t < tris.Count; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    long key = EdgeKey(tris[t][e], tris[t][(e + 1) % 3]);
                    List<int> owners;
                    if (!edgeOwners.TryGetValue(key, out owners)) { owners = new List<int>(); edgeOwners[key] = owners; }
                    owners.Add(t);
                }
            }

            foreach (var pair in edgeOwners)
            {
                if (pair.Value.Count == 2) continue;
                failures.Add(new Failure("M3 prisme ferme",
                    string.Format("une arete est portee par {0} triangle(s) au lieu de 2 — la surface n'est pas fermee",
                        pair.Value.Count)));
                return;
            }

            // Orientation propagee de proche en proche : deux triangles voisins doivent parcourir
            // leur arete commune en sens contraire.
            var flipped = new bool[tris.Count];
            var visited = new bool[tris.Count];
            var stack = new Stack<int>();
            stack.Push(0);
            visited[0] = true;

            while (stack.Count > 0)
            {
                int t = stack.Pop();
                for (int e = 0; e < 3; e++)
                {
                    int a = Corner(tris[t], flipped[t], e);
                    int b = Corner(tris[t], flipped[t], (e + 1) % 3);

                    foreach (int other in edgeOwners[EdgeKey(a, b)])
                    {
                        if (other == t) continue;
                        bool sameDirection = SharesDirectedEdge(tris[other], flipped[other], a, b);
                        if (!visited[other])
                        {
                            visited[other] = true;
                            flipped[other] = sameDirection;   // meme sens = voisin a retourner
                            stack.Push(other);
                        }
                        else if (SharesDirectedEdge(tris[other], flipped[other], a, b))
                        {
                            failures.Add(new Failure("M3 prisme ferme",
                                "orientation incoherente : deux triangles voisins parcourent leur arete commune dans le meme sens"));
                            return;
                        }
                    }
                }
            }

            for (int t = 0; t < tris.Count; t++)
            {
                if (visited[t]) continue;
                failures.Add(new Failure("M3 prisme ferme",
                    "le maillage se compose de plusieurs morceaux disjoints"));
                return;
            }

            // Volume signe (theoreme de la divergence) : positif ou negatif selon le cote choisi
            // arbitrairement par la propagation, mais sa valeur absolue est le volume du prisme.
            var positions = new Vector3[mesh.Vertices.Count];
            for (int i = 0; i < mesh.Vertices.Count; i++) positions[ids[i]] = mesh.Vertices[i];

            double volume = 0.0;
            for (int t = 0; t < tris.Count; t++)
            {
                Vector3 a = positions[Corner(tris[t], flipped[t], 0)];
                Vector3 b = positions[Corner(tris[t], flipped[t], 1)];
                Vector3 c = positions[Corner(tris[t], flipped[t], 2)];
                volume += Determinant(a, b, c) / 6.0;
            }

            double expected = panel.ExpectedArea() * thickness;
            double tol = 1e-3 * Math.Max(1e-6, expected);
            if (Math.Abs(Math.Abs(volume) - expected) > tol)
            {
                failures.Add(new Failure("M3 prisme ferme",
                    string.Format("volume {0:F8} au lieu de {1:F8} (aire x epaisseur)", Math.Abs(volume), expected)));
            }
        }

        /// <summary>
        /// Vrai si le bord de la face passe deux fois par un meme sommet. On le determine
        /// directement depuis la triangulation 2D, sans rien demander a PanelMeshBuilder : la
        /// precondition doit etre etablie independamment de ce qu'elle conditionne.
        /// </summary>
        private static bool FacePinches(Panel panel)
        {
            List<Vector2> tris = PolygonTriangulator.TriangulateFace(panel.Outer, panel.Holes);
            if (tris.Count < 3) return false;

            var ids = new Dictionary<Vector2, int>();
            int count = tris.Count / 3;
            var corners = new int[count * 3];
            for (int i = 0; i < count * 3; i++)
            {
                int id;
                if (!ids.TryGetValue(tris[i], out id)) { id = ids.Count; ids[tris[i]] = id; }
                corners[i] = id;
            }

            var uses = new Dictionary<long, int>();
            for (int t = 0; t < count; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = corners[t * 3 + e], b = corners[t * 3 + (e + 1) % 3];
                    if (a == b) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    int used;
                    uses.TryGetValue(key, out used);
                    uses[key] = used + 1;
                }
            }

            var degree = new Dictionary<int, int>();
            foreach (var pair in uses)
            {
                if (pair.Value != 1) continue;
                int a = (int)(pair.Key >> 32), b = (int)(pair.Key & 0xFFFFFFFF);
                int d;
                degree.TryGetValue(a, out d); degree[a] = d + 1;
                degree.TryGetValue(b, out d); degree[b] = d + 1;
            }

            foreach (var pair in degree)
            {
                if (pair.Value > 2) return true;
            }
            return false;
        }

        // --- M4 : le panneau tient exactement dans sa boite, et la remplit ---------------------
        //
        // La boite se deduit du CONTOUR, pas de dimensions declarees a cote : un contour libre
        // n'est pas centre, et le supposer masquerait justement les erreurs de repere.
        private static void CheckBounds(PanelMesh mesh, List<Vector2> outer, float thickness,
            int uAxis, int vAxis, int wAxis, List<Failure> failures)
        {
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var p in outer)
            {
                if (p.x < minU) minU = p.x;
                if (p.x > maxU) maxU = p.x;
                if (p.y < minV) minV = p.y;
                if (p.y > maxV) maxV = p.y;
            }
            float halfW = Math.Abs(thickness) * 0.5f;
            double slack = 1e-4 * Math.Max(1.0, Math.Max(maxU - minU, maxV - minV));

            double gotMinU = double.MaxValue, gotMaxU = double.MinValue;
            double gotMinV = double.MaxValue, gotMaxV = double.MinValue;
            double gotMaxW = 0;

            foreach (var p in mesh.Vertices)
            {
                double u = p[uAxis], v = p[vAxis], w = Math.Abs(p[wAxis]);
                if (u < gotMinU) gotMinU = u;
                if (u > gotMaxU) gotMaxU = u;
                if (v < gotMinV) gotMinV = v;
                if (v > gotMaxV) gotMaxV = v;
                if (w > gotMaxW) gotMaxW = w;

                if (u < minU - slack || u > maxU + slack || v < minV - slack || v > maxV + slack
                    || w > halfW + slack)
                {
                    failures.Add(new Failure("M4 boite du panneau",
                        "le sommet " + p + " sort de la boite du contour"));
                    return;
                }
            }

            // La geometrie doit aussi REMPLIR sa boite : un panneau qui ne toucherait pas ses
            // propres bords trahirait une echelle perdue en route — le defaut exact qui rendait
            // les murs decoupes trop petits avant la reprise de l'outil.
            if (Math.Abs(gotMinU - minU) > slack || Math.Abs(gotMaxU - maxU) > slack
                || Math.Abs(gotMinV - minV) > slack || Math.Abs(gotMaxV - maxV) > slack
                || Math.Abs(gotMaxW - halfW) > slack)
            {
                failures.Add(new Failure("M4 boite du panneau",
                    string.Format("la geometrie occupe U[{0:F5},{1:F5}] V[{2:F5},{3:F5}] W±{4:F5} " +
                                  "pour un contour U[{5:F5},{6:F5}] V[{7:F5},{8:F5}] W±{9:F5}",
                        gotMinU, gotMaxU, gotMinV, gotMaxV, gotMaxW, minU, maxU, minV, maxV, halfW)));
            }
        }

        // --- M5 : UV exploitables ----------------------------------------------------------
        //
        // Le premier test se contentait de verifier que tous les sommets ne partageaient pas le
        // MEME UV. Insuffisant : ecraser les UV de la face seule le laissait passer, celles des
        // chants continuant de varier. C'est un defaut deja survenu ici, et l'injection de defauts
        // l'a remis en evidence. On raisonne donc par triangle : un triangle non degenere dans
        // l'espace ne peut pas se replier sur un point dans l'espace des textures.
        private static void CheckUVs(PanelMesh mesh, List<Failure> failures)
        {
            const float slack = 1e-4f;

            foreach (var uv in mesh.UVs)
            {
                if (uv.x < -slack || uv.x > 1f + slack || uv.y < -slack || uv.y > 1f + slack)
                {
                    failures.Add(new Failure("M5 UV dans [0,1]", "UV hors bornes : " + uv));
                    return;
                }
            }

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int i0 = mesh.Triangles[t * 3], i1 = mesh.Triangles[t * 3 + 1], i2 = mesh.Triangles[t * 3 + 2];

                Vector3 a = mesh.Vertices[i0], b = mesh.Vertices[i1], c = mesh.Vertices[i2];
                if (a.Equals(b) || b.Equals(c) || c.Equals(a)) continue;   // deja couvert par M2

                Vector2 ua = mesh.UVs[i0], ub = mesh.UVs[i1], uc = mesh.UVs[i2];
                double uvArea = Math.Abs(((double)ub.x - ua.x) * ((double)uc.y - ua.y)
                                       - ((double)ub.y - ua.y) * ((double)uc.x - ua.x)) * 0.5;

                if (uvArea <= 1e-12)
                {
                    failures.Add(new Failure("M5 UV dans [0,1]",
                        string.Format("le triangle {0} occupe une aire nulle dans l'espace des textures " +
                                      "— sa portion de mur n'afficherait qu'un seul texel", t)));
                    return;
                }
            }
        }

        // --- M6 : deterministe -----------------------------------------------------------------
        private static void CheckDeterminism(List<Vector2> outer, float thickness, int uAxis, int vAxis, int wAxis,
            List<List<Vector2>> holes, PanelMesh reference, List<Failure> failures)
        {
            PanelMesh again = PanelMeshBuilder.Build(outer, thickness, uAxis, vAxis, wAxis, holes);
            if (again == null || again.Vertices.Count != reference.Vertices.Count)
            {
                failures.Add(new Failure("M6 determinisme", "le 2e assemblage ne rend pas le meme nombre de sommets"));
                return;
            }
            for (int i = 0; i < again.Vertices.Count; i++)
            {
                if (!again.Vertices[i].Equals(reference.Vertices[i]))
                {
                    failures.Add(new Failure("M6 determinisme",
                        string.Format("sommet {0} : {1} contre {2}", i, again.Vertices[i], reference.Vertices[i])));
                    return;
                }
            }
        }

        // --- outils ----------------------------------------------------------------------------

        /// <summary>
        /// Regroupe les sommets par egalite EXACTE. PanelMeshBuilder calcule les positions
        /// coincidentes par les memes expressions sur les memes valeurs : elles sont identiques au
        /// bit pres. Une tolerance masquerait justement les ecarts que ce test doit voir.
        /// </summary>
        private static int[] Weld(List<Vector3> vertices)
        {
            var ids = new int[vertices.Count];
            var map = new Dictionary<Vector3, int>();
            for (int i = 0; i < vertices.Count; i++)
            {
                int id;
                if (!map.TryGetValue(vertices[i], out id))
                {
                    id = map.Count;
                    map[vertices[i]] = id;
                }
                ids[i] = id;
            }
            return ids;
        }

        private static long TriangleKey(int a, int b, int c)
        {
            int lo = Math.Min(a, Math.Min(b, c));
            int hi = Math.Max(a, Math.Max(b, c));
            int mid = a + b + c - lo - hi;
            return ((long)lo * 1000003L + mid) * 1000003L + hi;
        }

        private static long EdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        /// <summary>Parite de la permutation qui trie les trois sommets : distingue les deux enroulements.</summary>
        private static bool Parity(int[] ids, List<int> triangles, int t)
        {
            int a = ids[triangles[t * 3]], b = ids[triangles[t * 3 + 1]], c = ids[triangles[t * 3 + 2]];
            int swaps = 0;
            if (a > b) { int s = a; a = b; b = s; swaps++; }
            if (b > c) { int s = b; b = c; c = s; swaps++; }
            if (a > b) { int s = a; a = b; b = s; swaps++; }
            return (swaps & 1) == 0;
        }

        private static int Corner(int[] tri, bool flipped, int e)
        {
            return flipped ? tri[(3 - e) % 3] : tri[e];
        }

        private static bool SharesDirectedEdge(int[] tri, bool flipped, int a, int b)
        {
            for (int e = 0; e < 3; e++)
            {
                if (Corner(tri, flipped, e) == a && Corner(tri, flipped, (e + 1) % 3) == b) return true;
            }
            return false;
        }

        private static double Determinant(Vector3 a, Vector3 b, Vector3 c)
        {
            return (double)a.x * ((double)b.y * c.z - (double)b.z * c.y)
                 - (double)a.y * ((double)b.x * c.z - (double)b.z * c.x)
                 + (double)a.z * ((double)b.x * c.y - (double)b.y * c.x);
        }

        private static float Extent(List<Vector2> loop, bool horizontal)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in loop)
            {
                float v = horizontal ? p.x : p.y;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return max - min;
        }
    }
}
