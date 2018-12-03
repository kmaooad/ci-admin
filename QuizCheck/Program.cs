using KmaOoad18.CI.Admin;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuizCheck
{
    class Program
    {
        static void Main(string[] args)
        {
            ProcessAnswers().Wait();

            Console.ReadKey();
        }

        private static async Task ProcessAnswers()
        {
            var pat = "";
            var client = new GitHubClient(new Octokit.ProductHeaderValue("kmaooad18-ci"));
            var tokenAuth = new Credentials(pat);
            client.Credentials = tokenAuth;

            const string repoNamePattern = @"^quiz-w8-(?<student>.+)$";

            var repos = (await client.Repository.GetAllForOrg("kmaooad18"))
            .Where(r => Regex.IsMatch(r.Name, repoNamePattern)).ToList();

            Console.WriteLine($"Found {repos.Count} repos");

            var rightAnswers = new[] { "b", "e", "b", "b", "b|c", "a b e", "a", "a", "c", "a" };

            var httpclient = new HttpClient();

            var commonlist = new StringBuilder();

            foreach (var r in repos)
            {

                var checkStr = new StringBuilder();
                var result = 0;

                try
                {
                    var student = Regex.Matches(r.Name, repoNamePattern)[0].Groups["student"].Value;

                    var fileExists = new DirectoryInfo("D:/quiz-w8").GetFiles().Any(f => f.Name.StartsWith(student));

                    if (fileExists)
                    {
                        Console.WriteLine($"Check results exist for {student}, skipping");
                        Console.WriteLine();

                        continue;
                    }

                    Console.WriteLine($"Processing {student}");

                    var commit = (await client.Repository.Commit.GetAll(r.Id)).Where(c => c.Commit.Committer.Date < new DateTime(2018, 10, 29)).OrderByDescending(c => c.Commit.Committer.Date).FirstOrDefault();

                    if (commit != null)
                    {
                        Console.WriteLine($"Found latest commit on {commit.Commit.Committer.Date}");
                    }
                    else
                    {
                        Console.WriteLine($"No commit found");
                    }
                    
                    var commitSha = commit.Sha;

                    var refs = await client.Git.Reference.GetAll(r.Id);

                    var reference = refs.FirstOrDefault(rf => rf.Ref == "refs/heads/submissionfixed");

                    if (reference == null)
                        reference = await client.Git.Reference.Create(r.Id, new NewReference("refs/heads/submissionfixed", commitSha));

                    var submissionContent = (await client.Repository.Content.GetAllContentsByRef(r.Id, "submissionfixed"));

                    var answersContentUrl = submissionContent.FirstOrDefault(c => c.Name == "answer.txt")?.DownloadUrl;
                                        
                    var answersContent = await (await httpclient.GetAsync(answersContentUrl)).Content.ReadAsStringAsync();
                    
                    if (string.IsNullOrEmpty(answersContent))
                    {
                        checkStr.AppendLine("No answers found");
                        Console.WriteLine("No answers found");
                        result = 0;
                    }
                    else
                    {
                        var answers = answersContent.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

                        Console.WriteLine($"Answers contain {answers.Length} lines, processing...");


                        for (int i = 0; i < answers.Length; i++)
                        {
                            var ansregex = @"(?<q>\d{1,2})(\.)?\s(?<a>.+)";

                            var ansLine = answers[i];

                            if (!Regex.IsMatch(ansLine, ansregex))
                            {
                                checkStr.AppendLine($"{ansLine} # Not a valid answer line");
                                continue;
                            }

                            var answerParts = Regex.Matches(answers[i], ansregex)[0].Groups;

                            var questionParsed = Int32.TryParse(answerParts["q"].Value, out int q);

                            if (!questionParsed)
                            {
                                checkStr.AppendLine($"{ansLine} # Not a valid answer line");
                                continue;
                            }

                            if (q > rightAnswers.Length)
                                break;

                            var ans = answerParts["a"].Value.ToLower().Replace("с", "c");
                            var ra = rightAnswers[q - 1].ToLower();
                            var raoptions = ra.Split('|');

                            if (raoptions.Contains(ans))
                            {
                                checkStr.AppendLine($"{ansLine} RIGHT +1");
                                result++;
                            }
                            else
                            {
                                checkStr.AppendLine($"{ansLine} WRONG ({ra})");
                            }
                        }

                        Console.WriteLine("DONE");

                        checkStr
                        .AppendLine()
                        .AppendLine("===================")
                        .AppendLine($"TOTAL: {result}/10")
                        .AppendLine("===================");

                        commonlist.AppendLine($"{student}, {result}");

                        //await CiBuildManager.UploadFile(pat, r.Name, "check.txt", checkStr.ToString());
                        //Console.WriteLine($"Check results uploaded to {r.Name}");

                        //using (StreamWriter outputFile = new StreamWriter($"D:/quiz-w8/{student}.txt"))
                        //{
                        //    await outputFile.WriteAsync(checkStr.ToString());

                        //    Console.WriteLine($"Check results saved to {student}.txt");
                        //    Console.WriteLine();
                        //}
                    }                    
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                    Console.WriteLine();
                }

                
            }

            using (StreamWriter outputFile = new StreamWriter($"D:/quiz-w8/all.txt"))
            {
                await outputFile.WriteAsync(commonlist.ToString());                
            }
        }
    }
}
