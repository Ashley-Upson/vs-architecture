// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using System.Text;
using System.Xml.Linq;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
internal static class DiagramTestDocument
{
    internal static XDocument Parse(byte[] bytes)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        foreach (var cell in document.Descendants("mxCell").Where(cell => cell.Attribute("typeName") is not null))
        {
            string value = (string)cell.Attribute("value")!;
            if (!value.StartsWith("<span")) continue;
            var label = XElement.Parse("<label>" + value.Replace("<br>", "<br/>") + "</label>");
            cell.SetAttributeValue("value", string.Join("\n", label.Elements("span").Select(span => span.Value)));
        }
        return document;
    }
}
