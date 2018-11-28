using KmaOoad18.CI.Admin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FileUploader
{
    public class UnitTest1
    {
        [Fact]
        public async Task Test1()
        {
            var pat = "5e91a39ae3938439225a68b576b42001fac65c8d";

            var files = new List<(string source, string target)>()
            {
                (@"D:\Projects\_edu\kmaooad18\teacher\assignment-w12\Leanware.Web\Spec.cs", @"Leanware.Web/Spec.cs"),
                (@"D:\Projects\_edu\kmaooad18\teacher\assignment-w12\README.md", @"README.md"),
            };

            var repos = await CiBuildManager.GetRepos(pat, "kmaooad18", @"^(assignment-w12-.+)$");

            foreach (var (source, target) in files)
            {
                string content = null;

                using (var reader = new StreamReader(source))
                {
                    content = await reader.ReadToEndAsync();
                }

                if (content != null)
                {
                    foreach (var r in repos)
                        await CiBuildManager.UploadFile(pat, r.Name, target, content);
                }
            }
        }
    }
}
