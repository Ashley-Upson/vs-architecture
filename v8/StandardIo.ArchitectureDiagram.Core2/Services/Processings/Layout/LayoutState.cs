using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal static class LayoutState
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new CoordinateConverter() } };
    private sealed class CoordinateConverter : System.Text.Json.Serialization.JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, System.Type type, JsonSerializerOptions options) => reader.GetDouble();
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if(double.IsFinite(value)) writer.WriteNumberValue(System.Math.Round(value,6));
            else writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
    // Serialize values, not array/record references. Iteration counts are observations,
    // not diagram state. The source compiler model is immutable during layout.
    internal static string Capture(RenderModel model) => JsonSerializer.Serialize(new
    {
        model.Width, model.Height, model.Projects, model.CrossProjectConnections,
        Rows = model.Rows.OrderBy(pair => pair.Key).ToArray(),
        Passages = model.PassageOffsetParents.OrderBy(id => id).ToArray(),
        model.LayoutInitialized, model.ProjectLayoutInitialized, model.SharedParentLayoutInitialized
    }, Options);
}
