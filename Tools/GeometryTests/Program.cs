using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace LevelDesignTools.Tests
{
    /// <summary>
    /// Lanceur : cas figes, puis fuzz.
    ///
    /// Code de sortie 0 si tout passe, 1 sinon — utilisable tel quel dans un hook de commit
    /// ou une CI. Toute execution est reproductible : la graine est affichee, et un echec est
    /// reimprime en C# pret a coller dans Regressions.cs.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            int iterations = 5000;
            int seed = Environment.TickCount;
            bool verbose = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if ((a == "--iterations" || a == "-n") && i + 1 < args.Length)
                    iterations = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if ((a == "--seed" || a == "-s") && i + 1 < args.Length)
                    seed = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--verbose" || a == "-v") verbose = true;
                else if (a == "--help" || a == "-h") { Usage(); return 0; }
                else { Console.Error.WriteLine("Argument inconnu : " + a); Usage(); return 2; }
            }

            Console.WriteLine("Tests de proprietes — triangulation 2D et assemblage 3D");
            Console.WriteLine("graine {0}, {1} tirages", seed, iterations);
            Console.WriteLine();

            var watch = Stopwatch.StartNew();
            int failed = 0;

            failed += RunContracts(verbose);
            failed += RunRegressions(verbose);
            failed += RunFuzz(seed, iterations, verbose);

            watch.Stop();
            Console.WriteLine();
            if (failed == 0)
            {
                Console.WriteLine("OK — tout passe ({0:F1} s)", watch.Elapsed.TotalSeconds);
                return 0;
            }

            Console.WriteLine("ECHEC — {0} cas en defaut ({1:F1} s)", failed, watch.Elapsed.TotalSeconds);
            Console.WriteLine("Rejouer a l'identique : run.bat --seed {0} --iterations {1}", seed, iterations);
            return 1;
        }

        private static void Usage()
        {
            Console.WriteLine("Usage : GeometryTests [--iterations N] [--seed S] [--verbose]");
            Console.WriteLine("  --iterations N  nombre de panneaux tires au hasard (defaut 5000)");
            Console.WriteLine("  --seed S        graine, pour rejouer une execution a l'identique");
            Console.WriteLine("  --verbose       liste chaque cas");
        }

        private static int RunContracts(bool verbose)
        {
            int failed = 0;
            int total = 0;
            foreach (var failure in Regressions.CheckContractHelpers())
            {
                total++;
                if (failure == null) continue;
                failed++;
                Console.WriteLine("  ECHEC  " + failure);
            }
            Console.WriteLine("Garde-fous de la decoupe : {0}/{1}", total - failed, total);
            return failed;
        }

        private static int RunRegressions(bool verbose)
        {
            int failed = 0;
            int total = 0;

            foreach (var panel in Regressions.All())
            {
                total++;
                var failures = Invariants.Check(panel);
                failures.AddRange(MeshInvariants.Check(panel));
                if (failures.Count == 0)
                {
                    if (verbose) Console.WriteLine("  ok     " + panel.Profile);
                    continue;
                }
                failed++;
                Console.WriteLine("  ECHEC  " + panel.Profile);
                foreach (var f in failures) Console.WriteLine("           " + f);
            }

            Console.WriteLine("Cas figes : {0}/{1}", total - failed, total);
            return failed;
        }

        private const int ScaleCheckEvery = 8;

        private static int RunFuzz(int seed, int iterations, bool verbose)
        {
            var rng = new Random(seed);
            var byProperty = new Dictionary<string, int>();
            int failed = 0;
            int reported = 0;
            int holeTotal = 0;
            int maxHoles = 0;
            int scaleChecked = 0;

            for (int i = 0; i < iterations; i++)
            {
                Panel panel = Generators.Random(rng, seed + i);
                holeTotal += panel.Holes.Count;
                if (panel.Holes.Count > maxHoles) maxHoles = panel.Holes.Count;

                // L'independance a l'echelle rejoue toute la batterie sur deux variantes : la
                // verifier partout triplerait la duree du fuzz sans rien apprendre de plus, c'est
                // une propriete de l'algorithme et pas d'un panneau donne.
                bool checkScale = (i % ScaleCheckEvery) == 0;
                if (checkScale) scaleChecked++;

                var failures = Invariants.Check(panel, checkScale);
                failures.AddRange(MeshInvariants.Check(panel));
                if (failures.Count == 0)
                {
                    if (verbose) Console.WriteLine("  ok     " + panel.Name);
                    continue;
                }

                failed++;
                foreach (var f in failures)
                {
                    int count;
                    byProperty.TryGetValue(f.Property, out count);
                    byProperty[f.Property] = count + 1;
                }

                // On imprime les premiers en entier : au-dela, le detail noie le signal.
                if (reported < 3)
                {
                    reported++;
                    Console.WriteLine();
                    Console.WriteLine("  ECHEC  " + panel.Name);
                    foreach (var f in failures) Console.WriteLine("           " + f);
                    Console.WriteLine("         a coller dans Regressions.cs :");
                    Console.WriteLine(panel.ToCSharp());
                }
            }

            Console.WriteLine("Fuzz : {0}/{1} panneaux ({2:F1} trous en moyenne, {3} au maximum)",
                iterations - failed, iterations, iterations == 0 ? 0f : (float)holeTotal / iterations, maxHoles);

            Console.WriteLine("    P8 (echelle x8 / x0.25) verifiee sur {0} panneau(x)", scaleChecked);

            if (MeshInvariants.PinchedPanels > 0)
            {
                Console.WriteLine("    M3 ecartee sur {0} panneau(x) : la face s'y pince en un point, " +
                                  "le maillage reste etanche mais n'est plus une variete",
                    MeshInvariants.PinchedPanels);
            }

            if (Invariants.SkippedTJunctionChecks > 0)
            {
                Console.WriteLine("    P4 ecartee sur {0} panneau(x) : sommets distants de moins de {1:E0}, " +
                                  "en dessous de la tolerance du triangulateur",
                    Invariants.SkippedTJunctionChecks, Invariants.TJunctionMinSeparation);
            }

            foreach (var pair in byProperty)
            {
                Console.WriteLine("    {0} violee {1} fois", pair.Key, pair.Value);
            }

            return failed;
        }
    }
}
