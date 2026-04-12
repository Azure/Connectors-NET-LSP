//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace SdkLspServer.Diagnostics;

/// <summary>
/// Computes the Levenshtein (edit) distance between two strings.
/// Used for typo detection in attribute argument values.
/// </summary>
internal static class LevenshteinDistance
{
    /// <summary>
    /// Computes the minimum number of single-character edits (insertions, deletions,
    /// or substitutions) required to change <paramref name="source"/> into <paramref name="target"/>.
    /// The comparison is case-insensitive (characters are compared via <see cref="char.ToLowerInvariant"/>).
    /// </summary>
    /// <param name="source">The source string.</param>
    /// <param name="target">The target string.</param>
    public static int Compute(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.IsNullOrEmpty(target) ? 0 : target.Length;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        int sourceLength = source.Length;
        int targetLength = target.Length;

        // Swap so the shorter string is used for the row buffer, reducing memory to O(min(n,m)).
        if (sourceLength < targetLength)
        {
            (source, target) = (target, source);
            (sourceLength, targetLength) = (targetLength, sourceLength);
        }

        // Use a single-row buffer sized to the shorter dimension.
        int[] previousRow = new int[targetLength + 1];
        int[] currentRow = new int[targetLength + 1];

        for (int j = 0; j <= targetLength; j++)
        {
            previousRow[j] = j;
        }

        for (int i = 1; i <= sourceLength; i++)
        {
            currentRow[0] = i;

            for (int j = 1; j <= targetLength; j++)
            {
                int cost = char.ToLowerInvariant(source[i - 1]) == char.ToLowerInvariant(target[j - 1])
                    ? 0
                    : 1;

                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + cost);
            }

            // Swap rows
            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[targetLength];
    }
}
