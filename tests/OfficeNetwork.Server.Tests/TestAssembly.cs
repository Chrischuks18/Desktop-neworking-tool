using Xunit;
// Database pools are global; keep restore and credential-preservation tests isolated.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
