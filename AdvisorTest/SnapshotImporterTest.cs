using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HDT.Plugins.Advisor.Services.HsReplay;
using System.IO;

namespace AdvisorTest
{
    [TestClass]
    public class SnapshotImporterTest
    {
        [TestMethod]
        public void ImportDecksTest()
        {
            Task.Run(async () =>
            {
                var hsReplayDeckListJson = File.ReadAllText(@"../../../hsReplayDeckList.json");
                var hsReplayArchetypesJson = File.ReadAllText(@"../../../hsReplayArchetypes.json");
                var importer = new HsReplaySnapshotImporter(null);
                var count = await importer.ImportDecks(false, false, false, null, hsReplayDeckListJson, hsReplayArchetypesJson);
                Assert.AreEqual("expected", "expected");

            }).GetAwaiter().GetResult();            
        }
    }
}
