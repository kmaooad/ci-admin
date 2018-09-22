using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.IO;

namespace KmaOoad18.CI.Admin
{
    public class CiBuildManager
    {
        const String vsts_collectionUri = "https://dev.azure.com/ashabinskiy";
        const String vsts_projectName = "KmaOoad18";
        const String vsts_pat = "7w23glh34svsocbhgvksefgwiyxtvucrfaqv6vt5y5uypnrkbp7a";
        const string vsts_projectId = "84882b39-af14-4d97-9f21-3baa86fb7ac6";
        const string vsts_githubConnection = "0224b15d-51eb-47ac-932e-9402ad67df1b";
        const string github_pat = "ff6e1f5e477ad0a0a6dfb0bf925dc0d44887fbb5";
        private VssConnection _connection;
        private BuildHttpClient _buildClient;
        private ConnectedServiceHttpClient _serviceClient;
        private ProjectHttpClient _projectClient;

        private CiBuildManager()
        {
        }

        public static CiBuildManager Create()
        {
            var mgr = new CiBuildManager();

            mgr._connection = new VssConnection(new Uri(vsts_collectionUri), new VssBasicCredential(string.Empty, vsts_pat));

            mgr._buildClient = mgr._connection.GetClient<BuildHttpClient>();
            mgr._serviceClient = mgr._connection.GetClient<ConnectedServiceHttpClient>();
            mgr._projectClient = mgr._connection.GetClient<ProjectHttpClient>();

            return mgr;
        }

        public async Task<List<Repository>> GetRepos()
        {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("kmaooad18-ci"));
            var tokenAuth = new Credentials(github_pat);
            client.Credentials = tokenAuth;          

            const string repoNamePattern = @"^(assignment-w\d-.+)$";

            var repos = (await client.Repository.GetAllForOrg("kmaooad18"))
            .Where(r => Regex.IsMatch(r.Name, repoNamePattern)).ToList();
            
            return repos;
        }

        public async Task<string> GetProjectId() =>
            (await _projectClient.GetProjects()).FirstOrDefault(p => p.Name == vsts_projectName)?.Id.ToString();

        public async Task<BuildDefinitionReference> GetBuildDefinitionReference(string name)
        => (await _buildClient.GetDefinitionsAsync(project: vsts_projectName)).FirstOrDefault(d => d.Name == name);

        public async Task UploadFile(long repoId, string filePath, string content)
        {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("kmaooad18-ci"));
            var tokenAuth = new Credentials(github_pat);
            client.Credentials = tokenAuth;

            var file = (await client.Repository.Content.GetAllContents(repoId)).FirstOrDefault(f => f.Path.ToLower() == filePath.ToLower());

            if (file != null)
                await client.Repository.Content.UpdateFile(repoId, filePath, new UpdateFileRequest($"Update {file.Name}", content, file.Sha, true));
            else
                await client.Repository.Content.CreateFile(repoId, filePath, new CreateFileRequest($"Create {filePath}", content, true));
        }

        public async Task DeleteDefinitions()
        {
            var defs = await _buildClient.GetDefinitionsAsync(project: vsts_projectName);
            foreach(var d in defs)
            {
                await _buildClient.DeleteDefinitionAsync(new Guid (vsts_projectId), d.Id);
            }
        }

        public async Task SetBuildTrigger()
        {
            var defs = await _buildClient.GetDefinitionsAsync(project: vsts_projectName);
            foreach(var d in defs)
            {
                var buildDef = await _buildClient.GetDefinitionAsync(d.Id);
                buildDef.Triggers.Add(new ScheduleTrigger() { Schedules = new List<Schedule>() { new Schedule() { DaysToBuild = ScheduleDays.All, StartHours = 1, StartMinutes = 0 } } });

                await _buildClient.UpdateDefinitionAsync(buildDef);
            }
        }

        public async Task<string> GenerateBuildSummary()
        {
            var students = new List<string>();

            using (var f = File.OpenText(@"C:\Projects\kmaooad18-ci\roster.txt"))
            {
                string l;
                while ((l = f.ReadLine()) != null)
                {
                    students.Add(l);
                }
            }

            var weeks = new List<string>();
            for (int i = 2; i < 12; i++)
            {
                weeks.Add($"w{i}");
            }

            var summary = new StringBuilder();

            summary.AppendLine();
            summary.Append("| Student |");


            foreach (var w in weeks) {
                summary.Append($" {w} |");
            }
            summary.AppendLine();

            summary.Append("| --- |");

            foreach (var w in weeks)
            {
                summary.Append($" --- |");
            }

            summary.AppendLine();

            var defs = await _buildClient.GetDefinitionsAsync(project: vsts_projectName);

            foreach (var s in students)
            {
                var sl = new StringBuilder($"| {s} |");
                foreach (var w in weeks)
                {
                    var def = defs.FirstOrDefault(d => d.Name == $"assignment-{w}-{s}-ci");
                    if (def == null)
                        sl.Append(" N/A |");
                    else
                    {
                        var url = $"https://dev.azure.com/ashabinskiy/KmaOoad18/_build/latest?definitionId={def.Id}";

                        sl.Append($" [![Build Status](https://dev.azure.com/ashabinskiy/KmaOoad18/_apis/build/status/assignment-{w}-{s}-ci)]({url}) |");
                    }
                }

                summary.AppendLine(sl.ToString());
            }

            return summary.ToString();
        }

        public async Task<bool> CreateDefinition(string repoName, Uri cloneUrl)
        {            
            var buildDefName = $"{repoName}-ci";

            var existing = await GetBuildDefinitionReference(buildDefName);

            if (existing != null)
                return false;
            
            var buildDef = new BuildDefinition();
            buildDef.Name = buildDefName;

            var yaml = new YamlProcess();
            yaml.YamlFilename = "/azure-pipelines.yml";
            buildDef.Process = yaml;

            var queue = new AgentPoolQueue();
            queue.Id = 34;
            
            buildDef.Queue = queue;

            var repoNameParts = repoName.Split('-');

            buildDef.Variables.Add("ParentRepository", new BuildDefinitionVariable() { Value = $"{repoNameParts[0]}-{repoNameParts[1]}" });

            var trigger = new ContinuousIntegrationTrigger();
            trigger.BranchFilters.Add("+master");
            buildDef.Triggers.Add(trigger);

            var repo = new BuildRepository();
     
            repo.Type = RepositoryTypes.GitHub;
            repo.Url = cloneUrl;
            repo.DefaultBranch = "master";
            repo.Name = $"kmaooad18/{repoName}";
            repo.Id = $"kmaooad18/{repoName}";
            repo.Properties["connectedServiceId"] = vsts_githubConnection;

            buildDef.Repository = repo;

            buildDef = await _buildClient.CreateDefinitionAsync(buildDef, project: vsts_projectName);

            return true;
        }
        
    }
}