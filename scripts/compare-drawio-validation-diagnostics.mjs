import fs from "node:fs";
import path from "node:path";

const [baselinePath, currentPath, outputDirectory, currentDrawioPath] = process.argv.slice(2);
if (!baselinePath || !currentPath || !outputDirectory) {
  console.error("Usage: node scripts/compare-drawio-validation-diagnostics.mjs <baseline.json> <current.json> <output-directory> [current.drawio]");
  process.exit(2);
}

const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));
const current = JSON.parse(fs.readFileSync(currentPath, "utf8"));
fs.mkdirSync(outputDirectory, { recursive: true });

const segmentKey = finding => (finding.offendingSegments ?? [])
  .map(segment => `${segment.start.x},${segment.start.y}-${segment.end.x},${segment.end.y}`)
  .sort()
  .join("|");
const locationKey = finding => segmentKey(finding) || (finding.locations ?? [])
  .map(point => `${point.x},${point.y}`)
  .sort()
  .join("|");
const findingKey = finding => [
  finding.category,
  finding.logicalRouteId,
  finding.otherRouteId ?? "",
  finding.otherNodeId ?? "",
  locationKey(finding)
].join("~");
const relationKey = finding => [
  finding.logicalRouteId,
  finding.otherRouteId ?? "",
  finding.otherNodeId ?? "",
  locationKey(finding)
].join("~");

const baselineFindings = new Map(baseline.findings.map(finding => [findingKey(finding), finding]));
const currentFindings = new Map(current.findings.map(finding => [findingKey(finding), finding]));
const added = [...currentFindings].filter(([key]) => !baselineFindings.has(key)).map(([, finding]) => finding);
const removed = [...baselineFindings].filter(([key]) => !currentFindings.has(key)).map(([, finding]) => finding);
const removedByRelation = new Map(removed.map(finding => [relationKey(finding), finding]));
const changedCategory = added
  .filter(finding => removedByRelation.has(relationKey(finding)))
  .map(finding => ({
    routeId: finding.logicalRouteId,
    otherRouteId: finding.otherRouteId,
    location: locationKey(finding),
    before: removedByRelation.get(relationKey(finding)).category,
    after: finding.category
  }));

const baselineGeometry = new Map((baseline.routeGeometry ?? []).map(route => [route.logicalRouteId, route]));
const currentGeometry = new Map((current.routeGeometry ?? []).map(route => [route.logicalRouteId, route]));
const allRouteIds = [...new Set([...baselineGeometry.keys(), ...currentGeometry.keys()])].sort();
const changedRoutes = allRouteIds.filter(routeId =>
  baselineGeometry.has(routeId) &&
  currentGeometry.has(routeId) &&
  JSON.stringify(baselineGeometry.get(routeId).points) !== JSON.stringify(currentGeometry.get(routeId).points));
const changedRouteSet = new Set(changedRoutes);

const routeChangeCause = routeId => {
  const before = baselineGeometry.get(routeId);
  const after = currentGeometry.get(routeId);
  if (!before || !after || JSON.stringify(before.points) === JSON.stringify(after.points)) return null;
  const candidateUnchanged =
    JSON.stringify(before.selectedCandidatePoints) === JSON.stringify(after.selectedCandidatePoints);
  const beforeMatchesCandidate =
    JSON.stringify(before.points) === JSON.stringify(before.selectedCandidatePoints);
  const afterMatchesCandidate =
    JSON.stringify(after.points) === JSON.stringify(after.selectedCandidatePoints);
  if (candidateUnchanged && !beforeMatchesCandidate && afterMatchesCandidate) {
    return "0c62c50 retained the unchanged selected candidate as the authoritative traversal fallback; the 81-build traversal compiler had rewritten it.";
  }
  if (candidateUnchanged) {
    return "The selected candidate is unchanged; 0c62c50 changed post-selection terminal/lane/traversal compilation.";
  }
  return "0c62c50 changed the selected candidate during global or regional selection.";
};

const cause = finding => {
  const primaryCause = routeChangeCause(finding.logicalRouteId);
  const otherCause = finding.otherRouteId ? routeChangeCause(finding.otherRouteId) : null;
  if (primaryCause && otherCause) return `Both involved routes changed. Primary: ${primaryCause} Other: ${otherCause}`;
  if (primaryCause) return `Rejected route: ${primaryCause}`;
  if (otherCause) return `Interacting route: ${otherCause}`;
  return "Neither serialized route changed; corridor-success enforcement eligibility changed.";
};

const categories = [...new Set([
  ...baseline.categories.map(category => category.category),
  ...current.categories.map(category => category.category)
])].sort().map(category => {
  const before = baseline.categories.find(item => item.category === category);
  const after = current.categories.find(item => item.category === category);
  const introduced = added.filter(item => item.category === category);
  const alreadyPresent = current.findings.filter(item =>
    item.category === category && baselineFindings.has(findingKey(item)));
  return {
    category,
    baselineUniqueRoutes: before?.uniqueLogicalRoutes ?? 0,
    currentUniqueRoutes: after?.uniqueLogicalRoutes ?? 0,
    baselineRawFindings: before?.rawValidatorFindings ?? 0,
    currentRawFindings: after?.rawValidatorFindings ?? 0,
    currentDistinctLocations: after?.distinctPhysicalLocations ?? 0,
    introducedFindings: introduced.length,
    alreadyPresentFindings: alreadyPresent.length,
    removedFindings: removed.filter(item => item.category === category).length
  };
});

const routeById = new Map(current.routes.map(route => [route.logicalRouteId, route]));
const representative = categories.map(category => {
  const routes = current.routes
    .filter(route => route.violations.some(finding => finding.category === category.category))
    .sort((left, right) => left.selectedPoints.length - right.selectedPoints.length ||
      left.logicalRouteId.localeCompare(right.logicalRouteId));
  if (routes.length === 0) return { category: category.category, examples: [] };
  const magnitude = route => Math.max(...route.violations
    .filter(finding => finding.category === category.category)
    .map(finding => finding.magnitude));
  const worst = [...routes].sort((left, right) => magnitude(right) - magnitude(left) ||
    right.selectedPoints.length - left.selectedPoints.length)[0];
  const middle = routes[Math.floor(routes.length / 2)];
  return {
    category: category.category,
    examples: [...new Map([
      ["smallest", routes[0]],
      ["worst", worst],
      ["representative", middle]
    ].map(([role, route]) => [route.logicalRouteId, {
      role,
      logicalRouteId: route.logicalRouteId,
      source: route.sourceNode,
      target: route.targetNode,
      points: route.selectedPoints,
      violations: route.violations.filter(finding => finding.category === category.category),
      alternatives: route.candidates,
      candidateAlternativesAvailable: route.candidateAlternativesAvailable
    }])).values()]
  };
});

const delta = {
  baseline: baseline.summary,
  current: current.summary,
  netChange: current.summary.enforcedFindings - baseline.summary.enforcedFindings,
  addedCount: added.length,
  removedCount: removed.length,
  changedCategory,
  changedRoutes: changedRoutes.map(routeId => ({
    logicalRouteId: routeId,
    directCause: routeChangeCause(routeId),
    before: baselineGeometry.get(routeId).points,
    after: currentGeometry.get(routeId).points,
    selectedCandidateBefore: baselineGeometry.get(routeId).selectedCandidatePoints,
    selectedCandidateAfter: currentGeometry.get(routeId).selectedCandidatePoints
  })),
  unchangedGeometryWithAddedValidation: [...new Set(added
    .filter(finding => !changedRouteSet.has(finding.logicalRouteId) &&
      !(finding.otherRouteId && changedRouteSet.has(finding.otherRouteId)))
    .map(finding => finding.logicalRouteId))].sort(),
  added: added.map(finding => ({ ...finding, directCause: cause(finding) })),
  removed,
  categories,
  representative
};
fs.writeFileSync(path.join(outputDirectory, "validation-delta.json"), JSON.stringify(delta, null, 2));

const categoryMeaning = {
  NodeInteriorIntersection: ["Route enters a non-terminal node interior.", "Fatal candidate", "A, E, F, G"],
  SharedNonZeroLengthSegment: ["Two logical routes occupy the same collinear geometry.", "Ambiguous candidate", "A, B, C, F, H"],
  SpacingDeficit: ["Parallel routes are separated by less than configured spacing.", "Degraded candidate", "B, C, F, H"],
  ReusedBend: ["Two routes use the same interior bend coordinate.", "Ambiguous candidate", "A, B, C, F, H"],
  ImmediateReversal: ["A route reverses immediately on one axis.", "Fatal candidate when enforced", "A, F, G"],
  Other: ["Unmapped validator condition.", "Policy required", "A, B, F, G"]
};
const tableRows = categories.map(category => {
  const [appearance, meaning, handling] = categoryMeaning[category.category] ?? categoryMeaning.Other;
  return `| ${category.category} | ${category.currentUniqueRoutes} | ${appearance} | ${meaning} | Strict when corridor-enforced | ${handling} |`;
}).join("\n");

const markdown = `# Draw.io strict-validation decision report

## Baselines

- Baseline: ${baseline.summary.enforcedFindings} enforced findings, ${baseline.summary.uniqueRejectedRoutes} unique rejected routes, ${baseline.summary.allValidatorFindings} raw observations.
- Current: ${current.summary.enforcedFindings} enforced findings, ${current.summary.uniqueRejectedRoutes} unique rejected routes, ${current.summary.allValidatorFindings} raw observations.
- Exact delta: ${added.length} added, ${removed.length} removed, net ${delta.netChange}.
- Routes with changed selected geometry: ${changedRoutes.length}.
- Category changes at the same normalized relationship/location: ${changedCategory.length}.

The net increase of ${delta.netChange} is not a set of ${delta.netChange} independently pairable findings. It is the arithmetic result of ${added.length} additions and ${removed.length} removals.

## Findings by category

| Category | Baseline routes | Current routes | Baseline findings | Current findings | Locations | Added | Present in both | Removed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
${categories.map(item => `| ${item.category} | ${item.baselineUniqueRoutes} | ${item.currentUniqueRoutes} | ${item.baselineRawFindings} | ${item.currentRawFindings} | ${item.currentDistinctLocations} | ${item.introducedFindings} | ${item.alreadyPresentFindings} | ${item.removedFindings} |`).join("\n")}

## Strict-rule inventory

| Rule | Trigger | Blocks normal CLI | Present in 0.3.8-era validator | Configurable | Proposed reporting class |
| --- | --- | --- | --- | --- | --- |
| NodeCollision | Any non-terminal route segment intersects a node's open interior rectangle. | Yes, only when the edge has a successful corridor mapping. | Yes | No | Fatal |
| SharedSegment | Two routes share positive-length collinear geometry. | Yes, only when both edges share a successful corridor. | Yes | No | Ambiguous |
| ParallelSpacing | Parallel overlapping spans are closer than ParallelLaneSpacing. | Yes, only when both edges share a successful corridor. | Yes | Spacing value only | Degraded |
| ReusedBend | Two routes contain the same interior waypoint. | Yes, only when both edges share a successful corridor. | Yes | No | Ambiguous |
| ImmediateReversal | Three consecutive points reverse on the same axis. | No; explicitly excluded before ownership normalization. | Yes | No | Informational in current output gate |

Rules requested in the review but not currently emitted by the final validator—AmbiguousMerge, AmbiguousDeparture, TerminalOrderViolation, TerminalAxisViolation, MalformedTraversal, OwnershipOrSegmentationFailure, and UnsupportedTopologyFallback—appear only in selector, traversal, ownership, or serializer diagnostics. They are not members of the current strict-validation result.

## Decision table

| Category | Routes affected | What it looks like | Architectural meaning | Current severity | Possible handling |
| --- | ---: | --- | --- | --- | --- |
${tableRows}

## Delta evidence

See \`validation-delta.json\` for every added and removed normalized finding, exact before/after route point arrays, direct cause classification, representative routes, and candidate evaluation records.
`;
fs.writeFileSync(path.join(outputDirectory, "validation-decision-report.md"), markdown);

if (currentDrawioPath) {
  const regressionDrawioPath = path.join(outputDirectory, "new-81-to-93-regressions.drawio");
  fs.writeFileSync(regressionDrawioPath, focusedDrawio(added, "81-to-93", currentDrawioPath));
  const representativeFindings = representative.flatMap(group =>
    group.examples.flatMap(example => example.violations.slice(0, 1)));
  fs.writeFileSync(
    path.join(outputDirectory, "representative-examples.drawio"),
    focusedDrawio(representativeFindings, "representative-examples", currentDrawioPath));
}

console.log(path.join(outputDirectory, "validation-delta.json"));
console.log(path.join(outputDirectory, "validation-decision-report.md"));

function focusedDrawio(findings, diagnosticName, drawioPath) {
  const markers = findings.map((finding, index) => {
    const point = finding.locations?.[0] ?? finding.offendingSegments?.[0]?.start ?? { x: 20 + index * 6, y: 60 + index * 6 };
    const route = routeById.get(finding.logicalRouteId);
    const routeName = route
      ? `${route.sourceNode.name} -> ${route.targetNode.name}`
      : finding.logicalRouteId;
    const label = escapeXml(`${index + 1}: ${routeName} | ${finding.category} | ${finding.logicalRouteId} | (${point.x},${point.y})`);
    return `<mxCell id="delta_marker_${String(index + 1).padStart(4, "0")}" value="${label}" style="shape=ellipse;whiteSpace=wrap;html=1;fillColor=#ff0000;strokeColor=#ffffff;fontColor=#ffffff;fontSize=10;" vertex="1" parent="1"><mxGeometry x="${point.x - 9}" y="${point.y - 9}" width="18" height="18" as="geometry"/></mxCell>`;
  }).join("");
  let drawio = fs.readFileSync(drawioPath, "utf8");
  drawio = drawio.replace(/<mxCell id="diagnostic_(?:marker|banner)_[^"]*"[\s\S]*?<\/mxCell>/g, "");
  drawio = drawio.replace(/<diagram name="Diagnostics - [^"]*"[\s\S]*?<\/diagram>/g, "");
  drawio = drawio.replace(/(<diagram name="Architecture"[\s\S]*?<root>[\s\S]*?)(<\/root>)/, `$1${markers}$2`);
  return drawio.replace("<mxfile ", `<mxfile deltaDiagnostic="${diagnosticName}" deltaFindings="${findings.length}" `);
}

function escapeXml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("\"", "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}
