using System.Collections.Generic;
using UnityEngine;

namespace LevelDesignTools
{
    /// <summary>
    /// Le maillage d'un panneau, tel qu'il sera pousse dans un Mesh Unity.
    /// Des listes brutes plutot qu'un Mesh : c'est ce qui permet de construire et de verifier la
    /// geometrie hors de l'editeur.
    /// </summary>
    public sealed class PanelMesh
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();

        public int TriangleCount { get { return Triangles.Count / 3; } }
    }

    /// <summary>
    /// Assemble le maillage 3D d'un panneau (mur / sol / plafond) a partir de sa description
    /// logique : dimensions, reperage des axes, contours des trous.
    ///
    /// Cette classe ne touche a AUCUNE API de l'editeur — ni Mesh, ni MeshFilter, ni Undo, ni
    /// AssetDatabase. C'est deliberé : l'assemblage 3D est la seconde source d'erreurs de l'outil
    /// apres la triangulation, et le garder pur permet de le passer au meme banc de test, a la
    /// meme vitesse. RoomBuilderWindow ne conserve que ce qui releve vraiment d'Unity : ecrire le
    /// mesh dans un asset, recuire le collider, enregistrer l'undo.
    /// </summary>
    public static class PanelMeshBuilder
    {
        /// <summary>
        /// Repere du panneau : U et V portent la surface, W l'epaisseur (l'axe le plus court).
        /// </summary>
        public static void GetFlatAxes(Vector3 size, out int uAxis, out int vAxis, out int wAxis)
        {
            float ax = Mathf.Abs(size.x);
            float ay = Mathf.Abs(size.y);
            float az = Mathf.Abs(size.z);

            if (az <= ax && az <= ay) { wAxis = 2; uAxis = 0; vAxis = 1; }       // mur classique : epaisseur sur Z
            else if (ay <= ax && ay <= az) { wAxis = 1; uAxis = 0; vAxis = 2; }  // sol/plafond : epaisseur sur Y
            else { wAxis = 0; uAxis = 1; vAxis = 2; }                            // epaisseur sur X
        }

        /// <summary>
        /// Repartit les sommets d'un contour (U,V) sur sa boite englobante, pour donner des UV
        /// dans [0,1]. Sans cela tous les sommets partagent l'UV (0,0) et le panneau n'affiche
        /// qu'un seul texel de son materiau — un aplat uni des qu'une texture est appliquee.
        /// </summary>
        private struct FaceUVMapping
        {
            private float _minU, _minV, _spanU, _spanV;

            public static FaceUVMapping From(IList<Vector2> outer)
            {
                float minU = float.MaxValue, maxU = float.MinValue;
                float minV = float.MaxValue, maxV = float.MinValue;
                foreach (var p in outer)
                {
                    if (p.x < minU) minU = p.x;
                    if (p.x > maxU) maxU = p.x;
                    if (p.y < minV) minV = p.y;
                    if (p.y > maxV) maxV = p.y;
                }
                return new FaceUVMapping
                {
                    _minU = minU,
                    _minV = minV,
                    _spanU = Mathf.Max(maxU - minU, 1e-6f),
                    _spanV = Mathf.Max(maxV - minV, 1e-6f),
                };
            }

            public Vector2 Of(Vector2 p)
            {
                return new Vector2((p.x - _minU) / _spanU, (p.y - _minV) / _spanV);
            }
        }

        /// <summary>
        /// Construit le maillage complet : les deux faces, la tranche du contour exterieur et
        /// celle de chaque trou. Renvoie null si la surface est degeneree — l'appelant doit alors
        /// renoncer plutot qu'ecrire un mesh vide.
        ///
        /// "outer" est un contour QUELCONQUE, pas necessairement rectangulaire. C'est ce qui
        /// permet de percer aussi bien un mur qu'un sol dessine a main levee : les deux sont le
        /// meme objet, un contour plat extrude sur une epaisseur.
        /// </summary>
        public static PanelMesh Build(IList<Vector2> outer, float thickness,
            int uAxis, int vAxis, int wAxis, IList<List<Vector2>> holes)
        {
            if (outer == null || outer.Count < 3) return null;

            float halfW = Mathf.Abs(thickness) * 0.5f;
            if (halfW <= 0f) return null;

            var outerLoop = new List<Vector2>(outer);

            var holeLoops = new List<List<Vector2>>();
            if (holes != null)
            {
                foreach (var hole in holes)
                {
                    if (hole != null && hole.Count >= 3) holeLoops.Add(hole);
                }
            }

            List<Vector2> faceTriangles = PolygonTriangulator.TriangulateFace(outerLoop, holeLoops);
            if (faceTriangles.Count < 3) return null;

            FaceUVMapping uvMapping = FaceUVMapping.From(outerLoop);
            var mesh = new PanelMesh();

            AddFace(mesh, faceTriangles, -halfW, uAxis, vAxis, wAxis, uvMapping);
            AddFace(mesh, faceTriangles, halfW, uAxis, vAxis, wAxis, uvMapping);

            // Les chants sont extrudes A PARTIR DU BORD DE LA FACE, et non recalcules depuis les
            // contours d'origine. C'est ce qui garantit qu'ils s'y raccordent : ils reprennent
            // exactement ses sommets, aux memes valeurs, au bit pres.
            //
            // La version precedente redecoupait les contours en parallele et comptait sur les deux
            // calculs pour tomber d'accord. Ils divergeaient des que deux sommets de trous voisins
            // avaient des ordonnees a moins d'un Epsilon : la face les ramenait sur une meme ligne
            // de tranche, le chant gardait les valeurs d'origine, et il restait entre les deux une
            // fissure de quelques microns. Ici la question ne se pose plus.
            foreach (var loop in BoundaryLoops(faceTriangles))
            {
                AddLoopSides(mesh, loop, -halfW, halfW, uAxis, vAxis, wAxis);
            }

            return mesh;
        }

        /// <summary>
        /// Contours du bord de la face : le contour exterieur du panneau et celui de chaque trou.
        ///
        /// Une arete du bord est une arete portee par un seul triangle — toutes les autres sont
        /// interieures et partagees par deux. On les enchaine ensuite bout a bout pour reformer
        /// des boucles fermees, ce qui permet de derouler une coordonnee de texture continue le
        /// long de chaque chant.
        /// </summary>
        private static List<List<Vector2>> BoundaryLoops(List<Vector2> faceTriangles)
        {
            var ids = new Dictionary<Vector2, int>();
            var points = new List<Vector2>();
            int triangleCount = faceTriangles.Count / 3;
            var corners = new int[triangleCount * 3];

            for (int i = 0; i < triangleCount * 3; i++)
            {
                int id;
                if (!ids.TryGetValue(faceTriangles[i], out id))
                {
                    id = points.Count;
                    ids[faceTriangles[i]] = id;
                    points.Add(faceTriangles[i]);
                }
                corners[i] = id;
            }

            // Comptage des aretes, sans tenir compte du sens.
            var uses = new Dictionary<long, int>();
            var directed = new Dictionary<long, int>();   // arete non orientee -> sommet de depart
            for (int t = 0; t < triangleCount; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = corners[t * 3 + e];
                    int b = corners[t * 3 + (e + 1) % 3];
                    if (a == b) continue;

                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    int used;
                    uses.TryGetValue(key, out used);
                    uses[key] = used + 1;
                    directed[key] = a;
                }
            }

            // Enchainement en consommant les aretes une par une, et non en supposant un seul
            // depart par sommet. Un sommet peut en porter deux : il suffit que deux contours se
            // touchent en un point, ce qui arrive sur les tranches tres minces. Consommer les
            // aretes garantit que chacune donne exactement un quad de chant, quelle que soit la
            // facon dont les boucles se croisent.
            var outgoing = new Dictionary<int, List<int>>();
            foreach (var pair in uses)
            {
                if (pair.Value != 1) continue;

                int a = directed[pair.Key];
                int high = (int)(pair.Key >> 32);
                int low = (int)(pair.Key & 0xFFFFFFFF);
                int b = high == a ? low : high;

                List<int> targets;
                if (!outgoing.TryGetValue(a, out targets)) { targets = new List<int>(); outgoing[a] = targets; }
                targets.Add(b);
            }

            var loops = new List<List<Vector2>>();
            var starts = new List<int>(outgoing.Keys);

            foreach (int start in starts)
            {
                while (outgoing[start].Count > 0)
                {
                    var loop = new List<Vector2>();
                    int current = start;
                    bool closed = false;

                    while (true)
                    {
                        List<int> targets;
                        if (!outgoing.TryGetValue(current, out targets) || targets.Count == 0) break;

                        int following = targets[targets.Count - 1];
                        targets.RemoveAt(targets.Count - 1);
                        loop.Add(points[current]);

                        current = following;
                        if (current == start) { closed = true; break; }
                    }

                    // Le bord d'une surface triangulee est toujours fait de cycles fermes ; une
                    // chaine ouverte signalerait une incoherence, et la fermer d'office ajouterait
                    // un quad fantome. On prefere l'ignorer et laisser les tests le dire.
                    if (closed && loop.Count >= 3) loops.Add(loop);
                }
            }

            return loops;
        }

        private static Vector3 MapUVW(float u, float v, float w, int uAxis, int vAxis, int wAxis)
        {
            Vector3 result = Vector3.zero;
            result[uAxis] = u;
            result[vAxis] = v;
            result[wAxis] = w;
            return result;
        }

        /// <summary>
        /// Ajoute un triangle des deux cotes (deux copies de sommets, enroulement oppose) afin que
        /// la face soit visible quel que soit le sens de vue.
        ///
        /// Le prix est connu : six sommets et deux triangles par triangle utile. Le panneau etant
        /// desormais un prisme ferme (deux faces + les chants), l'enroulement sortant est
        /// determinable et ce doublage pourrait etre supprime — a condition de le verifier de
        /// visu dans l'editeur, ce qu'aucun test hors Unity ne peut faire a notre place.
        /// </summary>
        private static void AddTriangleBothSides(PanelMesh mesh, Vector3 a, Vector3 b, Vector3 c,
            Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int i0 = mesh.Vertices.Count;
            mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c);
            mesh.UVs.Add(ua); mesh.UVs.Add(ub); mesh.UVs.Add(uc);
            mesh.Triangles.Add(i0); mesh.Triangles.Add(i0 + 1); mesh.Triangles.Add(i0 + 2);

            int i1 = mesh.Vertices.Count;
            mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c);
            mesh.UVs.Add(ua); mesh.UVs.Add(ub); mesh.UVs.Add(uc);
            mesh.Triangles.Add(i1); mesh.Triangles.Add(i1 + 2); mesh.Triangles.Add(i1 + 1); // ordre inverse -> normale opposee
        }

        private static void AddFace(PanelMesh mesh, List<Vector2> faceTriangles, float w,
            int uAxis, int vAxis, int wAxis, FaceUVMapping uvMapping)
        {
            for (int i = 0; i + 2 < faceTriangles.Count; i += 3)
            {
                Vector2 pa = faceTriangles[i];
                Vector2 pb = faceTriangles[i + 1];
                Vector2 pc = faceTriangles[i + 2];

                AddTriangleBothSides(mesh,
                    MapUVW(pa.x, pa.y, w, uAxis, vAxis, wAxis),
                    MapUVW(pb.x, pb.y, w, uAxis, vAxis, wAxis),
                    MapUVW(pc.x, pc.y, w, uAxis, vAxis, wAxis),
                    uvMapping.Of(pa), uvMapping.Of(pb), uvMapping.Of(pc));
            }
        }

        private static void AddLoopSides(PanelMesh mesh, List<Vector2> loopUV, float wMin, float wMax,
            int uAxis, int vAxis, int wAxis)
        {
            int n = loopUV.Count;
            if (n < 3) return;

            // U suit le developpe du contour (distance parcourue), V suit l'epaisseur : une
            // texture appliquee sur la tranche d'un trou reste ainsi continue.
            float perimeter = 0f;
            for (int i = 0; i < n; i++) perimeter += Vector2.Distance(loopUV[i], loopUV[(i + 1) % n]);
            if (perimeter < 1e-5f) perimeter = 1f;

            float run = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = loopUV[i];
                Vector2 p1 = loopUV[(i + 1) % n];

                float u0 = run / perimeter;
                run += Vector2.Distance(p0, p1);
                float u1 = run / perimeter;

                Vector3 a = MapUVW(p0.x, p0.y, wMin, uAxis, vAxis, wAxis);
                Vector3 b = MapUVW(p1.x, p1.y, wMin, uAxis, vAxis, wAxis);
                Vector3 c = MapUVW(p1.x, p1.y, wMax, uAxis, vAxis, wAxis);
                Vector3 d = MapUVW(p0.x, p0.y, wMax, uAxis, vAxis, wAxis);

                var uvA = new Vector2(u0, 0f);
                var uvB = new Vector2(u1, 0f);
                var uvC = new Vector2(u1, 1f);
                var uvD = new Vector2(u0, 1f);

                AddTriangleBothSides(mesh, a, b, c, uvA, uvB, uvC);
                AddTriangleBothSides(mesh, a, c, d, uvA, uvC, uvD);
            }
        }
    }
}
