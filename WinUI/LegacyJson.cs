// Compatibility adapter for the legacy object-tree readers. Persisted journal serialization is unchanged.
using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
namespace System.Web.Script.Serialization;
internal sealed class JavaScriptSerializer
{
    public int MaxJsonLength { get; set; } = 2097152;
    public object DeserializeObject(string json)
    {
        if (json.Length > MaxJsonLength) throw new ArgumentException("JSON 크기 제한 초과");
        try { using var doc = JsonDocument.Parse(json); return ConvertValue(doc.RootElement); }
        catch (JsonException error) { throw new ArgumentException("JSON 형식 오류", error); }
    }
    private static object ConvertValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => ConvertValue(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(ConvertValue).ToArray(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out long number) ? (object)number : value.GetDecimal(),
        _ => null
    };
}
