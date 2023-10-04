namespace NeoAgi.AWS.CodeArtifact.Pruner.Models
{
    internal class QueuedActionDiscoverPackages : QueuedAction
    {
        public string Domain { get; }
        public string Repository { get; }
        public int PageIterationCount { get; }
        public string NextToken { get; }

        public QueuedActionDiscoverPackages(string domain, string repository, int pageIterationCount, string nextToken)
        {
            Domain = domain;
            Repository = repository;
            PageIterationCount = pageIterationCount;
            NextToken = nextToken;
        }
    }
}
