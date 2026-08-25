using System.Collections.Generic;

namespace LevelDesignTools.Mutations
{
    /// <summary>
    /// Une mutation : un defaut plausible injecte dans le code de production, et ce qu'on attend
    /// des tests face a lui.
    /// </summary>
    public sealed class Mutation
    {
        public string Name;
        public string File;
        public string Find;
        public string Replace;
        public string Note;
    }

    /// <summary>
    /// Le catalogue des defauts injectes.
    ///
    /// Ils ne sont pas tires au hasard : chacun imite une erreur qu'on commet vraiment en ecrivant
    /// de la geometrie — un seuil absolu la ou il faut du relatif, une borne oubliee, deux axes
    /// intervertis, une comparaison stricte devenue large. Quatre d'entre eux sont des bugs qui
    /// ont reellement existe dans ce fichier et ont ete corriges ; les autres sont des variantes
    /// de la meme famille.
    ///
    /// Muter au hasard produit surtout des programmes qui ne compilent pas, ou des mutations
    /// equivalentes au code d'origine. Un catalogue ecrit a la main dit quelque chose de plus
    /// utile : parmi les erreurs qu'on est susceptible de commettre ici, combien passeraient ?
    /// </summary>
    public static class Catalogue
    {
        private const string Triangulator = "../../Assets/Editor/PolygonTriangulator.cs";
        private const string MeshBuilder = "../../Assets/Editor/PanelMeshBuilder.cs";

        public static List<Mutation> All()
        {
            return new List<Mutation>
            {
                // ---- defauts qui ont reellement existe ----
                new Mutation {
                    Name = "seuil de degenerescence absolu",
                    File = Triangulator,
                    Find = "if (Mathf.Abs(area2) <= areaEpsilon) continue;",
                    Replace = "if (Mathf.Abs(area2) <= 1e-9f) continue;",
                    Note = "bug reel : le maillage dependait de la position et de l'echelle du mur",
                },
                new Mutation {
                    Name = "extrapolation non bornee (bas)",
                    File = Triangulator,
                    Find = "                if (y <= MinY) return A.y < B.y ? A.x : B.x;\n",
                    Replace = "",
                    Note = "bug reel : un sommet finissait a l'interieur d'un trou voisin",
                },
                new Mutation {
                    Name = "extrapolation non bornee (haut)",
                    File = Triangulator,
                    Find = "                if (y >= MaxY) return A.y < B.y ? B.x : A.x;\n",
                    Replace = "",
                    Note = "meme famille, sur l'autre borne",
                },

                // ---- tolerances ----
                new Mutation {
                    Name = "tolerance globale x100",
                    File = Triangulator,
                    Find = "private const float Epsilon = 1e-5f;",
                    Replace = "private const float Epsilon = 1e-3f;",
                    Note = "des lignes de tranche distinctes fusionnent",
                },
                new Mutation {
                    Name = "tolerance globale a zero",
                    File = Triangulator,
                    Find = "private const float Epsilon = 1e-5f;",
                    Replace = "private const float Epsilon = 0f;",
                    Note = "plus aucune fusion : sommets quasi confondus traites comme distincts",
                },

                // ---- comparaisons ----
                new Mutation {
                    Name = "fusion des lignes : comparaison inversee",
                    File = Triangulator,
                    Find = "if (slabLines.Count == 0 || y - slabLines[slabLines.Count - 1] > Epsilon) slabLines.Add(y);",
                    Replace = "if (slabLines.Count == 0 || y - slabLines[slabLines.Count - 1] >= 0f) slabLines.Add(y);",
                    Note = "des tranches d'epaisseur nulle apparaissent",
                },
                new Mutation {
                    Name = "appariement pair/impair decale",
                    File = Triangulator,
                    Find = "for (int i = 0; i + 1 < crossings.Count; i += 2)",
                    Replace = "for (int i = 1; i + 1 < crossings.Count; i += 2)",
                    Note = "la matiere et les trous s'inversent",
                },
                new Mutation {
                    Name = "chaine : borne inferieure relachee",
                    File = Triangulator,
                    Find = "if (x > xMin + Epsilon && x < xMax - Epsilon) chain.Add(x);",
                    Replace = "if (x >= xMin && x < xMax - Epsilon) chain.Add(x);",
                    Note = "un point de coupure double le coin du trapeze",
                },

                // ---- selection de l'arete traversante ----
                new Mutation {
                    Name = "tranche : condition d'appartenance relachee",
                    File = Triangulator,
                    Find = "if (edge.MinY <= y0 + Epsilon && edge.MaxY >= y1 - Epsilon)",
                    Replace = "if (edge.MinY <= y1 && edge.MaxY >= y0)",
                    Note = "des aretes qui ne traversent pas la tranche sont comptees",
                },
                new Mutation {
                    Name = "aretes horizontales conservees",
                    File = Triangulator,
                    Find = "if (Mathf.Abs(a.y - b.y) > Epsilon) edges.Add(new Edge(a, b));",
                    Replace = "edges.Add(new Edge(a, b));",
                    Note = "division par zero en puissance dans XAt",
                },

                // ---- assemblage 3D ----
                new Mutation {
                    Name = "les deux faces a la meme profondeur",
                    File = MeshBuilder,
                    Find = "AddFace(mesh, faceTriangles, halfW, uAxis, vAxis, wAxis, uvMapping);",
                    Replace = "AddFace(mesh, faceTriangles, -halfW, uAxis, vAxis, wAxis, uvMapping);",
                    Note = "le panneau n'a plus d'epaisseur, les deux faces se superposent",
                },
                new Mutation {
                    Name = "axes U et V intervertis",
                    File = MeshBuilder,
                    Find = "result[uAxis] = u;\n            result[vAxis] = v;",
                    Replace = "result[uAxis] = v;\n            result[vAxis] = u;",
                    Note = "le panneau est transpose",
                },
                new Mutation {
                    Name = "epaisseur portee par le mauvais axe",
                    File = MeshBuilder,
                    Find = "if (az <= ax && az <= ay) { wAxis = 2; uAxis = 0; vAxis = 1; }",
                    Replace = "if (az <= ax && az <= ay) { wAxis = 1; uAxis = 0; vAxis = 2; }",
                    Note = "un mur est traite comme une dalle",
                },
                new Mutation {
                    Name = "copie verso au meme enroulement",
                    File = MeshBuilder,
                    Find = "mesh.Triangles.Add(i1); mesh.Triangles.Add(i1 + 2); mesh.Triangles.Add(i1 + 1);",
                    Replace = "mesh.Triangles.Add(i1); mesh.Triangles.Add(i1 + 1); mesh.Triangles.Add(i1 + 2);",
                    Note = "la face n'est plus visible que d'un cote",
                },
                new Mutation {
                    Name = "doublage recto/verso supprime",
                    File = MeshBuilder,
                    Find = "            int i1 = mesh.Vertices.Count;\n            mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c);\n            mesh.UVs.Add(ua); mesh.UVs.Add(ub); mesh.UVs.Add(uc);\n",
                    Replace = "            int i1 = mesh.Vertices.Count;\n",
                    Note = "chaque triangle n'est plus emis qu'une fois",
                },
                new Mutation {
                    Name = "chant du contour exterieur oublie",
                    File = MeshBuilder,
                    Find = "                AddLoopSides(mesh, loop, -halfW, halfW, uAxis, vAxis, wAxis);",
                    Replace = "                if (loop.Count > 4) AddLoopSides(mesh, loop, -halfW, halfW, uAxis, vAxis, wAxis);",
                    Note = "les tranches des contours simples disparaissent",
                },
                new Mutation {
                    Name = "UV ecrasees sur un seul texel",
                    File = MeshBuilder,
                    Find = "return new Vector2((p.x - _minU) / _spanU, (p.y - _minV) / _spanV);",
                    Replace = "return new Vector2(0f, 0f);",
                    Note = "bug deja rencontre : le mur devient un aplat uni",
                },
                new Mutation {
                    Name = "epaisseur nulle acceptee",
                    File = MeshBuilder,
                    Find = "if (halfW <= 0f) return null;",
                    Replace = "if (halfW < 0f) return null;",
                    Note = "un panneau d'epaisseur nulle passe au lieu d'etre refuse",
                },

                // ---- garde-fous de la decoupe ----
                new Mutation {
                    Name = "contour auto-intersectant accepte",
                    File = Triangulator,
                    Find = "                    if (SegmentsIntersect(a0, a1, points[j], points[(j + 1) % n])) return false;",
                    Replace = "",
                    Note = "IsSimplePolygon ne detecte plus les croisements",
                },
                new Mutation {
                    Name = "chevauchement de trous non detecte",
                    File = Triangulator,
                    Find = "            return PointInPolygon(a[0], b) || PointInPolygon(b[0], a);",
                    Replace = "            return false;",
                    Note = "un trou entierement contenu dans un autre passe",
                },
                new Mutation {
                    Name = "marge de bord ignoree",
                    File = Triangulator,
                    Find = "                if (DistanceToLoop(p, outer) <= margin) return false;",
                    Replace = "",
                    Note = "un trou peut affleurer le bord du panneau",
                },
            };
        }
    }
}
