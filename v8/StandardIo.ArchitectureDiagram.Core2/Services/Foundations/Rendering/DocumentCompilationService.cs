using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface IDocumentCompilationService { byte[] Compile(RenderedDiagramTab[] tabs, DiagramFormats format); }
internal sealed class DocumentCompilationService : IDocumentCompilationService
{
    private static string Name(DiagramTypes type) => type == DiagramTypes.DataModel ? "Entity Relationship" : type == DiagramTypes.CallChain ? "Call Chain" : type.ToString();
    public byte[] Compile(RenderedDiagramTab[] tabs, DiagramFormats format)
    {
        if (format == DiagramFormats.DrawIO)
        {
            var root = new XElement("mxfile", new XAttribute("host", "app.diagrams.net"));
            foreach (var tab in tabs)
            {
                var page = XElement.Parse(Encoding.UTF8.GetString(tab.Content)).Element("diagram")!;
                page.SetAttributeValue("id", tab.DiagramType.ToString()); page.SetAttributeValue("name", Name(tab.DiagramType)); root.Add(page);
            }
            return Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        }
        var nav = new XElement("nav", new XAttribute("role", "tablist"));
        var body = new XElement("body", nav);
        for (int i = 0; i < tabs.Length; i++)
        {
            var button = new XElement("button", new XAttribute("type", "button"), new XAttribute("role", "tab"), new XAttribute("data-tab", i), new XAttribute("aria-selected", i == 0 ? "true" : "false"), new XAttribute("aria-controls", "tab-" + i), Name(tabs[i].DiagramType));
            nav.Add(button);
            var frame = new XElement("iframe", new XAttribute("id", "tab-" + i), new XAttribute("role", "tabpanel"), new XAttribute("title", Name(tabs[i].DiagramType)), new XAttribute("srcdoc", Encoding.UTF8.GetString(tabs[i].Content)), string.Empty);
            if (i != 0) frame.SetAttributeValue("hidden", "hidden");
            body.Add(frame);
        }
        body.Add(new XElement("script", "//", new XCData("\ndocument.querySelectorAll('[data-tab]').forEach(b=>b.addEventListener('click',()=>{document.querySelectorAll('[data-tab]').forEach(x=>x.setAttribute('aria-selected',String(x===b)));document.querySelectorAll('iframe').forEach(f=>f.hidden=f.id!=='tab-'+b.dataset.tab);}));\n//")));
        var html = new XElement("html", new XAttribute("lang", "en"), new XElement("head", new XElement("meta", new XAttribute("charset", "utf-8")), new XElement("title", "Project diagrams"), new XElement("style", "html,body{margin:0;height:100%;background:#111827;color:#e5e7eb;font-family:Segoe UI,Arial,sans-serif}body{display:flex;flex-direction:column}nav{display:flex;flex-shrink:0;gap:4px;padding:14px 18px 0;background:#0b1220;border-bottom:1px solid #64748b;overflow-x:auto}button[role=tab]{position:relative;flex-shrink:0;margin:0 0 -1px;padding:13px 22px;font:inherit;font-size:14px;font-weight:500;white-space:nowrap;color:#aebdd0;background:transparent;border:1px solid transparent;border-radius:8px 8px 0 0;cursor:pointer;transition:background .15s,color .15s}button[role=tab]:hover{color:#f8fafc;background:#1e293b}button[role=tab][aria-selected=true]{color:#f8fafc;background:#111827;border-color:#64748b;border-bottom-color:#111827;box-shadow:inset 0 3px 0 #38bdf8}button[role=tab]:focus-visible{outline:2px solid #7dd3fc;outline-offset:-5px}iframe{display:block;border:0;width:100%;flex:1;min-height:0}iframe[hidden]{display:none}")), body);
        return Encoding.UTF8.GetBytes("<!DOCTYPE html>" + html.ToString(SaveOptions.DisableFormatting));
    }
}
