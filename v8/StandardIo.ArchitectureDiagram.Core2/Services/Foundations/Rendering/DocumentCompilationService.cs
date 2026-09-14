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
        var html = new XElement("html", new XAttribute("lang", "en"), new XElement("head", new XElement("meta", new XAttribute("charset", "utf-8")), new XElement("title", "Project diagrams"), new XElement("style", "html,body{margin:0;height:100%;background:#111827;color:white;font-family:Arial}body{display:flex;flex-direction:column}nav{display:flex;gap:8px;padding:10px}button{padding:10px;background:#374151;color:white;border:1px solid #94a3b8;cursor:pointer}button[aria-selected=true]{background:#075985}iframe{border:0;width:100%;flex:1}iframe[hidden]{display:none}")), body);
        return Encoding.UTF8.GetBytes("<!DOCTYPE html>" + html.ToString(SaveOptions.DisableFormatting));
    }
}
