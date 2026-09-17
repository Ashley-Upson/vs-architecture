using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class DocumentationContractTests
{
    private static string V8()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !Directory.Exists(Path.Combine(directory.FullName,"documentation"))) directory=directory.Parent;
        return directory!.FullName;
    }
    [Fact]
    public void EveryRegisteredRuleMustBeUniqueAndHaveItsOwnDocument()
    {
        using var provider=new ServiceCollection().AddArchitectureDiagram().BuildServiceProvider();
        var names=provider.GetServices<ILayoutRuleProcessingService>().Select(rule=>rule.GetType().Name).ToArray();
        Assert.Equal(names.Length,names.Distinct().Count());
        var catalogue=File.ReadAllText(Path.Combine(V8(),"documentation","rules","readme.md"));
        foreach(var name in names)
        {
            var shortName=name.Replace("LayoutRuleProcessingService","").Replace("RuleProcessingService","");
            Assert.True(File.Exists(Path.Combine(V8(),"documentation","rules",shortName+".md")),name);
            Assert.Contains(name,catalogue);
        }
    }
    [Fact]
    public void EverySelectableDiagramAndFormatMustHaveADocument()
    {
        foreach(var type in Enum.GetValues<DiagramTypes>())
        {
            string name=type switch {DiagramTypes.CallChain=>"call-chain",DiagramTypes.DataModel=>"data-model",_=>type.ToString().ToLowerInvariant()};
            Assert.True(File.Exists(Path.Combine(V8(),"documentation","implementations",name+".md")),type.ToString());
        }
        foreach(var format in Enum.GetValues<DiagramFormats>())
            Assert.True(File.Exists(Path.Combine(V8(),"documentation","implementations",format.ToString().ToLowerInvariant()+".md")),format.ToString());
    }
    [Fact]
    public void DocumentationLinksMustResolve()
    {
        foreach(var path in Directory.EnumerateFiles(Path.Combine(V8(),"documentation"),"*.md",SearchOption.AllDirectories))
        foreach(Match match in Regex.Matches(File.ReadAllText(path),@"\]\(([^)#]+)(?:#[^)]*)?\)"))
        {
            string target=match.Groups[1].Value;
            if(target.Contains("://",StringComparison.Ordinal)) continue;
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,target))),path+" -> "+target);
        }
    }
}
