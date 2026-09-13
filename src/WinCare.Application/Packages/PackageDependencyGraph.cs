namespace WinCare.Application.Packages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Strict Semantic Versioning 2.0 parser and comparator (scoop-directory).
/// </summary>
public sealed record SemVersion : IComparable<SemVersion>
{
    public int Major { get; init; }
    public int Minor { get; init; }
    public int Patch { get; init; }
    public string PreRelease { get; init; } = string.Empty;
    public string BuildMetadata { get; init; } = string.Empty;

    private static readonly Regex SemVerRegex = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<prerelease>[0-9A-Za-z\.-]+))?(?:\+(?<build>[0-9A-Za-z\.-]+))?$",
        RegexOptions.Compiled);

    public static bool TryParse(string versionString, out SemVersion version)
    {
        version = new SemVersion();
        if (string.IsNullOrWhiteSpace(versionString)) return false;

        var match = SemVerRegex.Match(versionString.Trim());
        if (!match.Success) return false;

        version = new SemVersion
        {
            Major = int.Parse(match.Groups["major"].Value),
            Minor = int.Parse(match.Groups["minor"].Value),
            Patch = int.Parse(match.Groups["patch"].Value),
            PreRelease = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : string.Empty,
            BuildMetadata = match.Groups["build"].Success ? match.Groups["build"].Value : string.Empty
        };

        return true;
    }

    public static SemVersion Parse(string versionString)
    {
        if (!TryParse(versionString, out var version))
        {
            throw new FormatException($"Invalid SemVer 2.0 version string: '{versionString}'");
        }
        return version;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;

        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;

        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;

        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // SemVer spec: normal version has higher precedence than pre-release version
        if (string.IsNullOrEmpty(PreRelease) && !string.IsNullOrEmpty(other.PreRelease)) return 1;
        if (!string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease)) return -1;
        if (string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease)) return 0;

        return ComparePreReleaseIdentifiers(PreRelease, other.PreRelease);
    }

    private static int ComparePreReleaseIdentifiers(string a, string b)
    {
        var partsA = a.Split('.');
        var partsB = b.Split('.');
        int length = Math.Min(partsA.Length, partsB.Length);

        for (int i = 0; i < length; i++)
        {
            string idA = partsA[i];
            string idB = partsB[i];

            bool aIsNum = int.TryParse(idA, out int numA);
            bool bIsNum = int.TryParse(idB, out int numB);

            if (aIsNum && bIsNum)
            {
                int diff = numA.CompareTo(numB);
                if (diff != 0) return diff;
            }
            else if (aIsNum)
            {
                return -1; // numeric has lower precedence than string
            }
            else if (bIsNum)
            {
                return 1;
            }
            else
            {
                int diff = string.Compare(idA, idB, StringComparison.Ordinal);
                if (diff != 0) return diff;
            }
        }

        return partsA.Length.CompareTo(partsB.Length);
    }

    public override string ToString()
    {
        string res = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(PreRelease)) res += $"-{PreRelease}";
        if (!string.IsNullOrEmpty(BuildMetadata)) res += $"+{BuildMetadata}";
        return res;
    }
}

/// <summary>
/// Topological package dependency graph resolver (scoop-directory).
/// Detects circular dependency deadlocks and outputs valid linear resolution sequences.
/// </summary>
public sealed class PackageDependencyGraph
{
    private readonly Dictionary<string, HashSet<string>> _adjacency = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a package to the graph.
    /// </summary>
    public void AddPackage(string packageId)
    {
        if (!_adjacency.ContainsKey(packageId))
        {
            _adjacency[packageId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Adds a dependency constraint: packageId depends on dependencyPackageId.
    /// </summary>
    public void AddDependency(string packageId, string dependencyPackageId)
    {
        AddPackage(packageId);
        AddPackage(dependencyPackageId);
        _adjacency[packageId].Add(dependencyPackageId);
    }

    /// <summary>
    /// Performs a topological sort (Kahn's algorithm) to determine the installation order.
    /// Dependencies appear before the packages that require them.
    /// </summary>
    public IReadOnlyList<string> ResolveOrder()
    {
        // Calculate in-degrees: in-degree of node X = how many dependencies X has
        var inDegree = _adjacency.ToDictionary(k => k.Key, v => v.Value.Count, StringComparer.OrdinalIgnoreCase);

        // Reverse map: who depends on X?
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in _adjacency.Keys)
        {
            dependents[node] = new List<string>();
        }

        foreach (var (package, deps) in _adjacency)
        {
            foreach (var dep in deps)
            {
                dependents[dep].Add(package);
            }
        }

        // Queue all nodes with 0 in-degree (no dependencies)
        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var order = new List<string>();

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            order.Add(current);

            foreach (string dependent in dependents[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (order.Count != _adjacency.Count)
        {
            throw new InvalidOperationException("Circular dependency detected in package graph.");
        }

        return order;
    }
}
