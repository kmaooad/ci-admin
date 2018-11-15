using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KmaOoad18.CI.Admin
{
    public class CiBuildManager
    {
        readonly String vsts_projectName;
        readonly string vsts_projectId;
        readonly string vsts_githubConnection;
        readonly string github_pat;

        private readonly ILogger log;
        private VssConnection _connection;
        private BuildHttpClient _buildClient;
        private ConnectedServiceHttpClient _serviceClient;
        private ProjectHttpClient _projectClient;

        private CiBuildManager(ILogger log, Config config)
        {
            this.log = log;

            vsts_githubConnection = config.VstsGitHubConnection;
            vsts_projectName = config.VstsProjectName;
            vsts_projectId = config.VstsProjectId;
            github_pat = config.GitHubPat;
        }

        public static CiBuildManager Create(ILogger log, Config config)
        {
            var mgr = new CiBuildManager(log, config);

            mgr._connection = new VssConnection(new Uri(config.VstsCollectionUri), new VssBasicCredential(string.Empty, config.VstsPat));

            mgr._buildClient = mgr._connection.GetClient<BuildHttpClient>();
            mgr._serviceClient = mgr._connection.GetClient<ConnectedServiceHttpClient>();
            mgr._projectClient = mgr._connection.GetClient<ProjectHttpClient>();

            return mgr;
        }

        public async Task<int> CreateDefs()
        {
            var repos = (await GetRepos()).OrderBy(r => r.Name).ToList();

            log.LogInformation($"Found {repos.Count} repos");

            var buildDefs = await GetBuildDefs();

            log.LogInformation($"{buildDefs.Count} build defs already exist");

            int created = 0;

            foreach (var r in repos)
            {
                try
                {
                    if (!buildDefs.Contains(CiBuildManager.BuildDefName(r.Name)))
                    {
                        log.LogInformation($"Creating CI build for {r.Name}....");

                        var result = await CreateDefinition(r.Name, new Uri(r.CloneUrl));

                        created++;

                        log.LogInformation(result ? "CREATED" : "SKIPPED");
                    }

                }
                catch (Exception ex)
                {
                    log.LogInformation($"ERROR: {ex.Message}");
                }
            }

            return created;
        }

        public async Task<List<Repository>> GetRepos()
        {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("kmaooad18-ci"));
            var tokenAuth = new Credentials(github_pat);
            client.Credentials = tokenAuth;

            const string repoNamePattern = @"^(assignment-w(\d){1,2}-.+)$";

            var repos = (await client.Repository.GetAllForOrg("kmaooad18"))
            .Where(r => Regex.IsMatch(r.Name, repoNamePattern)).ToList();

            return repos;
        }

        public async Task<string> GetProjectId() =>
            (await _projectClient.GetProjects()).FirstOrDefault(p => p.Name == vsts_projectName)?.Id.ToString();

        public async Task<BuildDefinitionReference> GetBuildDefinitionReference(string name)
        => (await _buildClient.GetDefinitionsAsync(project: vsts_projectName)).FirstOrDefault(d => d.Name == name);

        public async Task UploadFile(string repo, string filePath, string content)
        {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("kmaooad18-ci"));

            var repoId = (await client.Repository.GetAllForOrg("kmaooad18")).FirstOrDefault(r => r.Name.ToLower() == repo.ToLower())?.Id;

            if (!repoId.HasValue)
                return;

            var tokenAuth = new Credentials(github_pat);
            client.Credentials = tokenAuth;

            var file = (await client.Repository.Content.GetAllContents(repoId.Value)).FirstOrDefault(f => f.Path.ToLower() == filePath.ToLower());

            if (file != null)
                await client.Repository.Content.UpdateFile(repoId.Value, filePath, new UpdateFileRequest($"Update {file.Name}", content, file.Sha, true));
            else
                await client.Repository.Content.CreateFile(repoId.Value, filePath, new CreateFileRequest($"Create {filePath}", content, true));
        }

        public async Task<List<string>> GetBuildDefs()
        => (await _buildClient.GetDefinitionsAsync(project: vsts_projectName)).Select(bd => bd.Name).ToList();



        public async Task DeleteDefinitions()
        {
            var defs = await _buildClient.GetDefinitionsAsync(project: vsts_projectName);
            foreach (var d in defs)
            {
                await _buildClient.DeleteDefinitionAsync(new Guid(vsts_projectId), d.Id);
            }
        }

        public async Task SetBuildTrigger()
        {
            var defs = await _buildClient.GetDefinitionsAsync(project: vsts_projectName);
            foreach (var d in defs)
            {
                var buildDef = await _buildClient.GetDefinitionAsync(d.Id);
                buildDef.Triggers.Add(new ScheduleTrigger() { Schedules = new List<Schedule>() { new Schedule() { DaysToBuild = ScheduleDays.All, StartHours = 1, StartMinutes = 0 } } });

                await _buildClient.UpdateDefinitionAsync(buildDef);
            }
        }

        public async Task<string> GenerateBuildSummary()
        {
            const string defRegex = @"^assignment-(?<week>w(\d){1,2})-(?<student>.+)-ci$";

            var defs = await _buildClient.GetDefinitionsAsync(project: vsts_projectName);

            var summaryData = new Dictionary<(string Week, string Student), string>();

            foreach (var def in defs)
            {
                if (Regex.IsMatch(def.Name, defRegex))
                {
                    var defSpecs = Regex.Matches(def.Name, defRegex)[0].Groups;

                    var url = $"https://dev.azure.com/ashabinskiy/KmaOoad18/_build/latest?definitionId={def.Id}";

                    var w = defSpecs["week"].Value;
                    var s = defSpecs["student"].Value;

                    var defBadge = $" [![Build Status](https://dev.azure.com/ashabinskiy/KmaOoad18/_apis/build/status/assignment-{w}-{s}-ci)]({url}) |";

                    summaryData.Add((w, s), defBadge);
                }
            }

            var summary = new StringBuilder();

            summary.AppendLine("# Builds").AppendLine().AppendLine();

            var notes = new[] {
               "- List of students is re-generated hourly",
               "- If your build shows as never built, make any new commit (or edit any file on GitHub) to trigger CI"
            };

            foreach (var n in notes)
                summary.AppendLine(n);

            summary.AppendLine().AppendLine().Append("| Student |");

            var weeks = summaryData.Keys.Select(k => k.Week).Distinct().OrderBy(w => Convert.ToInt32(w.Replace("w",""))).ToList();

            var students = summaryData.Keys.Select(k => k.Student).Distinct().OrderBy(s => s).ToList();

            foreach (var w in weeks)
            {
                summary.Append($" {w} |");
            }

            summary.AppendLine();

            summary.Append("| --- |");

            foreach (var w in weeks)
            {
                summary.Append($" --- |");
            }

            summary.AppendLine();


            foreach (var s in students)
            {
                var sl = new StringBuilder($"| {s} |");
                foreach (var w in weeks)
                {
                    if (summaryData.ContainsKey((w, s)))
                    {
                        sl.Append(summaryData[(w, s)]);
                    }
                    else
                    {
                        sl.Append(" N/A |");
                    }
                }

                summary.AppendLine(sl.ToString());
            }

            return summary.ToString();
        }

        public static string BuildDefName(string repoName) => $"{repoName}-ci";

        public async Task<bool> CreateDefinition(string repoName, Uri cloneUrl)
        {
            var buildDefName = BuildDefName(repoName);

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

        public class Config
        {
            public string VstsCollectionUri { get; set; }
            public string VstsGitHubConnection { get; set; }
            public string VstsProjectName { get; set; }
            public string VstsPat { get; set; }
            public string VstsProjectId { get; set; }
            public string GitHubPat { get; set; }
        }
    }
}