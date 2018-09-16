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

namespace KmaOoad18.CI.Admin
{
    public class CiBuildManager
    {
        const String vsts_collectionUri = "https://dev.azure.com/ashabinskiy";
        const String vsts_projectName = "KmaOoad18";
        const String vsts_pat = "7w23glh34svsocbhgvksefgwiyxtvucrfaqv6vt5y5uypnrkbp7a";
        const string vsts_projectId = "84882b39-af14-4d97-9f21-3baa86fb7ac6";
        const string git_pat = "3bb73bfcbd50ccbe56a1aaf7c330bb392e9c2f25";
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
            var tokenAuth = new Credentials(git_pat);
            client.Credentials = tokenAuth;

            const string repoNamePattern = @"^((demo-assignment-.+)|(assignment-w\d-.+))$";

            var repos = (await client.Repository.GetAllForOrg("kmaooad18"))
            .Where(r => Regex.IsMatch(r.Name, repoNamePattern)).ToList();

            return repos;
        }

        public async Task<string> GetProjectId() =>
            (await _projectClient.GetProjects()).FirstOrDefault(p => p.Name == vsts_projectName)?.Id.ToString();

        public async Task<BuildDefinitionReference> GetBuildDefinitionReference(string name)
        => (await _buildClient.GetDefinitionsAsync(project: vsts_projectName)).FirstOrDefault(d => d.Name == name);

        public async Task<BuildDefinition> GetBuildDefinition(int id)
        => await _buildClient.GetDefinitionAsync(vsts_projectName, id);

        public async Task<WebApiConnectedServiceDetails> GetConnectedService(string name) =>
            await _serviceClient.GetConnectedServiceDetails(vsts_projectId, name);

        public async Task<IEnumerable<WebApiConnectedService>> GetConnectedServices() =>
            await _serviceClient.GetConnectedServices(vsts_projectId);

        public async Task CreateDefinition(string repoName, Uri cloneUrl)
        {
            var buildDefName = $"{repoName}-ci";

            var existing = await GetBuildDefinitionReference(buildDefName);

            if (existing != null)
                return;

            const int ciTemplateDefId = 11;

            var bd = await GetBuildDefinition(ciTemplateDefId);

            var buildDef = new BuildDefinition();
            buildDef.Name = buildDefName;
            buildDef.Queue = bd.Queue;
            buildDef.Process = bd.Process;
            buildDef.Triggers.AddRange(bd.Triggers);
            buildDef.BuildNumberFormat = bd.BuildNumberFormat;


            var gitConnectionId = await ConnectGit(repoName, cloneUrl);

            var repo = new BuildRepository();
            repo.Url = cloneUrl;
            repo.Type = "Git";
            repo.DefaultBranch = "master";
            repo.Properties["isPrivate"] = "true";
            repo.Properties["cloneUrl"] = cloneUrl.ToString();
            repo.Properties["connectedServiceId"] = gitConnectionId.ToString();


            buildDef.Repository = repo;

            buildDef = await _buildClient.CreateDefinitionAsync(buildDef, project: vsts_projectName);            
        }

        public async Task<Guid> ConnectGit(string repoName, Uri cloneUrl)
        {
            var post = new HttpRequestMessage(HttpMethod.Post, $"{vsts_collectionUri}/{vsts_projectId}/_apis/serviceendpoint/endpoints?api-version=5.0-preview.2");
            
            var body = new
            {
                name = repoName,
                type = "Git",
                authorization = new
                {
                    parameters = new
                    {
                        username = "ironpercival",
                        password = git_pat
                    },
                    scheme = "UsernamePassword"
                },
                url = cloneUrl
            };

            post.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", vsts_pat))));

                using (HttpResponseMessage response = await client.SendAsync(post))
                {
                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var connectionObj = new { Id = Guid.Empty };

                    var connection = JsonConvert.DeserializeAnonymousType(responseContent, connectionObj);

                    return connection.Id;
                }
            }

        }

        public void CreateDefinitions()
        {

        }
    }
}