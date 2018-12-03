using Octokit;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using System.Threading;
using System.IO;

namespace Final
{
    internal class StudentRecord
    {
        public string TimelyCommit { get; set; }
        public string LateCommit { get; set; }
        public string Student { get; set; }
        public int Week { get; set; }
        public int? LateBuildId { get; set; }
        public int? TimelyBuildId { get; set; }
        public bool? LateBuildResult { get; set; }
        public bool? TimelyBuildResult { get; set; }
    }

    class Program
    {
        private const string vstspat = "";
        private const string connectionString = "Server=.\\SQLEXPRESS;Database=KmaOoad18;Trusted_Connection=True;";
        private const string pat = "";


        static void Main(string[] args)
        {
            //Console.WriteLine("Checking submissions....");
            //SaveSubmissions().Wait();

            //Console.WriteLine(".....");
            //Console.WriteLine(".....");
            //Console.WriteLine(".....");

            //Console.WriteLine("Checking builds....");
            //CheckBuilds().Wait();

            //Console.WriteLine("Starting builds....");
            //QueueBuilds().Wait();

            //Console.WriteLine("Calculating finals....");
            //CalculateFinals().Wait();

            Console.WriteLine("Publishing finals....");
            PublishFinals().Wait();

            Console.ReadKey();
        }

        static async Task PublishFinals()
        {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("kmaooad18-ci"));
            var tokenAuth = new Credentials(pat);
            client.Credentials = tokenAuth;

            var files = new DirectoryInfo("D:/final").GetFiles();

            foreach (var f in files)
            {
                var student = f.Name.Replace(".txt", "");

                //var repo = new NewRepository($"final-{student}");
                //repo.AutoInit = true;
                //repo.Private = true;

                //var created = await client.Repository.Create("kmaooad18", repo);
                //Console.WriteLine($"Created repository {created.Name}");

                //await client.Repository.Collaborator.Add(created.Id, student);
                //Console.WriteLine($"Added {student} as collaborator");

                using (var reader = new StreamReader(f.OpenRead()))
                {
                    var content = await reader.ReadToEndAsync();
                    var repo = $"final-{student}";
                    await UploadFile(pat, repo, "totals.txt", content);
                    Console.WriteLine($"Totals uploaded to {repo}");
                }
            }
        }

        static async Task UploadFile(string githubPat, string repo, string filePath, string content)
        {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("kmaooad18-ci"));

            var tokenAuth = new Credentials(githubPat);
            client.Credentials = tokenAuth;

            var repoId = (await client.Repository.GetAllForOrg("kmaooad18")).FirstOrDefault(r => r.Name.ToLower() == repo.ToLower())?.Id;

            if (!repoId.HasValue)
                return;

            await client.Repository.Content.CreateFile(repoId.Value, filePath, new CreateFileRequest($"Create {filePath}", content, true));
        }

        static async Task CalculateFinals()
        {
            List<StudentRecord> studentRecords = await GetStudentRecords();

            var studentGroups = studentRecords.GroupBy(sr => sr.Student).ToList();

            foreach (var sg in studentGroups)
            {
                try
                {
                    var quiz = await GetQuizMark(sg.Key);

                    var finalcontent = new StringBuilder();

                    finalcontent.AppendLine("Base ....... 23");
                    finalcontent.AppendLine($"Quiz ....... {quiz}");

                    var totalAssignments = 0m;

                    foreach (var w in sg)
                    {
                        var mark = Math.Max(Convert.ToInt32(w.LateBuildResult) * 1.5m, Convert.ToInt32(w.TimelyBuildResult) * 3m);

                        finalcontent.AppendLine($"Week {w.Week} ...... {mark}");
                        totalAssignments += mark;
                    }
                    finalcontent.AppendLine("=====================");

                    var subtotal = Convert.ToInt32(Math.Ceiling(totalAssignments + 23 + quiz));

                    finalcontent.AppendLine($"Subtotal ..... {subtotal}");

                    var autoexam = Convert.ToInt32(Math.Ceiling(quiz / 10 * 20 + (totalAssignments >= 24 ? 27 : totalAssignments) / 27 * 20));


                    if (subtotal + autoexam == 80 || subtotal + autoexam == 90)
                        autoexam += 1;

                    var pretotal = subtotal + autoexam;

                    if (pretotal >= 81)
                    {
                        finalcontent.AppendLine($"Auto-exam ..... {autoexam}");
                        finalcontent.AppendLine($"Total ..... {pretotal}");
                    }

                    using (var connection = new SqlConnection(connectionString))
                    {
                        await connection.OpenAsync();
                        var qparams = new
                        {
                            st = sg.Key,
                            quiz,
                            assignments = totalAssignments,
                            autoexam,
                            total = pretotal
                        };

                        await connection.ExecuteAsync(@"INSERT INTO [dbo].[Totals]
           ([Student]
           ,[Quiz]
           ,[Assignments]
           ,[Autoexam]
           ,[Total])
     VALUES
           (@st
           ,@quiz
           ,@assignments
           ,@autoexam
           ,@total)", qparams);
                    }
                    Console.WriteLine($"Final results for {sg.Key} saved to DB");


                    using (StreamWriter outputFile = new StreamWriter($"D:/final/{sg.Key}.txt"))
                    {
                        await outputFile.WriteAsync(finalcontent.ToString());

                        Console.WriteLine($"Final results saved to {sg.Key}.txt");
                        Console.WriteLine();
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static async Task<decimal> GetQuizMark(string student)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var qparams = new { st = student };

                var quizmark = await connection.QuerySingleOrDefaultAsync<decimal>(@"SELECT Mark FROM Quiz WHERE Student = @st", qparams);

                return quizmark;
            }
        }

        static async Task QueueBuilds()
        {
            var httpclient = new HttpClient();
            var defs = await GetBuildDefs(httpclient);

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                var builds = (await connection.QueryAsync<(string student, int week, string commit)>(@"
                              SELECT student, week, LateCommit as sha
  FROM [KmaOoad18].[dbo].[Final]
  where LateCommit is not null and LateBuildResult is null
  union
  SELECT student, week, TimelyCommit as sha
  FROM [KmaOoad18].[dbo].[Final]
  where TimelyCommit is not null AND TimelyBuildResult is null"))
                              .ToList();

                Console.WriteLine($"{builds.Count} builds to start.....");


                foreach (var (student, week, commit) in builds)
                {
                    try
                    {
                        Console.WriteLine($"Build for {student}/{week}/{commit} starting.....");

                        var defId = defs.FirstOrDefault(d => d.name == $"assignment-w{week}-{student}-ci").id;

                        if (defId == 0)
                        {
                            Console.WriteLine("Wrong def ID, skipping.....");
                            continue;
                        }

                        var queueReq = new HttpRequestMessage(HttpMethod.Post, $"https://dev.azure.com/ashabinskiy/kmaooad18/_apis/build/builds?api-version=4.1");

                        queueReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{vstspat}")));

                        var queueReqBody = new
                        {
                            definition = new
                            {
                                id = defId
                            },
                            sourceVersion = commit
                        };

                        queueReq.Content = new StringContent(JsonConvert.SerializeObject(queueReqBody), Encoding.UTF8, "application/json");

                        var queueResponse = await httpclient.SendAsync(queueReq);

                        queueResponse.EnsureSuccessStatusCode();

                        Console.WriteLine($"Started build for {student}/{week}/{commit}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error starting build for {student}/{week}/{commit}: {ex.Message}");
                    }
                }
            }
        }

        static async Task CheckBuilds()
        {
            var httpclient = new HttpClient();

            List<StudentRecord> studentRecords = await GetStudentRecords();

            var defs = await GetBuildDefs(httpclient);

            var batches = studentRecords.Select((el, idx) => (el, idx % 30)).GroupBy(el => el.Item2).ToList();

            Console.WriteLine($"{studentRecords.Count} total, {batches.Count} batches....");

            foreach (var b in batches)
            {
                Console.WriteLine($"===============================================");

                Console.WriteLine($"Processing batch #{b.Key + 1}/{batches.Count} of {b.Count()} items...");

                var tasks = b.Select(r => CheckSubmissionBuilds(defs, r.el, httpclient, connectionString));

                await Task.WhenAll(tasks);
                Console.WriteLine("Done!");
                //Console.WriteLine("Pausing for 15s......");
                //Thread.Sleep(15000);
                Console.WriteLine($"===============================================");
                Console.WriteLine("Proceeding....");
            }
        }

        private static async Task<List<StudentRecord>> GetStudentRecords()
        {
            var studentRecords = new List<StudentRecord>();

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                studentRecords = (await connection.QueryAsync<StudentRecord>(@"SELECT * FROM Final")).ToList();
            }

            return studentRecords;
        }

        private static async Task<IEnumerable<(int id, string name)>> GetBuildDefs(HttpClient httpclient)
        {
            var defsRequest = new HttpRequestMessage(HttpMethod.Get, "https://dev.azure.com/ashabinskiy/kmaooad18/_apis/build/definitions?api-version=4.1");

            defsRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{vstspat}")));

            var defsResponse = await httpclient.SendAsync(defsRequest);

            var defsResponseBody = await defsResponse.Content.ReadAsStringAsync();

            var defsObj = new { value = new[] { new { id = 0, name = "" } } };
            var defs = JsonConvert.DeserializeAnonymousType(defsResponseBody, defsObj).value.Select(d => (d.id, d.name));
            return defs;
        }

        private static async Task CheckSubmissionBuilds(IEnumerable<(int id, string name)> defs, StudentRecord r, HttpClient httpclient, string connectionString)
        {
            string v = $"assignment-w{r.Week}-{r.Student}-ci";

            if (!defs.Any(d => d.name == v))
                return;

            var defId = defs.FirstOrDefault(d => d.name == v).id;


            bool? timelyBuildResult = null;
            bool? lateBuildResult = null;

            if (r.TimelyCommit != null && r.TimelyBuildResult == null)
            {
                timelyBuildResult = await CheckBuild(defId, r.Week, r.Student, r.TimelyCommit, httpclient);
                await UpdateBuildResult(r, connectionString, timelyBuildResult, "TimelyBuildResult");

            }

            if (r.LateCommit != null && r.LateBuildResult == null)
            {
                lateBuildResult = await CheckBuild(defId, r.Week, r.Student, r.LateCommit, httpclient);
                await UpdateBuildResult(r, connectionString, lateBuildResult, "LateBuildResult");
            }

        }

        private static async Task UpdateBuildResult(StudentRecord r, string connectionString, bool? buildResult, string buildType)
        {
            if (buildResult.HasValue)
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    var queryParams = new
                    {
                        st = r.Student,
                        w = r.Week,
                        buildres = buildResult,
                    };

                    await connection.ExecuteAsync($@"UPDATE [dbo].[Final] SET
                                      {buildType} = @buildres
                                  WHERE Student = @st AND Week = @w", queryParams);

                    Console.WriteLine($"Updated {buildType} for {r.Student}/{r.Week}");
                }
            }
        }

        private static async Task<bool?> CheckBuild(int defId, int week, string student, string commit, HttpClient httpclient)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"https://dev.azure.com/ashabinskiy/kmaooad18/_apis/build/builds?api-version=4.1&definitions={defId}");

                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{vstspat}")));

                var response = await httpclient.SendAsync(request);

                var responseBody = await response.Content.ReadAsStringAsync();

                var buildObj = new { value = new[] { new { id = 0, status = "", sourceVersion = "", result = "" } } };
                var builds = JsonConvert.DeserializeAnonymousType(responseBody, buildObj).value;

                var completedBuilds = builds.Where(b => b.sourceVersion == commit && b.status == "completed").ToList();

                if (completedBuilds.Count == 0)
                    return null;

                return completedBuilds.Any(cb => cb.result == "succeeded");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {student}: {ex.Message}");
                return null;
            }
        }

        static async Task SaveSubmissions()
        {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("kmaooad18-ci"));
            var tokenAuth = new Credentials(pat);
            client.Credentials = tokenAuth;

            const string repoNamePattern = @"^assignment-w(?<week>\d{1,2})-(?<student>.+)$";

            var repos = (await client.Repository.GetAllForOrg("kmaooad18")).ToList();


            var deadlines = new Dictionary<int, DateTime>
            {
                { 2, new DateTime(2018, 9, 23) },
                { 3, new DateTime(2018, 9, 30) },
                { 4, new DateTime(2018, 10, 8) },
                { 5, new DateTime(2018, 10, 16) },
                { 6, new DateTime(2018, 10, 21) },
                { 7, new DateTime(2018, 11, 4) },
                { 10, new DateTime(2018, 11, 18) },
                { 11, new DateTime(2018, 11, 25) },
                { 12, new DateTime(2018, 12, 2) }
            };

            foreach (var r in repos)
            {
                await ProcessSubmission(r, client, repoNamePattern, deadlines);
            }
        }

        private static async Task ProcessSubmission(Repository r, GitHubClient client, string repoNamePattern, Dictionary<int, DateTime> dealines)
        {
            if (!Regex.IsMatch(r.Name, repoNamePattern))
                return;

            try
            {
                Console.WriteLine($"Processing {r.Name}....");

                var groups = Regex.Matches(r.Name, repoNamePattern)[0].Groups;

                var week = Convert.ToInt32(groups["week"].Value);
                var student = groups["student"].Value;

                var deadline = dealines[week];
                var lateDeadline = dealines[week].AddDays(7);

                Console.WriteLine($"Week: {week}. Dealine: {deadline}. Late deadline: {lateDeadline}");

                var commits = await client.Repository.Commit.GetAll(r.Id);

                var timelyCommit = GetLatestCommit(deadline, commits);
                var lateCommit = GetLatestCommit(lateDeadline, commits);
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    var selectParams = new { st = student, w = week };
                    var studentRecord = await connection.QuerySingleOrDefaultAsync(@"SELECT * FROM Final WHERE Student = @st AND Week = @w", selectParams);

                    var queryParams = new
                    {
                        st = student,
                        w = week,
                        timely = timelyCommit?.Sha,
                        late = lateCommit?.Sha == timelyCommit?.Sha ? null : lateCommit?.Sha,
                    };

                    if (studentRecord == null)
                    {
                        await connection.ExecuteAsync(
                                 @"INSERT INTO [dbo].[Final]
                                                       ([Student]
                                                       ,[Week]
                                                       ,[TimelyCommit]
                                                       ,[LateCommit])
                                                 VALUES
                                                       (@st
                                                       ,@w
                                                       ,@timely
                                                       ,@late)", queryParams);

                        Console.WriteLine($"Added student record for {student}/{week}");
                    }
                    else if (studentRecord.TimelyCommit == null && queryParams.timely != null || studentRecord.LateCommit == null && queryParams.late != null)
                    {
                        await connection.ExecuteAsync(
                             @"UPDATE [dbo].[Final] SET
                                      TimelyCommit = @timely,
                                      LateCommit = @late
                                  WHERE Student = @st AND Week = @w", queryParams);

                        Console.WriteLine($"Updated student record for {student}/{week}");
                    }
                    else
                    {
                        Console.WriteLine($"Student record for {student}/{week} is up to date");
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        private static GitHubCommit GetLatestCommit(DateTime deadline, IReadOnlyList<GitHubCommit> commits)
        {
            return commits.Where(c => c.Commit.Committer.Date < deadline.AddDays(1).AddHours(1)).OrderByDescending(c => c.Commit.Committer.Date).FirstOrDefault();
        }
    }
}
