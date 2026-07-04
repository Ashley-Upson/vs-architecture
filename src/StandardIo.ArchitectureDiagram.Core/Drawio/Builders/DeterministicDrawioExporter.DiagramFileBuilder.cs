using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed partial class DeterministicDrawioExporter
{
    private sealed partial class DiagramFileBuilder
    {
        private readonly DiagramSettings _settings;
        private readonly StyleResolver _styleResolver;

        public DiagramFileBuilder(DiagramSettings settings)
        {
            _settings = settings;
            _styleResolver = new StyleResolver(settings);
        }

        public string Build(RenderLayout layout)
        {
            var architectureRoot = new ArchitectureGenerator(this).Generate(layout);
            var dataModelRoot = new DataModelGenerator(this).Generate(layout.Graph.DataModels);

            var file = new XElement("mxfile",
                new XAttribute("host", "StandardIo.ArchitectureDiagram"),
                new XElement("diagram", new XAttribute("name", "Architecture"), GraphModel(architectureRoot)),
                new XElement("diagram", new XAttribute("name", "Data Model"), GraphModel(dataModelRoot)));

            return new XDocument(file).ToString(SaveOptions.DisableFormatting);
        }

        private XElement ArchitectureRoot(RenderLayout layout)
        {
            var root = new XElement("root",
                new XElement("mxCell", new XAttribute("id", "0")),
                new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

            foreach (var project in layout.Graph.Projects)
            {
                if (_settings.ShowProjectContainers && layout.Projects.TryGetValue(project.Id, out var projectLayout))
                {
                    root.Add(Vertex(project.Id, project.Name, BuildNodeStyle(_settings.ProjectContainerStyle), "1", projectLayout.Rect));
                }

                foreach (var nodeLayout in layout.Nodes.Values
                    .Where(node => string.Equals(node.Node.ProjectId, project.Id, StringComparison.Ordinal))
                    .OrderBy(node => node.Node.Order))
                {
                    var rect = nodeLayout.Rect;
                    ProjectLayout? parentProjectLayout = null;
                    var hasProjectParent = _settings.ShowProjectContainers &&
                        layout.Projects.TryGetValue(project.Id, out parentProjectLayout);
                    var parent = hasProjectParent ? project.Id : "1";
                    if (hasProjectParent)
                    {
                        rect = rect with
                        {
                            X = rect.X - parentProjectLayout!.Rect.X,
                            Y = rect.Y - parentProjectLayout.Rect.Y
                        };
                    }

                    root.Add(Vertex(
                        nodeLayout.Node.Id,
                        NodeLabel(nodeLayout.Node),
                        BuildNodeStyle(_styleResolver.Resolve(ToTypeNode(nodeLayout.Node))),
                        parent,
                        rect));
                }
            }

            foreach (var nodeLayout in layout.Nodes.Values
                .Where(node => node.Node.IsExternal)
                .OrderBy(node => node.Node.Order))
            {
                root.Add(Vertex(
                    nodeLayout.Node.Id,
                    $"{nodeLayout.Node.Tag}\n{nodeLayout.Node.Name}\n{nodeLayout.Node.FullName}",
                    BuildNodeStyle(_settings.ExternalDependencyStyle),
                    "1",
                    nodeLayout.Rect));
            }

            foreach (var linkLayout in layout.Links.Values.OrderBy(link => link.Link.Order))
            {
                root.Add(Edge(linkLayout));
            }

            return root;
        }

        private sealed class ArchitectureGenerator
        {
            private readonly DiagramFileBuilder _builder;

            public ArchitectureGenerator(DiagramFileBuilder builder)
            {
                _builder = builder;
            }

            public XElement Generate(RenderLayout layout)
            {
                return _builder.ArchitectureRoot(layout);
            }
        }

        private sealed class DataModelGenerator
        {
            private readonly DiagramFileBuilder _builder;

            public DataModelGenerator(DiagramFileBuilder builder)
            {
                _builder = builder;
            }

            public XElement Generate(IReadOnlyList<RenderNode> models)
            {
                return _builder.DataModelRoot(models);
            }
        }

    }
}
