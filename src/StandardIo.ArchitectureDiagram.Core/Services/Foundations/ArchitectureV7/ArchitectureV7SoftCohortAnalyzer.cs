using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>Infers generic, non-exclusive soft cohorts without changing hard reservations or placement.</summary>
public sealed class ArchitectureV7SoftCohortAnalyzer
{
    private static readonly Regex IdentifierToken = new("[A-Z]+(?=[A-Z][a-z]|$)|[A-Z]?[a-z]+|[0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ArchitectureV7SoftCohortAnalysisResult Analyze(
        ArchitectureV7PositionalOwnershipResult ownership,
        ArchitectureV7PrePlacementConfiguration configuration)
    {
        if (ownership is null) throw new ArgumentNullException(nameof(ownership));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configuration.SoftCohortMinimumSize <= 0) throw new ArgumentOutOfRangeException(nameof(configuration), "Soft cohort minimum size must be positive.");

        var nodes = ownership.Projection.PhysicalNodes.OrderBy(node => node.PhysicalNodeId, StringComparer.Ordinal).ToArray();
        var decisions = ownership.Decisions.ToDictionary(decision => decision.PhysicalNodeId, StringComparer.Ordinal);
        var hardRules = (configuration.ReservedLayerTypePatterns ?? Array.Empty<ArchitectureV7ReservedRoleRule>())
            .OrderBy(rule => rule.Order).ThenBy(rule => rule.Name, StringComparer.Ordinal).ToArray();
        var naturalDepths = NaturalDepths(nodes, decisions);
        var eligible = nodes.Where(node => !node.IsExternal && !node.IsStandalone && !MatchesHardRule(node.Name, hardRules))
            .ToDictionary(node => node.PhysicalNodeId, node => Tokenize(node.Name), StringComparer.Ordinal);
        var remaining = new HashSet<string>(eligible.Keys, StringComparer.Ordinal);
        var cohorts = new List<ArchitectureV7SoftCohort>();

        while (remaining.Count != 0)
        {
            var candidates = eligible
                .Where(item => remaining.Contains(item.Key))
                .SelectMany(item => Suffixes(item.Value).Select(suffix => (item.Key, suffix)))
                .GroupBy(item => item.suffix.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => new Candidate(new TokenSuffix(group.Key, Tokenize(group.Key).Length, group.Key.Length), group.Select(item => item.Key).Distinct(StringComparer.Ordinal).Where(remaining.Contains).ToArray()))
                .Where(candidate => candidate.Members.Length >= configuration.SoftCohortMinimumSize)
                .OrderByDescending(candidate => candidate.Suffix.Tokens)
                .ThenByDescending(candidate => candidate.Suffix.CharacterLength)
                .ThenBy(candidate => candidate.Suffix.Display, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Suffix.Display, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0) break;

            var selected = candidates[0];
            var members = selected.Members.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var anchorCounts = ownership.Projection.PhysicalLinks
                .Where(link => members.Contains(link.SourcePhysicalNodeId, StringComparer.Ordinal) && naturalDepths.ContainsKey(link.DestinationPhysicalNodeId))
                .GroupBy(link => naturalDepths[link.DestinationPhysicalNodeId])
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count());
            cohorts.Add(new ArchitectureV7SoftCohort(selected.Suffix.Display, members,
                anchorCounts.Count == 0 ? null : anchorCounts.Keys.First(), anchorCounts,
                "v7-soft-cohort;generic-identifier-token-suffix;hard-reservations-first;viable-specificity-first"));
            foreach (var member in members) remaining.Remove(member);
        }

        return new ArchitectureV7SoftCohortAnalysisResult(ownership, cohorts, configuration.SoftCohortMinimumSize,
            Fingerprint(ownership.FreezeFingerprint, configuration.SoftCohortMinimumSize, cohorts));
    }

    private static IReadOnlyDictionary<string, int> NaturalDepths(
        IReadOnlyList<ArchitectureV7PhysicalNode> nodes,
        IReadOnlyDictionary<string, ArchitectureV7PositionalOwnershipDecision> decisions)
    {
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        int Depth(string id)
        {
            if (depths.TryGetValue(id, out var known)) return known;
            if (!visiting.Add(id)) return 0;
            var depth = decisions.TryGetValue(id, out var decision) && decision.PositionalParentPhysicalNodeId is { } parent
                ? Depth(parent) + 1 : 0;
            visiting.Remove(id);
            depths[id] = depth;
            return depth;
        }
        foreach (var node in nodes) Depth(node.PhysicalNodeId);
        return depths;
    }

    private static bool MatchesHardRule(string name, IReadOnlyList<ArchitectureV7ReservedRoleRule> rules) =>
        rules.Any(rule => SuffixMatch(name, rule.Pattern));

    private static bool SuffixMatch(string name, string pattern)
    {
        var suffix = (pattern ?? string.Empty).Trim();
        if (suffix.StartsWith("*", StringComparison.Ordinal)) suffix = suffix.Substring(1);
        if (suffix.EndsWith("$", StringComparison.Ordinal)) suffix = suffix.Substring(0, suffix.Length - 1);
        return suffix.Length != 0 && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Tokenize(string name) => IdentifierToken.Matches(name ?? string.Empty).Cast<Match>().Select(match => match.Value).ToArray();

    private static IEnumerable<TokenSuffix> Suffixes(IReadOnlyList<string> tokens)
    {
        for (var start = 0; start < tokens.Count; start++)
        {
            var suffixTokens = tokens.Skip(start).ToArray();
            if (suffixTokens.Length == 0) continue;
            yield return new TokenSuffix(string.Join(string.Empty, suffixTokens), suffixTokens.Length, suffixTokens.Sum(token => token.Length));
        }
    }

    private static string Fingerprint(string ownershipFingerprint, int minimumSize, IEnumerable<ArchitectureV7SoftCohort> cohorts)
    {
        var text = ownershipFingerprint + "#" + minimumSize + "#" + string.Join("|", cohorts.Select(cohort => cohort.TokenSuffix + ":" +
            string.Join(",", cohort.MemberPhysicalNodeIds) + ":" + cohort.PreferredDependencyAnchorNaturalDepth));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }

    private sealed record TokenSuffix(string Display, int Tokens, int CharacterLength)
    {
        public override string ToString() => Display;
    }

    private sealed record Candidate(TokenSuffix Suffix, string[] Members);
}
