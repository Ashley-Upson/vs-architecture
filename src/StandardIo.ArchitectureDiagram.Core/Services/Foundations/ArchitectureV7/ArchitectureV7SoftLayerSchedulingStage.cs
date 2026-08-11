using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7SoftLayerSchedulingStage
{
    public ArchitectureV7FrozenLayerSchedule Schedule(
        ArchitectureV7PositionalOwnershipResult ownership,
        ArchitectureV7ReservationReconciliationResult reservation,
        ArchitectureV7SoftCohortAnalysisResult softAnalysis)
    {
        if (ownership is null) throw new ArgumentNullException(nameof(ownership));
        if (reservation is null) throw new ArgumentNullException(nameof(reservation));
        if (softAnalysis is null) throw new ArgumentNullException(nameof(softAnalysis));

        var projection = ownership.Projection;
        var nodes = projection.PhysicalNodes.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        var depths = reservation.Inspection.NaturalDepthByPhysicalNodeId;
        var cohorts = softAnalysis.Cohorts.OrderBy(item => item.TokenSuffix, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.TokenSuffix, StringComparer.Ordinal).ToArray();
        var cohortByNode = cohorts.SelectMany(cohort => cohort.MemberPhysicalNodeIds.Select(id => (id, cohort)))
            .ToDictionary(item => item.id, item => item.cohort, StringComparer.Ordinal);
        var hardByName = reservation.Table.Reservations.ToDictionary(item => item.Name, item => ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(item.NodeRow), StringComparer.OrdinalIgnoreCase);
        var hardBase = hardByName;
        var hardByNode = nodes.Values.Where(node => !node.IsExternal).Select(node => (node, reservation: MatchReservation(node.Name, reservation.Table.Reservations)))
            .Where(item => item.reservation is not null).ToDictionary(item => item.node.PhysicalNodeId, item => hardByName[item.reservation!.Name], StringComparer.Ordinal);
        foreach (var node in nodes.Values.Where(node => node.IsExternal)) hardByNode[node.PhysicalNodeId] = hardByName[reservation.Table.External.Name];
        var ordinaryLayers = new HashSet<int>(nodes.Values
            .Where(node => !node.IsExternal && !node.IsStandalone && !hardByNode.ContainsKey(node.PhysicalNodeId) && !cohortByNode.ContainsKey(node.PhysicalNodeId))
            .Select(node => depths.TryGetValue(node.PhysicalNodeId, out var depth) ? depth : 0));

        var baseAnchor = new Dictionary<string, (string Identity, int Layer, int Count)>(StringComparer.OrdinalIgnoreCase);
        var cohortEdges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rawPreferences = new List<(ArchitectureV7SoftCohort Cohort, string Anchor, int AnchorLayer, int PreferredLayer, Dictionary<string, int> Counts)>();
        foreach (var cohort in cohorts)
        {
            var memberSet = new HashSet<string>(cohort.MemberPhysicalNodeIds, StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in projection.PhysicalLinks.Where(link => memberSet.Contains(link.SourcePhysicalNodeId)))
            {
                if (!nodes.TryGetValue(link.DestinationPhysicalNodeId, out var target) || memberSet.Contains(target.PhysicalNodeId)) continue;
                var identity = AnchorIdentity(target);
                counts[identity] = counts.TryGetValue(identity, out var count) ? count + 1 : 1;
            }
            if (counts.Count == 0) counts["ordinary:0"] = 1;
            var anchor = counts.OrderByDescending(item => item.Value)
                .ThenByDescending(item => AnchorLayer(item.Key))
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .First().Key;
            var anchorLayer = AnchorLayer(anchor);
            var preferred = Math.Max(0, anchorLayer - 1);
            if (anchor.StartsWith("soft:", StringComparison.OrdinalIgnoreCase)) cohortEdges[cohort.TokenSuffix] = anchor.Substring("soft:".Length);
            rawPreferences.Add((cohort, anchor, anchorLayer, preferred, counts));
            baseAnchor[cohort.TokenSuffix] = (anchor, anchorLayer, counts[anchor]);
        }

        var diagnostics = new List<string>();
        var orderedPreferences = TopologicalOrder(rawPreferences, cohortEdges, diagnostics);
        var softBaseIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var softPreferenceList = new List<ArchitectureV7SoftLayerPreference>();
        foreach (var item in orderedPreferences)
        {
            var exceptions = item.Cohort.MemberPhysicalNodeIds
                .Where(id => depths.TryGetValue(id, out var depth) && depth > item.PreferredLayer)
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => $"{id}:natural-layer-{depths[id]}-exceeds-preferred-{item.PreferredLayer}")
                .ToArray();
            softBaseIndex[item.Cohort.TokenSuffix] = item.PreferredLayer;
            softPreferenceList.Add(new ArchitectureV7SoftLayerPreference(item.Cohort.TokenSuffix, item.Cohort.MemberPhysicalNodeIds,
                item.Anchor, item.PreferredLayer, item.AnchorLayer, item.Counts, exceptions,
                "v7-soft-layer-schedule;generic-non-exclusive;dependency-aware;viability-before-specificity"));
        }

        var finalSoftLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in orderedPreferences)
        {
            var shifts = orderedPreferences.Count(previous => NeedsInsertion(previous) &&
                (previous.PreferredLayer < item.PreferredLayer || (previous.PreferredLayer == item.PreferredLayer &&
                    StringComparer.OrdinalIgnoreCase.Compare(previous.Cohort.TokenSuffix, item.Cohort.TokenSuffix) < 0)));
            finalSoftLayer[item.Cohort.TokenSuffix] = checked(item.PreferredLayer + shifts);
        }
        var finalHardLayer = hardBase.ToDictionary(item => item.Key, item => checked(item.Value + orderedPreferences.Count(soft => NeedsInsertion(soft) && soft.PreferredLayer <= item.Value)), StringComparer.OrdinalIgnoreCase);
        var entries = new List<ArchitectureV7LayerScheduleEntry>();
        foreach (var hard in reservation.Table.Reservations)
            entries.Add(new ArchitectureV7LayerScheduleEntry(hard.Name, finalHardLayer[hard.Name], true, hard.IsExternal, null));
        foreach (var item in orderedPreferences)
            if (NeedsInsertion(item))
                entries.Add(new ArchitectureV7LayerScheduleEntry(item.Cohort.TokenSuffix, finalSoftLayer[item.Cohort.TokenSuffix], false, false, item.Cohort.TokenSuffix));
        foreach (var ordinaryLayer in ordinaryLayers.OrderBy(item => item))
            if (!entries.Any(entry => entry.NodeLayer == ordinaryLayer))
                entries.Add(new ArchitectureV7LayerScheduleEntry("ordinary:" + ordinaryLayer, ordinaryLayer, false, false, null));

        var preferredByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in orderedPreferences)
        {
            if (item.Cohort.MemberPhysicalNodeIds.Any(id => depths.TryGetValue(id, out var depth) && depth > item.PreferredLayer))
            {
                foreach (var id in item.Cohort.MemberPhysicalNodeIds.Where(id => !depths.TryGetValue(id, out var depth) || depth <= item.PreferredLayer))
                    preferredByNode[id] = finalSoftLayer[item.Cohort.TokenSuffix];
            }
            else
                foreach (var id in item.Cohort.MemberPhysicalNodeIds) preferredByNode[id] = finalSoftLayer[item.Cohort.TokenSuffix];
        }

        var shiftedReservations = reservation.Table.Reservations.Select(item => new ArchitectureV7FrozenReservation(item.Name, item.Pattern, item.Order,
            item.MatchCount, checked(finalHardLayer[item.Name] * 2 + ArchitectureV7ReservationCoordinates.FirstReservedNodeRow), item.IsExternal)).ToArray();
        var shiftedTable = new ArchitectureV7FrozenReservationTable(shiftedReservations, Fingerprint(reservation.Table.Fingerprint, shiftedReservations));
        var preSoft = reservation.Table.Fingerprint;
        var fingerprint = Fingerprint(preSoft + "#" + softAnalysis.Fingerprint, entries, softPreferenceList, diagnostics);
        return new ArchitectureV7FrozenLayerSchedule(shiftedTable, entries, softPreferenceList, preferredByNode, diagnostics, preSoft, fingerprint);

        bool NeedsInsertion((ArchitectureV7SoftCohort Cohort, string Anchor, int AnchorLayer, int PreferredLayer, Dictionary<string, int> Counts) item) =>
            !ordinaryLayers.Contains(item.PreferredLayer) || hardBase.Values.Contains(item.PreferredLayer);

        string AnchorIdentity(ArchitectureV7PhysicalNode node)
        {
            if (node.IsExternal) return "External";
            if (cohortByNode.TryGetValue(node.PhysicalNodeId, out var cohort)) return "soft:" + cohort.TokenSuffix;
            var hard = MatchReservation(node.Name, reservation.Table.Reservations);
            if (hard is not null) return "hard:" + hard.Name;
            return "ordinary:" + (depths.TryGetValue(node.PhysicalNodeId, out var depth) ? depth : 0);
        }

        int AnchorLayer(string identity)
        {
            if (identity.Equals("External", StringComparison.OrdinalIgnoreCase)) return hardBase[reservation.Table.External.Name];
            if (identity.StartsWith("hard:", StringComparison.OrdinalIgnoreCase) && hardBase.TryGetValue(identity.Substring("hard:".Length), out var hardLayer)) return hardLayer;
            if (identity.StartsWith("soft:", StringComparison.OrdinalIgnoreCase) && baseAnchor.TryGetValue(identity.Substring("soft:".Length), out var soft)) return soft.Layer;
            return identity.StartsWith("ordinary:", StringComparison.OrdinalIgnoreCase) && int.TryParse(identity.Substring("ordinary:".Length), out var natural) ? natural : 0;
        }

        ArchitectureV7FrozenReservation? MatchReservation(string name, IReadOnlyList<ArchitectureV7FrozenReservation> reservations)
        {
            foreach (var item in reservations.Where(item => !item.IsExternal).OrderBy(item => item.Order))
            {
                var suffix = item.Pattern.Trim();
                if (suffix.StartsWith("*", StringComparison.Ordinal)) suffix = suffix.Substring(1);
                if (suffix.EndsWith("$", StringComparison.Ordinal)) suffix = suffix.Substring(0, suffix.Length - 1);
                if (suffix.Length > 0 && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return item;
            }
            return null;
        }
    }

    private static IReadOnlyList<(ArchitectureV7SoftCohort Cohort, string Anchor, int AnchorLayer, int PreferredLayer, Dictionary<string, int> Counts)> TopologicalOrder(
        IReadOnlyList<(ArchitectureV7SoftCohort Cohort, string Anchor, int AnchorLayer, int PreferredLayer, Dictionary<string, int> Counts)> items,
        IReadOnlyDictionary<string, string> edges, ICollection<string> diagnostics)
    {
        var byName = items.ToDictionary(item => item.Cohort.TokenSuffix, StringComparer.OrdinalIgnoreCase);
        var result = new List<(ArchitectureV7SoftCohort Cohort, string Anchor, int AnchorLayer, int PreferredLayer, Dictionary<string, int> Counts)>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string name)
        {
            if (visited.Contains(name)) return;
            if (!visiting.Add(name)) { diagnostics.Add("v7-soft-layer-cycle:" + name); return; }
            if (edges.TryGetValue(name, out var dependency) && byName.ContainsKey(dependency)) Visit(dependency);
            visiting.Remove(name); visited.Add(name);
            result.Add(byName[name]);
        }
        foreach (var item in items.OrderBy(item => item.Cohort.TokenSuffix, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Cohort.TokenSuffix, StringComparer.Ordinal)) Visit(item.Cohort.TokenSuffix);
        return result;
    }

    private static string Fingerprint(string seed, IReadOnlyList<ArchitectureV7FrozenReservation> reservations)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(seed + "#" + string.Join("|", reservations.Select(item => item.Name + ":" + item.NodeRow))))).Replace("-", string.Empty);
    }

    private static string Fingerprint(string seed, IReadOnlyList<ArchitectureV7LayerScheduleEntry> entries,
        IReadOnlyList<ArchitectureV7SoftLayerPreference> preferences, IReadOnlyList<string> diagnostics)
    {
        using var sha = SHA256.Create();
        var text = seed + "#" + string.Join("|", entries.OrderBy(item => item.NodeLayer).ThenBy(item => item.Name, StringComparer.Ordinal).Select(item => item.Name + ":" + item.NodeLayer + ":" + item.IsHardReservation)) +
            "#" + string.Join("|", preferences.Select(item => item.TokenSuffix + ":" + item.PreferredNodeLayer + ":" + item.PreferredAnchor)) + "#" + string.Join("|", diagnostics);
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }
}
