namespace WinCare.Application.QuickActions;

using System;
using System.Collections.Generic;
using System.Linq;
using WinCare.CommandCatalog.Models;

/// <summary>
/// Quick Action Palette tokenizer, ranking, and search engine (i3utils).
/// Parses interactive user commands, fuzzy-matches against catalog tools, and structures command invocations.
/// </summary>
public static class QuickActionPaletteService
{
    public sealed record PaletteMatch(
        CommandDefinition Command,
        int MatchScore,
        IReadOnlyList<string> ExtractedArguments);

    /// <summary>
    /// Tokenizes a command line query into discrete arguments, honoring quoted substrings.
    /// </summary>
    public static IReadOnlyList<string> TokenizeQuery(string query)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(query)) return tokens;

        bool inQuotes = false;
        var currentToken = new System.Text.StringBuilder();

        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (currentToken.Length > 0)
                {
                    tokens.Add(currentToken.ToString());
                    currentToken.Clear();
                }
            }
            else
            {
                currentToken.Append(c);
            }
        }

        if (currentToken.Length > 0)
        {
            tokens.Add(currentToken.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Searches and scores available command definitions against the input query.
    /// </summary>
    public static IReadOnlyList<PaletteMatch> SearchCommands(
        string query,
        IEnumerable<CommandDefinition> availableCommands,
        int maxResults = 10)
    {
        var tokens = TokenizeQuery(query);
        if (tokens.Count == 0)
        {
            return Array.Empty<PaletteMatch>();
        }

        string primaryTerm = tokens[0];
        var remainderArgs = tokens.Skip(1).ToList();

        var scored = new List<PaletteMatch>();

        foreach (var cmd in availableCommands)
        {
            int score = CalculateMatchScore(primaryTerm, cmd);
            if (score > 0)
            {
                scored.Add(new PaletteMatch(cmd, score, remainderArgs));
            }
        }

        return scored
            .OrderByDescending(m => m.MatchScore)
            .Take(maxResults)
            .ToList();
    }

    private static int CalculateMatchScore(string term, CommandDefinition cmd)
    {
        int score = 0;

        // Exact ID match
        if (string.Equals(cmd.Id, term, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        // Exact keyword match
        if (cmd.Keywords != null && cmd.Keywords.Any(k => string.Equals(k, term, StringComparison.OrdinalIgnoreCase)))
        {
            return 900;
        }

        // ID starts with term
        if (cmd.Id.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }
        else if (cmd.Id.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }

        // Title starts with term
        if (cmd.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }
        else if (cmd.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 150;
        }

        // Summary match
        if (!string.IsNullOrWhiteSpace(cmd.Summary) && cmd.Summary.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        return score;
    }
}
