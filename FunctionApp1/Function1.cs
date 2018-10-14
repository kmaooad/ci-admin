using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

namespace KmaOoad18.CI.Admin
{
    public static class KmaOoadCi
    {
        [FunctionName("BuildsGenerator")]
        public static void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            var a = CiBuildManager.Create();

            a.CreateDefs().Wait();

            Console.WriteLine("Generating build summary...");
            var buildSummary = a.GenerateBuildSummary().GetAwaiter().GetResult();

            Console.WriteLine("Uploading build summary...");
            a.UploadFile("course-home", "builds.md", buildSummary).GetAwaiter().GetResult();
            Console.WriteLine("Done!");
            Console.ReadKey();
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
        
    }
}
