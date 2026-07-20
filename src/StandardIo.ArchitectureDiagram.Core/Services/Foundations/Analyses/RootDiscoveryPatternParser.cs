using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;

internal static class RootDiscoveryPatternParser
{
    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    internal const RegexOptions Options = RegexOptions.CultureInvariant;

    public static IReadOnlyList<RootDiscoveryPatternDefinition> Parse(string? source)
    {
        var result = new List<RootDiscoveryPatternDefinition>();
        var lines = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var pattern = lines[index].Trim();
            if (pattern.Length == 0) continue;
            try
            {
                _ = new Regex(pattern, Options, MatchTimeout);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Root discovery pattern on line {index + 1} is invalid: {pattern}", exception);
            }
            result.Add(new RootDiscoveryPatternDefinition(result.Count, index + 1, pattern));
        }
        return result;
    }

    public static Regex Compile(RootDiscoveryPatternDefinition pattern) =>
        new(pattern.PatternText, Options, MatchTimeout);
}
