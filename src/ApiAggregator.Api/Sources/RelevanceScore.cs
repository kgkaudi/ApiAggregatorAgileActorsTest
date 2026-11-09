namespace ApiAggregator.Api.Sources;

internal static class RelevanceScore
{
    /// <summary>Converts a 0-based position in a ranked list into a 0..1 score (first = 1.0).</summary>
    public static double FromRank(int index, int count) =>
        count <= 0 ? 0 : Math.Round(1.0 - (double)index / count, 4);
}
