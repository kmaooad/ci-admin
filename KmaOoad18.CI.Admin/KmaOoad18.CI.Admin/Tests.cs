using System;
using Xunit;
using KmaOoad18.CI.Admin;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Xunit.Abstractions;
using System.IO;

namespace KmaOoad18.CI.Admin
{
    public class Tests
    {

        private readonly ITestOutputHelper _output;

        public Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task GetRepos()
        {
            var a = CiBuildManager.Create();
            var repos = (await a.GetRepos()).OrderBy(r => r.Name).ToList();
            _output.WriteLine($"Found {repos.Count} repos");
            foreach (var r in repos)
                _output.WriteLine(r.CloneUrl);
        }

        [Fact]
        public async Task GetBuildDefinitionReference()
        {
            var a = CiBuildManager.Create();
            var def = await a.GetBuildDefinitionReference("KmaOoad18-CI");
        }
        
        [Fact]
        public async Task UploadFile()
        {
            var a = CiBuildManager.Create();
            var repo = (await a.GetRepos()).FirstOrDefault(r => r.Name== "assignment-w2-kmaooad18-st-a");

            using (var file = File.OpenText(@"C:\Projects\kmaooad18-ci\azure-pipelines.yml")) {
                await a.UploadFile(repo.Id, "azure-pipelines.yml", await file.ReadToEndAsync());
            }
        }

        [Fact]
        public async Task CreateDefinition()
        {
            var m = CiBuildManager.Create();
            await m.CreateDefinition("assignment-w2-kmaooad18-st-a", new Uri("https://github.com/kmaooad18/assignment-w2-kmaooad18-st-a.git"));
        }
    }
}
