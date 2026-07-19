using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// No real logic to test yet (repo-setup phase, see ROADMAP.md). This one
    /// test just proves the test project restores and runs before any mod
    /// code exists, so `dotnet test` is wired up from commit one.
    /// </summary>
    public class PlaceholderTests
    {
        [Fact]
        public void Placeholder_ProjectIsWired()
        {
            Assert.True(true);
        }
    }
}
