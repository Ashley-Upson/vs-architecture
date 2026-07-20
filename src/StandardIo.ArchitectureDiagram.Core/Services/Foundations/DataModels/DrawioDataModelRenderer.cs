using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.DataModels;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;

public sealed class DrawioDataModelRenderer : IDataModelRenderer<DrawioPage>
{
    public DrawioPage Render(
        DataModelDiagram model,
        DataModelRenderSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        settings ??= new DataModelRenderSettings();
        var root = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        var entities = model.Entities.OrderBy(entity => entity.OrderKey, StringComparer.Ordinal).ToArray();
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, entities.Length))));
        var rects = new Dictionary<string, TableRect>(StringComparer.Ordinal);
        var yByRow = new Dictionary<int, int>();

        foreach (var indexed in entities.Select((entity, index) => (entity, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = indexed.index / columns;
            var column = indexed.index % columns;
            if (!yByRow.TryGetValue(row, out var y))
            {
                y = settings.CanvasMargin;
                for (var prior = 0; prior < row; prior++)
                    y += entities.Skip(prior * columns).Take(columns)
                        .Select(entity => Height(entity, settings)).DefaultIfEmpty(0).Max() + settings.RowSpacing;
                yByRow[row] = y;
            }
            var rect = new TableRect(
                settings.CanvasMargin + column * settings.ColumnWidth,
                y,
                settings.TableWidth,
                Height(indexed.entity, settings));
            rects[indexed.entity.Id] = rect;
            AddTable(root, indexed.entity, rect, settings);
        }

        foreach (var relationship in model.Relationships.OrderBy(item => item.OrderKey, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!rects.TryGetValue(relationship.SourceEntityId, out var source) ||
                !rects.TryGetValue(relationship.TargetEntityId, out var target)) continue;
            root.Add(RelationshipCell(relationship, source, target, settings));
        }

        var graph = new XElement("mxGraphModel",
            new XAttribute("dx", "1200"), new XAttribute("dy", "900"),
            new XAttribute("grid", "0"), new XAttribute("gridSize", "10"),
            new XAttribute("guides", "1"), new XAttribute("tooltips", "1"),
            new XAttribute("connect", "1"), new XAttribute("arrows", "1"),
            new XAttribute("fold", "1"), new XAttribute("page", "0"),
            new XAttribute("pageScale", "1"), new XAttribute("pageWidth", "1600"),
            new XAttribute("pageHeight", "1200"), new XAttribute("background", settings.Canvas.BackgroundColor), root);
        return new DrawioPage("Data Model", "data-model", graph,
            model.Diagnostics.Select(item => new DiagramDiagnostic(item.Code, item.Message, item.SemanticId)).ToArray());
    }

    private static int Height(DataModelEntity entity, DataModelRenderSettings settings) =>
        settings.HeaderHeight + entity.Properties.Count * settings.PropertyRowHeight;

    private static void AddTable(XElement root, DataModelEntity entity, TableRect rect, DataModelRenderSettings settings)
    {
        var id = CellId(entity.Id);
        root.Add(Vertex(id, string.Empty,
            "shape=rectangle;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#9fb7d5;strokeWidth=1;",
            "1", rect.X, rect.Y, rect.Width, rect.Height));
        root.Add(Vertex(id + "_header", entity.Name,
            "shape=rectangle;whiteSpace=wrap;html=1;align=left;verticalAlign=middle;spacingLeft=8;fontStyle=1;fontColor=#ffffff;fillColor=#2f5f97;strokeColor=#9fb7d5;",
            id, 0, 0, rect.Width, settings.HeaderHeight));
        for (var index = 0; index < entity.Properties.Count; index++)
        {
            var property = entity.Properties[index];
            var fill = index % 2 == 0 ? "#f7fbff" : "#e6f0fb";
            root.Add(Vertex(id + "_row_" + index,
                $"{property.DeclaredTypeDisplay}: {property.Name}",
                $"shape=rectangle;whiteSpace=wrap;html=1;align=left;verticalAlign=middle;spacingLeft=8;fontColor=#111111;fillColor={fill};strokeColor=#9fb7d5;",
                id, 0, settings.HeaderHeight + index * settings.PropertyRowHeight,
                rect.Width, settings.PropertyRowHeight));
        }
    }

    private static XElement RelationshipCell(
        DataModelRelationship relationship,
        TableRect source,
        TableRect target,
        DataModelRenderSettings settings)
    {
        var sourcePoint = new CellPoint(source.CenterX, source.CenterY);
        var targetPoint = new CellPoint(target.CenterX, target.CenterY);
        var horizontalFirst = Math.Abs(targetPoint.X - sourcePoint.X) >= Math.Abs(targetPoint.Y - sourcePoint.Y);
        var points = horizontalFirst
            ? new[] { new CellPoint(targetPoint.X, sourcePoint.Y) }
            : new[] { new CellPoint(sourcePoint.X, targetPoint.Y) };
        return new XElement("mxCell",
            new XAttribute("id", "data_model_relationship_" + relationship.Id),
            new XAttribute("relationshipKind", relationship.Kind),
            new XAttribute("sourcePropertyId", relationship.SourcePropertyId),
            new XAttribute("style", $"edgeStyle=none;noEdgeStyle=1;orthogonal=0;curved=0;html=1;rounded=0;endArrow=block;endFill=1;strokeColor={settings.Connector.StrokeColor};strokeWidth={settings.Connector.StrokeWidth};"),
            new XAttribute("edge", "1"), new XAttribute("parent", "1"),
            new XAttribute("source", CellId(relationship.SourceEntityId)),
            new XAttribute("target", CellId(relationship.TargetEntityId)),
            new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"),
                new XElement("Array", new XAttribute("as", "points"), points.Select(point =>
                    new XElement("mxPoint", new XAttribute("x", point.X), new XAttribute("y", point.Y))))));
    }

    private static XElement Vertex(string id, string value, string style, string parent, int x, int y, int width, int height) =>
        new("mxCell", new XAttribute("id", id), new XAttribute("value", value), new XAttribute("style", style),
            new XAttribute("vertex", "1"), new XAttribute("parent", parent),
            new XElement("mxGeometry", new XAttribute("x", x), new XAttribute("y", y),
                new XAttribute("width", width), new XAttribute("height", height), new XAttribute("as", "geometry")));

    private static string CellId(string entityId) => "data_model_" + entityId;
    private readonly record struct CellPoint(int X, int Y);
    private readonly record struct TableRect(int X, int Y, int Width, int Height)
    {
        public int CenterX => X + Width / 2;
        public int CenterY => Y + Height / 2;
    }
}
