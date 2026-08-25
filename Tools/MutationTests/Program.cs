using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LevelDesignTools.Mutations
{
    /// <summary>
    /// Teste les tests.
    ///
    /// Un banc de tests qui passe au vert ne dit rien tant qu'on ne l'a pas vu echouer. On injecte
    /// donc, un par un, des defauts plausibles dans le code de production, et on regarde si la
    /// batterie les rattrape. Un defaut rattrape est un « mutant tue » ; un defaut qui passe
    /// inapercu est un « survivant », et chaque survivant est un angle mort documente.
    ///
    /// Le taux de mutants tues est la seule mesure honnete de ce que valent des tests. Le nombre
    /// de tests, le taux de couverture, le nombre de bugs deja trouves : rien de tout cela ne dit
    /// ce qui serait attrape la prochaine fois.
    ///
    /// Le programme modifie les fichiers source le temps de chaque essai et les restaure ensuite,
    /// y compris si on l'interrompt. Ne pas le lancer sur un arbre de travail non commite.
    /// </summary>
    public static class Program
    {
        private const string DefaultCommand =
            "dotnet run -c Release --project ../GeometryTests/GeometryTests.csproj -- --iterations 600 --seed 20260825";

        // Un defaut peut n'apparaitre que sur une configuration rare : il survit alors a la passe
        // courte sans etre pour autant hors de portee. Avant de declarer un survivant, on lui
        // laisse donc une seconde chance sur une passe bien plus longue. Sans ce rattrapage,
        // l'outil designerait comme angles morts des defauts que la batterie sait attraper — et
        // on irait ecrire des tests pour rien.
        private const string DefaultDeepCommand =
            "dotnet run -c Release --project ../GeometryTests/GeometryTests.csproj -- --iterations 8000 --seed 424242";

        private static readonly Dictionary<string, string> Originals = new Dictionary<string, string>();

        public static int Main(string[] args)
        {
            string command = DefaultCommand;
            string deepCommand = DefaultDeepCommand;
            bool verbose = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--command" && i + 1 < args.Length) command = args[++i];
                else if (args[i] == "--deep-command" && i + 1 < args.Length) deepCommand = args[++i];
                else if (args[i] == "--verbose") verbose = true;
                else if (args[i] == "--help") { Usage(); return 0; }
            }

            List<Mutation> mutations = Catalogue.All();

            Console.WriteLine("Test des tests — injection de défauts");
            Console.WriteLine("{0} mutations, commande : {1}", mutations.Count, command);
            Console.WriteLine();

            // Sauvegarde en mémoire, restauration garantie même sur Ctrl+C.
            foreach (var mutation in mutations)
            {
                if (!Originals.ContainsKey(mutation.File)) Originals[mutation.File] = File.ReadAllText(mutation.File);
            }
            Console.CancelKeyPress += (s, e) => RestoreAll();

            // Contrôle préalable : la batterie doit être verte AVANT toute mutation. Sans quoi
            // tous les mutants seraient déclarés tués, pour une raison qui n'a rien à voir.
            Console.Write("Contrôle à blanc (aucune mutation)… ");
            int baseline = Run(command, verbose);
            if (baseline != 0)
            {
                Console.WriteLine("ÉCHEC");
                Console.WriteLine("La batterie ne passe pas sur le code intact : rien de ce qui suit n'aurait de sens.");
                RestoreAll();
                return 2;
            }
            Console.WriteLine("vert");
            Console.WriteLine();

            int killed = 0, survived = 0, invalid = 0, stale = 0, rareKills = 0;
            var survivors = new List<Mutation>();

            try
            {
                foreach (var mutation in mutations)
                {
                    string original = Originals[mutation.File];
                    int occurrences = Occurrences(original, mutation.Find);

                    if (occurrences != 1)
                    {
                        // Le catalogue a dérivé du code : le signaler, ne pas le compter.
                        Console.WriteLine("  PÉRIMÉE  {0,-42} ({1} occurrence(s) du motif)", mutation.Name, occurrences);
                        stale++;
                        continue;
                    }

                    File.WriteAllText(mutation.File, original.Replace(mutation.Find, mutation.Replace));
                    int exitCode = Run(command, verbose);
                    File.WriteAllText(mutation.File, original);

                    if (exitCode == 0)
                    {
                        // Seconde chance sur une passe longue avant de conclure.
                        File.WriteAllText(mutation.File, original.Replace(mutation.Find, mutation.Replace));
                        int deepExit = Run(deepCommand, verbose);
                        File.WriteAllText(mutation.File, original);

                        if (deepExit != 0 && deepExit < 100)
                        {
                            Console.WriteLine("  tué      {0,-42} (passe longue seulement)", mutation.Name);
                            killed++;
                            rareKills++;
                        }
                        else
                        {
                            Console.WriteLine("  SURVIT   {0,-42} {1}", mutation.Name, mutation.Note);
                            survived++;
                            survivors.Add(mutation);
                        }
                    }
                    else if (exitCode >= 100)
                    {
                        // Ne compile pas : mutation dégénérée, hors sujet pour la mesure.
                        Console.WriteLine("  INVALIDE {0,-42} (ne compile pas)", mutation.Name);
                        invalid++;
                    }
                    else
                    {
                        Console.WriteLine("  tué      {0}", mutation.Name);
                        killed++;
                    }
                }
            }
            finally
            {
                RestoreAll();
            }

            int scored = killed + survived;
            Console.WriteLine();
            Console.WriteLine("Mutants tués : {0}/{1}{2}", killed, scored,
                scored > 0 ? string.Format("  ({0:P0})", killed / (double)scored) : "");
            if (rareKills > 0)
            {
                Console.WriteLine("dont {0} rattrapé(s) seulement en passe longue — la configuration " +
                                  "qui les révèle est rare, pas hors de portée.", rareKills);
            }
            if (invalid > 0) Console.WriteLine("Mutations écartées (ne compilent pas) : {0}", invalid);
            if (stale > 0) Console.WriteLine("Mutations périmées (motif introuvable dans le code) : {0}", stale);

            if (survivors.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Angles morts — ces défauts passent, même en passe longue :");
                foreach (var s in survivors) Console.WriteLine("  · {0} : {1}", s.Name, s.Note);
                Console.WriteLine();
                Console.WriteLine("Deux lectures possibles pour chacun : soit la batterie a un trou et il faut");
                Console.WriteLine("l'écrire, soit la mutation ne change rien d'observable et le code qu'elle");
                Console.WriteLine("touche est redondant. Le second cas mérite autant d'attention que le premier.");
            }

            return survivors.Count == 0 && stale == 0 ? 0 : 1;
        }

        private static void Usage()
        {
            Console.WriteLine("Usage : MutationTests [--command \"<commande de test>\"] [--verbose]");
            Console.WriteLine("  La commande doit compiler ET lancer la batterie, et rendre 0 si elle passe.");
        }

        private static void RestoreAll()
        {
            foreach (var pair in Originals)
            {
                try { File.WriteAllText(pair.Key, pair.Value); } catch { }
            }
        }

        private static int Occurrences(string text, string pattern)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        private static int Run(string command, bool verbose)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            var info = new ProcessStartInfo
            {
                FileName = windows ? "cmd.exe" : "/bin/sh",
                Arguments = windows ? "/c " + command : "-c \"" + command.Replace("\"", "\\\"") + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using (var process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (verbose)
                {
                    Console.WriteLine(output);
                    if (error.Length > 0) Console.WriteLine(error);
                }

                // Une mutation qui ne compile pas n'est pas un défaut testable : on la distingue
                // d'un vrai échec de test par la signature du compilateur.
                if (output.Contains("error CS") || error.Contains("error CS")) return 100;
                return process.ExitCode;
            }
        }
    }
}
