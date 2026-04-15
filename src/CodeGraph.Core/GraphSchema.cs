namespace CodeGraph.Core;

public static class GraphSchema
{
    public const int CurrentVersion = 1;

    public static void Validate(int schemaVersion)
    {
        if (schemaVersion != CurrentVersion)
            throw new InvalidOperationException(
                $"Graph schema version mismatch. Expected {CurrentVersion}, got {schemaVersion}. " +
                "Please re-run 'codegraph index' to regenerate the graph.");
    }
}
