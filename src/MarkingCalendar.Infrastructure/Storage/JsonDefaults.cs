using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkingCalendar.Infrastructure.Storage;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

