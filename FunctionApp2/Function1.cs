using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KmaOoad18.CI.Admin
{
    public static class KmaOoadCi
    {
        [FunctionName("BuildsGenerator")]
        public static async Task Run([TimerTrigger("0 */60 * * * *")]TimerInfo myTimer, ILogger log, ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var c = config.Get<CiBuildManager.Config>();

            var a = CiBuildManager.Create(log, c);

            await a.CreateDefs();

            log.LogInformation("Generating build summary...");
            
            var buildSummary = await a.GenerateBuildSummary();

            log.LogInformation("Uploading build summary...");
            a.UploadFile("course-home", "builds.md", buildSummary).GetAwaiter().GetResult();
            log.LogInformation("Done!");
        }

        

    }
}
