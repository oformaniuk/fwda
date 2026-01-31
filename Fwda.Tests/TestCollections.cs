namespace Fwda.Tests;

// Tests that mutate process-wide environment variables must not run in parallel.
[CollectionDefinition(nameof(EnvVarCollection), DisableParallelization = true)]
public class EnvVarCollection
{
}

