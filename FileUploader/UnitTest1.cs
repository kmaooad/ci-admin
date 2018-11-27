using KmaOoad18.CI.Admin;
using System;
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
            var pat = "b0b7e192ab96fe41662a817df307a361624c4cfd";
            var targetFile = @"Leanware.Web/Spec.cs";
            var sourceFile = @"D:\Projects\_edu\kmaooad18\teacher\assignment-w12\Leanware.Web\Spec.cs";

            var repos = await CiBuildManager.GetRepos(pat, "kmaooad18", @"^(assignment-w12-.+)$");

            string content = null;

            using (var file = new StreamReader(sourceFile))
            {
                content = await file.ReadToEndAsync();
            }

            if (content != null)
            {
                foreach (var r in repos)
                    await CiBuildManager.UploadFile(pat, r.Name, targetFile, content);
            }
        }
    }
}
