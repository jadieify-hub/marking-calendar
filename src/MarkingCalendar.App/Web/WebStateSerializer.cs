using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkingCalendar.App.Web;

public static class WebStateSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize(AppViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return JsonSerializer.Serialize(new WebStateMessage("state", model), Options);
    }

    private sealed record WebStateMessage(string Type, AppViewModel Model);
}

