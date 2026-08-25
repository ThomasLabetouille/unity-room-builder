using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelDesignTools.Tests
{
    /// <summary>
    /// Fabrique des panneaux troues au hasard.
    ///
    /// Les trous sont places dans les cases d'une grille, avec une marge : ils sont disjoints PAR
    /// CONSTRUCTION, ce que la decoupe garantit de son cote en refusant un contour qui chevauche
    /// un trou existant. Un generateur qui produirait des trous secants testerait un cas que
    /// l'outil n'atteint jamais, et signalerait de faux bugs.
    ///
    /// Les profils "alignes" existent parce que c'est exactement la configuration qui mettait en
    /// echec l'ancienne triangulation : des trous partageant des coordonnees a l'identique.
    /// </summary>
    public static class Generators
    {
        private static readonly string[] Profiles =
        {
            "rectangles", "rectangles", "triangles", "convexes", "concaves",
            "alignes en Y", "alignes en X", "fins", "colles au bord", "nombreux", "melange",
        };

        public static Panel Random(Random rng, int seed)
        {
            string profile = Profiles[rng.Next(Profiles.Length)];

            float width = (float)(2.0 + rng.NextDouble() * 12.0);
            float height = (float)(1.5 + rng.NextDouble() * 4.0);

            // Un panneau sur quatre recoit un contour exterieur libre plutot qu'un rectangle :
            // c'est la forme des sols et plafonds dessines a main levee, que la decoupe accepte
            // depuis qu'un panneau n'est plus force d'etre rectangulaire.
            List<Vector2> outer = rng.Next(4) == 0
                ? FreeContour(rng, width, height)
                : Rect(0f, 0f, width, height);

            var panel = new Panel
            {
                Profile = profile,
                Seed = seed,
                Outer = outer,
            };

            int cols, rows;
            switch (profile)
            {
                case "alignes en Y": cols = rng.Next(2, 7); rows = 1; break;
                case "alignes en X": cols = 1; rows = rng.Next(2, 5); break;
                case "nombreux": cols = rng.Next(4, 8); rows = rng.Next(2, 4); break;
                default: cols = rng.Next(1, 5); rows = rng.Next(1, 4); break;
            }

            float cellW = width / cols;
            float cellH = height / rows;

            var cells = new List<int>();
            for (int i = 0; i < cols * rows; i++) cells.Add(i);
            Shuffle(cells, rng);

            int wanted = profile == "nombreux" ? cells.Count : rng.Next(1, Math.Min(cells.Count, 5) + 1);

            for (int i = 0; i < wanted && i < cells.Count; i++)
            {
                int cx = cells[i] % cols;
                int cy = cells[i] / cols;

                float left = -width * 0.5f + cx * cellW;
                float bottom = -height * 0.5f + cy * cellH;

                // Marge : garde le trou strictement a l'interieur du panneau et separe des voisins.
                float margin = Math.Min(cellW, cellH) * 0.12f;
                float boxW = cellW - 2f * margin;
                float boxH = cellH - 2f * margin;
                if (boxW <= 1e-3f || boxH <= 1e-3f) continue;

                float w = boxW, h = boxH;
                float ox = 0f, oy = 0f;

                if (profile == "alignes en Y")
                {
                    // Meme hauteur pour tous : coordonnees Y partagees a l'identique.
                    h = boxH * 0.6f;
                }
                else if (profile == "alignes en X")
                {
                    w = boxW * 0.6f;
                }
                else if (profile == "fins")
                {
                    if (rng.Next(2) == 0) w = boxW * (float)(0.02 + rng.NextDouble() * 0.06);
                    else h = boxH * (float)(0.02 + rng.NextDouble() * 0.06);
                    ox = (float)((rng.NextDouble() - 0.5) * (boxW - w));
                    oy = (float)((rng.NextDouble() - 0.5) * (boxH - h));
                }
                else if (profile == "colles au bord")
                {
                    w = boxW * 0.98f;
                    h = boxH * 0.98f;
                }
                else
                {
                    w = boxW * (float)(0.3 + rng.NextDouble() * 0.7);
                    h = boxH * (float)(0.3 + rng.NextDouble() * 0.7);
                    ox = (float)((rng.NextDouble() - 0.5) * (boxW - w));
                    oy = (float)((rng.NextDouble() - 0.5) * (boxH - h));
                }

                float centerX = left + margin + boxW * 0.5f + ox;
                float centerY = bottom + margin + boxH * 0.5f + oy;

                string shape = profile;
                if (profile == "melange" || profile == "nombreux" || profile == "colles au bord")
                {
                    string[] kinds = { "rectangles", "triangles", "convexes", "concaves" };
                    shape = kinds[rng.Next(kinds.Length)];
                }

                List<Vector2> hole = Shape(shape, centerX, centerY, w, h, rng);

                // Sur un contour libre, une case de la grille peut deborder : la decoupe exige un
                // trou STRICTEMENT interieur, on ecarte donc les candidats qui n'y sont pas. Un
                // generateur qui produirait des cas que l'outil refuse testerait un chemin mort.
                if (PolygonTriangulator.ContainsWithMargin(panel.Outer, hole, 1e-3f))
                {
                    panel.Holes.Add(hole);
                }
            }

            // Un panneau sans trou reste un cas valide : c'est l'etat d'un mur neuf.
            return panel;
        }

        private static List<Vector2> Shape(string kind, float cx, float cy, float w, float h, Random rng)
        {
            switch (kind)
            {
                case "triangles":
                    return new List<Vector2>
                    {
                        new Vector2(cx - w * 0.5f, cy - h * 0.5f),
                        new Vector2(cx + w * 0.5f, cy - h * 0.5f),
                        new Vector2(cx + (float)((rng.NextDouble() - 0.5) * w), cy + h * 0.5f),
                    };

                case "convexes":
                {
                    int n = rng.Next(5, 9);
                    var loop = new List<Vector2>(n);
                    double start = rng.NextDouble() * Math.PI * 2.0;
                    for (int i = 0; i < n; i++)
                    {
                        double angle = start + i * (Math.PI * 2.0 / n);
                        loop.Add(new Vector2(
                            cx + (float)(Math.Cos(angle) * w * 0.5),
                            cy + (float)(Math.Sin(angle) * h * 0.5)));
                    }
                    return loop;
                }

                case "concaves":
                {
                    // Etoile : un sommet sur deux rentre vers le centre -> sommets reflex.
                    int branches = rng.Next(3, 6);
                    var loop = new List<Vector2>(branches * 2);
                    double start = rng.NextDouble() * Math.PI * 2.0;
                    for (int i = 0; i < branches * 2; i++)
                    {
                        double angle = start + i * (Math.PI / branches);
                        double r = (i % 2 == 0) ? 0.5 : 0.22;
                        loop.Add(new Vector2(
                            cx + (float)(Math.Cos(angle) * w * r),
                            cy + (float)(Math.Sin(angle) * h * r)));
                    }
                    return loop;
                }

                default:
                    return Rect(cx, cy, w, h);
            }
        }

        /// <summary>
        /// Contour exterieur non rectangulaire, inscrit dans la boite width x height : polygone
        /// convexe, ou forme en L. De quoi exercer le cas des sols dessines a main levee.
        /// </summary>
        private static List<Vector2> FreeContour(Random rng, float width, float height)
        {
            float hw = width * 0.5f, hh = height * 0.5f;

            if (rng.Next(2) == 0)
            {
                int n = rng.Next(5, 10);
                var loop = new List<Vector2>(n);
                double start = rng.NextDouble() * Math.PI * 2.0;
                for (int i = 0; i < n; i++)
                {
                    double angle = start + i * (Math.PI * 2.0 / n);
                    loop.Add(new Vector2(
                        (float)(Math.Cos(angle) * hw),
                        (float)(Math.Sin(angle) * hh)));
                }
                return loop;
            }

            float cutX = (float)(hw * (0.2 + rng.NextDouble() * 0.5));
            float cutY = (float)(hh * (0.2 + rng.NextDouble() * 0.5));
            return new List<Vector2>
            {
                new Vector2(-hw, -hh),
                new Vector2(hw, -hh),
                new Vector2(hw, cutY),
                new Vector2(cutX, cutY),
                new Vector2(cutX, hh),
                new Vector2(-hw, hh),
            };
        }

        public static List<Vector2> Rect(float cx, float cy, float w, float h)
        {
            return new List<Vector2>
            {
                new Vector2(cx - w * 0.5f, cy - h * 0.5f),
                new Vector2(cx + w * 0.5f, cy - h * 0.5f),
                new Vector2(cx + w * 0.5f, cy + h * 0.5f),
                new Vector2(cx - w * 0.5f, cy + h * 0.5f),
            };
        }

        private static void Shuffle(List<int> items, Random rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }
    }
}
