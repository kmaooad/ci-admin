using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KmaOoad18.CI.Admin
{
    class Program
    {
        static void Main(string[] args)
        {
        }
               

        static void GenerateBuildMd()
        {
            var a = CiBuildManager.Create();
            var str = a.GenerateBuildSummary().GetAwaiter().GetResult();
            using (var f = File.CreateText("builds.md"))
            {
                f.Write(str);
                f.Flush();
                f.Close();
            }
        }

        static void CreateDefs()
        {
            var a = CiBuildManager.Create();

            a.DeleteDefinitions().GetAwaiter().GetResult();

            var repos = (a.GetRepos()).GetAwaiter().GetResult().OrderBy(r => r.Name).ToList();
            Console.WriteLine($"Found {repos.Count} repos");
            foreach (var r in repos)
            {
                try
                {
                    Console.Write($"Creating CI build for {r.Name}....");

                    var result = a.CreateDefinition(r.Name, new Uri(r.CloneUrl)).GetAwaiter().GetResult();

                    Console.WriteLine(result ? "CREATED" : "SKIPPED");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                }
            }
        }
    }
}
