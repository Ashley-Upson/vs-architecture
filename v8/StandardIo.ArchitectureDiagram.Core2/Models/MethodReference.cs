using System.Text.Json.Serialization;
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed record MethodReference(string TypeName, string MethodName)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MethodId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsPublicContract { get; init; }
}
