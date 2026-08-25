using System.Collections.Generic;
using UnityEngine;

namespace LevelDesignTools.Tests
{
    /// <summary>
    /// Les cas deja rencontres, figes une fois pour toutes.
    ///
    /// Ils ne remplacent pas le fuzz : ce sont ses trouvailles, mises de cote pour qu'un bug
    /// corrige ne revienne jamais sans qu'on le sache. Quand le fuzz signale un echec, il imprime
    /// le cas en C# collable : on l'ajoute ici, puis on corrige.
    /// </summary>
    public static class Regressions
    {
        public static IEnumerable<Panel> All()
        {
            // Mur neuf, pas encore perce.
            yield return new Panel
            {
                Profile = "mur plein",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
            };

            // Le cas d'usage de base : une porte.
            yield return new Panel
            {
                Profile = "une porte",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
                // Le bas de la porte s'arrete a 10 cm du sol : la decoupe exige un contour
                // STRICTEMENT interieur au panneau. Un trou affleurant le bord fusionnerait avec
                // le contour exterieur, et le chant genere autour du trou ferait double emploi
                // avec celui du panneau — la surface ne serait plus fermee.
                Holes = new List<List<Vector2>> { Generators.Rect(-2f, -0.45f, 1.2f, 1.9f) },
            };

            // Porte + deux fenetres : l'ancien ear-clipping avec ponts rendait une surface vide.
            yield return new Panel
            {
                Profile = "porte + 2 fenetres",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
                Holes = new List<List<Vector2>>
                {
                    Generators.Rect(-2.5f, -0.45f, 1.2f, 1.9f),
                    Generators.Rect(0.5f, 0.4f, 1f, 0.8f),
                    Generators.Rect(2.5f, 0.4f, 1f, 0.8f),
                },
            };

            // Trous partageant exactement les memes Y : c'est la configuration qui faisait
            // echouer le pontage (le rayon horizontal longeait une arete au lieu de la couper).
            yield return new Panel
            {
                Profile = "3 trous alignes en Y",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
                Holes = new List<List<Vector2>>
                {
                    Generators.Rect(-2f, 0f, 1f, 1f),
                    Generators.Rect(0f, 0f, 1f, 1f),
                    Generators.Rect(2f, 0f, 1f, 1f),
                },
            };

            yield return new Panel
            {
                Profile = "5 trous alignes",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
                Holes = new List<List<Vector2>>
                {
                    Generators.Rect(-3.2f, 0f, 0.7f, 1f),
                    Generators.Rect(-1.6f, 0f, 0.7f, 1f),
                    Generators.Rect(0f, 0f, 0.7f, 1f),
                    Generators.Rect(1.6f, 0f, 0.7f, 1f),
                    Generators.Rect(3.2f, 0f, 0.7f, 1f),
                },
            };

            // Trou non rectangulaire : le contour est dessine a la main, rien n'oblige a 4 points.
            yield return new Panel
            {
                Profile = "trou triangulaire",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
                Holes = new List<List<Vector2>>
                {
                    new List<Vector2> { new Vector2(-3f, -1f), new Vector2(-2f, -1f), new Vector2(-2.5f, 1f) },
                },
            };

            // Trou concave : sommets rentrants, le cas ou l'ear-clipping se trompe le plus.
            yield return new Panel
            {
                Profile = "trou concave en L",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
                Holes = new List<List<Vector2>>
                {
                    new List<Vector2>
                    {
                        new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 0f),
                        new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 1f),
                    },
                },
            };

            // Trous empiles verticalement : partagent des X a l'identique.
            yield return new Panel
            {
                Profile = "trous empiles",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
                Holes = new List<List<Vector2>>
                {
                    Generators.Rect(0f, -0.8f, 1f, 0.8f),
                    Generators.Rect(0f, 0.8f, 1f, 0.8f),
                },
            };

            // Mur etroit et haut : l'epaisseur tombe sur un autre axe, les proportions changent.
            yield return new Panel
            {
                Profile = "mur etroit",
                Outer = Generators.Rect(0f, 0f, 1.2f, 6f),
                Holes = new List<List<Vector2>>
                {
                    Generators.Rect(0f, -1.5f, 0.6f, 1.6f),
                    Generators.Rect(0f, 1.5f, 0.6f, 1.6f),
                },
            };

            // Trouve par le fuzz (graine 99), reduit de 14 trous a 2. Deux sommets voisins d'un
            // meme trou separes de 2.8e-3 en Y pour 0.28 en X : la pente vaut 100, si bien qu'une
            // extrapolation de XAt d'un Epsilon (1e-5) en Y projetait le point de coupure a 1e-3
            // en X — assez pour le poser a l'interieur du trou. Corrige en bornant y a l'etendue
            // de l'arete dans XAt. Le defaut n'apparaissait qu'a petite echelle, ou 1e-5 cesse
            // d'etre negligeable devant la taille des trous.
            yield return new Panel
            {
                Profile = "aretes a forte pente (fuzz seed 900)",
                Outer = new List<Vector2> { new Vector2(-4.65457726f, -0.9467581f), new Vector2(4.65457726f, -0.9467581f), new Vector2(4.65457726f, 0.9467581f), new Vector2(-4.65457726f, 0.9467581f) },
                Holes = new List<List<Vector2>>
                {
                    new List<Vector2> { new Vector2(-3.14853978f, 0.46439153f), new Vector2(-2.86916137f, 0.4616068f), new Vector2(-2.66296721f, 0.35979f), new Vector2(-2.65780926f, 0.5106293f), new Vector2(-2.46922851f, 0.6219558f), new Vector2(-2.748607f, 0.624740541f), new Vector2(-2.954801f, 0.7265574f), new Vector2(-2.959959f, 0.575718045f) },
                    new List<Vector2> { new Vector2(0.05339361f, 0.480123758f), new Vector2(-0.19610846f, 0.461587638f), new Vector2(-0.325056255f, 0.343810767f), new Vector2(-0.236349434f, 0.21548149f), new Vector2(0.0032139346f, 0.1732344f), new Vector2(0.213237762f, 0.248882413f), new Vector2(0.235569835f, 0.385461032f) },
                },
            };

            // Le meme cas, retourne verticalement. L'injection de defauts a montre que la borne
            // HAUTE de XAt pouvait etre retiree sans qu'aucun test ne bronche, alors que la borne
            // basse etait bien couverte : la batterie ne voyait qu'une moitie du probleme.
            // Retourner un cas connu est le moyen le plus court d'exercer l'autre moitie.
            yield return Mirrored(new Panel
            {
                Profile = "aretes a forte pente, retournees",
                Outer = new List<Vector2> { new Vector2(-4.65457726f, -0.9467581f), new Vector2(4.65457726f, -0.9467581f), new Vector2(4.65457726f, 0.9467581f), new Vector2(-4.65457726f, 0.9467581f) },
                Holes = new List<List<Vector2>>
                {
                    new List<Vector2> { new Vector2(-3.14853978f, 0.46439153f), new Vector2(-2.86916137f, 0.4616068f), new Vector2(-2.66296721f, 0.35979f), new Vector2(-2.65780926f, 0.5106293f), new Vector2(-2.46922851f, 0.6219558f), new Vector2(-2.748607f, 0.624740541f), new Vector2(-2.954801f, 0.7265574f), new Vector2(-2.959959f, 0.575718045f) },
                    new List<Vector2> { new Vector2(0.05339361f, 0.480123758f), new Vector2(-0.19610846f, 0.461587638f), new Vector2(-0.325056255f, 0.343810767f), new Vector2(-0.236349434f, 0.21548149f), new Vector2(0.0032139346f, 0.1732344f), new Vector2(0.213237762f, 0.248882413f), new Vector2(0.235569835f, 0.385461032f) },
                },
            });

            // Trouve par le fuzz : ces trous en etoile produisent un eclat d'aire 1.9e-9 que le
            // filtre des triangles plats, cale sur un seuil ABSOLU de 1e-9, laissait passer. Le
            // seuil est devenu proportionnel a la taille du panneau. Verifie rouge sur la version
            // d'avant : un cas de regression qu'on n'a pas vu echouer ne prouve rien.
            yield return new Panel
            {
                Profile = "eclats sous le seuil (fuzz seed 2369)",
                Outer = new List<Vector2> { new Vector2(-5.754332f, -1.09329283f), new Vector2(5.754332f, -1.09329283f), new Vector2(5.754332f, 1.09329283f), new Vector2(-5.754332f, 1.09329283f) },
                Holes = new List<List<Vector2>>
                {
                    new List<Vector2> { new Vector2(-1.09094882f, -0.457403958f), new Vector2(-1.40180147f, -0.377925158f), new Vector2(-1.397036f, -0.200090259f), new Vector2(-1.64077723f, -0.326847583f), new Vector2(-1.96974635f, -0.2695738f), new Vector2(-1.80953407f, -0.42739293f), new Vector2(-2.01761365f, -0.569830656f), new Vector2(-1.67485559f, -0.5406109f), new Vector2(-1.47448683f, -0.685916066f), new Vector2(-1.42286313f, -0.5100382f) },
                    new List<Vector2> { new Vector2(-4.371577f, -0.398833543f), new Vector2(-4.63107347f, -0.321237534f), new Vector2(-4.38421774f, -0.242099345f), new Vector2(-4.844165f, -0.266080558f), new Vector2(-5.155188f, -0.196002051f), new Vector2(-5.179954f, -0.288419276f), new Vector2(-5.619033f, -0.324246556f), new Vector2(-5.174392f, -0.357382327f), new Vector2(-5.13473463f, -0.449603319f), new Vector2(-4.8351655f, -0.377665132f) },
                    new List<Vector2> { new Vector2(4.929348f, -0.785186231f), new Vector2(5.10110855f, -0.572184265f), new Vector2(5.46783352f, -0.47961238f), new Vector2(5.108065f, -0.377920955f), new Vector2(4.951708f, -0.160800219f), new Vector2(4.77994728f, -0.373802155f), new Vector2(4.41322231f, -0.466374069f), new Vector2(4.7729907f, -0.5680655f) },
                },
            };

            // Sol de salle dessinee a main levee : contour exterieur non rectangulaire, avec une
            // tremie. C'est ce que la decoupe refusait jusqu'ici — l'aide de l'outil l'annonçait
            // noir sur blanc — et que le panneau a contour libre rend possible.
            yield return new Panel
            {
                Profile = "sol en L, une tremie",
                Outer = new List<Vector2>
                {
                    new Vector2(-4f, -3f), new Vector2(4f, -3f), new Vector2(4f, 0f),
                    new Vector2(0f, 0f), new Vector2(0f, 3f), new Vector2(-4f, 3f),
                },
                Holes = new List<List<Vector2>> { Generators.Rect(-2f, -1.5f, 1.4f, 1.4f) },
            };

            // Meme chose sur un contour convexe quelconque, avec deux trous.
            yield return new Panel
            {
                Profile = "sol hexagonal, deux trous",
                Outer = new List<Vector2>
                {
                    new Vector2(-3f, -1.6f), new Vector2(0f, -2.6f), new Vector2(3f, -1.6f),
                    new Vector2(3f, 1.6f), new Vector2(0f, 2.6f), new Vector2(-3f, 1.6f),
                },
                Holes = new List<List<Vector2>>
                {
                    Generators.Rect(-1.2f, 0f, 0.9f, 0.9f),
                    Generators.Rect(1.2f, 0f, 0.9f, 0.9f),
                },
            };

            // Meurtriere : trou tres fin, a la limite de la degenerescence.
            yield return new Panel
            {
                Profile = "trou tres fin",
                Outer = Generators.Rect(0f, 0f, 8f, 3f),
                Holes = new List<List<Vector2>> { Generators.Rect(0f, 0f, 0.02f, 2f) },
            };
        }

        /// <summary>
        /// Les garde-fous de la decoupe : ils decident si un contour dessine est acceptable.
        /// Un faux negatif ici laisse passer une geometrie invalide, un faux positif refuse une
        /// decoupe legitime sous les yeux du level designer.
        /// </summary>
        public static IEnumerable<Failure> CheckContractHelpers()
        {
            var square = Generators.Rect(0f, 0f, 2f, 2f);

            yield return Expect("IsSimplePolygon: carre", PolygonTriangulator.IsSimplePolygon(square), true);

            yield return Expect("IsSimplePolygon: noeud papillon",
                PolygonTriangulator.IsSimplePolygon(new List<Vector2>
                {
                    new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(2f, 0f), new Vector2(0f, 2f),
                }), false);

            yield return Expect("IsSimplePolygon: point double",
                PolygonTriangulator.IsSimplePolygon(new List<Vector2>
                {
                    new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 0f), new Vector2(1f, 2f),
                }), false);

            yield return Expect("IsSimplePolygon: 2 points seulement",
                PolygonTriangulator.IsSimplePolygon(new List<Vector2>
                {
                    new Vector2(0f, 0f), new Vector2(1f, 1f),
                }), false);

            yield return Expect("IsSimplePolygon: contour concave",
                PolygonTriangulator.IsSimplePolygon(new List<Vector2>
                {
                    new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f),
                    new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(0f, 2f),
                }), true);

            yield return Expect("PolygonsOverlap: disjoints",
                PolygonTriangulator.PolygonsOverlap(Generators.Rect(0f, 0f, 1f, 1f), Generators.Rect(3f, 0f, 1f, 1f)), false);

            yield return Expect("PolygonsOverlap: secants",
                PolygonTriangulator.PolygonsOverlap(Generators.Rect(0f, 0f, 1f, 1f), Generators.Rect(0.5f, 0f, 1f, 1f)), true);

            yield return Expect("PolygonsOverlap: l'un dans l'autre",
                PolygonTriangulator.PolygonsOverlap(Generators.Rect(0f, 0f, 0.4f, 0.4f), Generators.Rect(0f, 0f, 2f, 2f)), true);

            yield return Expect("PolygonsOverlap: bord commun",
                PolygonTriangulator.PolygonsOverlap(Generators.Rect(0f, 0f, 1f, 1f), Generators.Rect(1f, 0f, 1f, 1f)), true);

            yield return Expect("PointInPolygon: dedans",
                PolygonTriangulator.PointInPolygon(new Vector2(0f, 0f), square), true);

            yield return Expect("PointInPolygon: dehors",
                PolygonTriangulator.PointInPolygon(new Vector2(5f, 0f), square), false);

            // Un trou doit etre STRICTEMENT interieur : affleurer le bord ferait fusionner son
            // contour avec celui du panneau, et le chant genere autour ferait double emploi.
            yield return Expect("ContainsWithMargin: trou bien a l'interieur",
                PolygonTriangulator.ContainsWithMargin(Generators.Rect(0f, 0f, 4f, 4f),
                    Generators.Rect(0f, 0f, 1f, 1f), 0.001f), true);

            yield return Expect("ContainsWithMargin: trou affleurant le bord",
                PolygonTriangulator.ContainsWithMargin(Generators.Rect(0f, 0f, 4f, 4f),
                    Generators.Rect(0f, -1.5f, 1f, 1f), 0.001f), false);

            // Strictement interieur, mais a moins de la marge du bord : c'est le seul cas que la
            // marge attrape a elle seule. Un trou qui affleure vraiment est deja refuse par le
            // test de croisement des aretes — s'en contenter laissait la marge entierement
            // dispensable sans que rien ne le signale.
            yield return Expect("ContainsWithMargin: trou a 0,5 mm du bord",
                PolygonTriangulator.ContainsWithMargin(Generators.Rect(0f, 0f, 4f, 4f),
                    Generators.Rect(0f, -1.4995f, 1f, 1f), 0.001f), false);

            yield return Expect("ContainsWithMargin: trou a 5 cm du bord",
                PolygonTriangulator.ContainsWithMargin(Generators.Rect(0f, 0f, 4f, 4f),
                    Generators.Rect(0f, -1.45f, 1f, 1f), 0.001f), true);

            yield return Expect("ContainsWithMargin: trou debordant",
                PolygonTriangulator.ContainsWithMargin(Generators.Rect(0f, 0f, 4f, 4f),
                    Generators.Rect(1.8f, 0f, 1f, 1f), 0.001f), false);

            // Une epaisseur nulle ou negative n'est pas un panneau : PanelMeshBuilder doit
            // renoncer plutot que de produire une surface repliee sur elle-meme.
            yield return Expect("PanelMeshBuilder: epaisseur nulle refusee",
                PanelMeshBuilder.Build(Generators.Rect(0f, 0f, 4f, 3f), 0f, 0, 1, 2, null) == null, true);

            // Une epaisseur negative est ramenee a sa valeur absolue, deliberement : l'epaisseur
            // est stockee comme une composante de Size, dont le signe ne veut rien dire. Le test
            // fige ce choix au lieu de le supposer — c'est la difference entre une tolerance
            // assumee et une tolerance qu'on decouvre le jour ou elle fait des degats.
            yield return Expect("PanelMeshBuilder: epaisseur negative ramenee a sa valeur absolue",
                SameGeometry(PanelMeshBuilder.Build(Generators.Rect(0f, 0f, 4f, 3f), -0.2f, 0, 1, 2, null),
                             PanelMeshBuilder.Build(Generators.Rect(0f, 0f, 4f, 3f), 0.2f, 0, 1, 2, null)), true);

            yield return Expect("PanelMeshBuilder: epaisseur normale acceptee",
                PanelMeshBuilder.Build(Generators.Rect(0f, 0f, 4f, 3f), 0.2f, 0, 1, 2, null) != null, true);
        }

        private static bool SameGeometry(PanelMesh a, PanelMesh b)
        {
            if (a == null || b == null) return false;
            if (a.Vertices.Count != b.Vertices.Count) return false;
            for (int i = 0; i < a.Vertices.Count; i++)
            {
                if (!a.Vertices[i].Equals(b.Vertices[i])) return false;
            }
            return true;
        }

        /// <summary>Retourne un cas verticalement : les bornes hautes deviennent les bornes basses.</summary>
        private static Panel Mirrored(Panel panel)
        {
            var flipped = new Panel { Profile = panel.Profile, Seed = panel.Seed };
            foreach (var p in panel.Outer) flipped.Outer.Add(new Vector2(p.x, -p.y));
            flipped.Outer.Reverse();
            foreach (var hole in panel.Holes)
            {
                var loop = new List<Vector2>();
                foreach (var p in hole) loop.Add(new Vector2(p.x, -p.y));
                loop.Reverse();
                flipped.Holes.Add(loop);
            }
            return flipped;
        }

        private static Failure Expect(string name, bool got, bool want)
        {
            return got == want ? null : new Failure(name, "attendu " + want + ", obtenu " + got);
        }
    }
}
