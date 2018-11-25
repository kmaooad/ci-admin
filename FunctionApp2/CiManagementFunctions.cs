using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace KmaOoad18.CI.Admin
{
    public static class CiManagementFunctions
    {
        [FunctionName("BuildsGenerator")]
        public static async Task BuildsGenerator([TimerTrigger("0 */60 * * * *")]TimerInfo myTimer, ILogger log, ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var c = config.Get<CiBuildManager.Config>();

            var a = CiBuildManager.Create(log, c);

            var created = await a.CreateDefs();

            if (created > 0)
            {
                await UpdateBuildSummary(log, a);
            }
            else
            {
                log.LogInformation("Nothing to do here!");
            }
        }

        private static async Task UpdateBuildSummary(ILogger log, CiBuildManager a)
        {
            log.LogInformation("Generating build summary...");

            var buildSummary = await a.GenerateBuildSummary();

            log.LogInformation("Uploading build summary...");

            a.UploadFile("kmaooad18.github.io", "builds.md", buildSummary).GetAwaiter().GetResult();

            log.LogInformation("Done!");
        }

        [FunctionName("BuildsSummary"), NoAutomaticTrigger]
        public static async Task BuildsSummary(string input, ILogger log, ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var c = config.Get<CiBuildManager.Config>();

            var a = CiBuildManager.Create(log, c);

            await UpdateBuildSummary(log, a);
        }

    }
}
