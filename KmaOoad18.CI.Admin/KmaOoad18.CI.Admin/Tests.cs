using System;
using Xunit;
using KmaOoad18.CI.Admin;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Xunit.Abstractions;

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
            var repos = (await a.GetRepos()).OrderBy(r => r.Name);

            foreach (var r in repos)
                Console.WriteLine(r.CloneUrl);
        }

        [Fact]
        public async Task GetBuildDefinitionReference()
        {
            var a = CiBuildManager.Create();
            var def = await a.GetBuildDefinitionReference("KmaOoad18-CI");
        }

        [Fact]
        public async Task GetBuildDefinition()
        {
            var a = CiBuildManager.Create();
            var def = await a.GetBuildDefinition(13);
        }

        [Fact]
        public async Task ConnectGit()
        {
            var a = CiBuildManager.Create();
            var id = await a.ConnectGit("demo-assignment-kmaooad18-st-b", new Uri("https://github.com/kmaooad18/demo-assignment-kmaooad18-st-b.git"));
            _output.WriteLine(id.ToString());
        }

        [Fact]
        public async Task CreateDefinition()
        {
            var m = CiBuildManager.Create();
            await m.CreateDefinition("demo-assignment-kmaooad18-st-b", new Uri("https://github.com/kmaooad18/demo-assignment-kmaooad18-st-b.git"));
        }
    }
}
