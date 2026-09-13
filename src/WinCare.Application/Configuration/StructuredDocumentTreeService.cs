namespace WinCare.Application.Configuration;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Structured document hierarchical tree viewer and query service (yamlist).
/// Parses indented YAML/JSON configuration files into an inspectable object tree.
/// </summary>
public static class StructuredDocumentTreeService
{
    public sealed class ConfigNode
    {
        public required string Key { get; init; }
        public string? Value { get; set; }
        public int IndentLevel { get; init; }
        public int LineNumber { get; init; }
        public List<ConfigNode> Children { get; } = new();

        public ConfigNode? FindChild(string key)
        {
            return Children.Find(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Parses an indented YAML configuration string into a hierarchy of <see cref="ConfigNode"/> roots.
    /// </summary>
    public static IReadOnlyList<ConfigNode> ParseYamlStructure(string yamlContent)
    {
        var roots = new List<ConfigNode>();
        if (string.IsNullOrWhiteSpace(yamlContent)) return roots;

        var stack = new Stack<ConfigNode>();
        using var reader = new StringReader(yamlContent);
        string? line;
        int lineNum = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineNum++;
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            int indent = CountLeadingSpaces(line);

            string key;
            string? value = null;
            int colonIdx = trimmed.IndexOf(':');

            if (colonIdx >= 0)
            {
                key = trimmed[..colonIdx].Trim().Trim('-', ' ');
                string remainder = trimmed[(colonIdx + 1)..].Trim();
                if (!string.IsNullOrEmpty(remainder))
                {
                    value = remainder.Trim('"', '\'');
                }
            }
            else
            {
                key = trimmed.Trim('-', ' ');
            }

            var node = new ConfigNode
            {
                Key = key,
                Value = value,
                IndentLevel = indent,
                LineNumber = lineNum
            };

            // Unwind stack to parent indent
            while (stack.Count > 0 && stack.Peek().IndentLevel >= indent)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack.Peek().Children.Add(node);
            }

            stack.Push(node);
        }

        return roots;
    }

    /// <summary>
    /// Traverses a parsed root tree to retrieve a value by dot-notation path (e.g. "server.port").
    /// </summary>
    public static string? QueryPath(IReadOnlyList<ConfigNode> roots, string dotPath)
    {
        if (string.IsNullOrWhiteSpace(dotPath) || roots.Count == 0) return null;

        var parts = dotPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        ConfigNode? current = null;

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (i == 0)
            {
                current = roots.FirstOrDefault(r => string.Equals(r.Key, part, StringComparison.OrdinalIgnoreCase));
            }
            else if (current != null)
            {
                current = current.FindChild(part);
            }

            if (current == null) return null;
        }

        return current?.Value;
    }

    private static int CountLeadingSpaces(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else if (c == '\t') count += 4;
            else break;
        }
        return count;
    }
}
