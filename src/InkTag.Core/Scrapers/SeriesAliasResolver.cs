using System;
using System.Collections.Generic;

namespace InkTag.Core.Scrapers;

/// <summary>
/// Resolves alternative series titles, franchise expansions, and subtitle permutations
/// for comic volume searches against metadata providers like ComicVine.
/// </summary>
public static class SeriesAliasResolver
{
    private sealed record FranchiseRule(
        string Prefix,
        Func<string, IEnumerable<string>> ExpansionFunc
    );

    /// <summary>
    /// Static lookup table of known franchise abbreviation and season naming rules.
    /// </summary>
    private static readonly FranchiseRule[] FranchiseRules =
    [
        new("Buffy:", s =>
        {
            string remainder = s.Substring(6).Trim();
            return
            [
                $"Buffy the Vampire Slayer: {remainder}",
                remainder
            ];
        }),
        new("Buffy, Season", s =>
        [
            s.Replace("Buffy, Season", "Buffy the Vampire Slayer Season", StringComparison.OrdinalIgnoreCase)
        ])
    ];

    /// <summary>
    /// Generates candidate search titles for a given raw series name, expanding
    /// franchise abbreviations and subtitle permutations.
    /// </summary>
    /// <param name="series">The original series name.</param>
    /// <returns>An ordered list of search titles to attempt when querying volume endpoints.</returns>
    public static IReadOnlyList<string> ResolveSearchTitles(string? series)
    {
        if (string.IsNullOrWhiteSpace(series))
        {
            return Array.Empty<string>();
        }

        string trimmed = series.Trim();
        var searchTitles = new List<string> { trimmed };

        bool matchedRule = false;
        foreach (var rule in FranchiseRules)
        {
            if (trimmed.StartsWith(rule.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                matchedRule = true;
                foreach (var expansion in rule.ExpansionFunc(trimmed))
                {
                    if (!string.IsNullOrWhiteSpace(expansion) && !searchTitles.Contains(expansion, StringComparer.OrdinalIgnoreCase))
                    {
                        searchTitles.Add(expansion);
                    }
                }
                break;
            }
        }

        // Subtitle permutation: if no franchise rule matched and the series contains a colon,
        // also search for the subtitle portion (e.g. "Batman: Year One" -> "Year One")
        if (!matchedRule && trimmed.Contains(':'))
        {
            string[] parts = trimmed.Split(':', 2);
            if (parts.Length > 1)
            {
                string subtitle = parts[1].Trim();
                if (!string.IsNullOrWhiteSpace(subtitle) && !searchTitles.Contains(subtitle, StringComparer.OrdinalIgnoreCase))
                {
                    searchTitles.Add(subtitle);
                }
            }
        }

        return searchTitles;
    }
}
