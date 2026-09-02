namespace MarkingCalendar.Core.Events;

public static class EventClassifier
{
    private static readonly Dictionary<string, EventTypeDescriptor> KnownTypes =
        new Dictionary<string, EventTypeDescriptor>(StringComparer.Ordinal)
        {
            ["розничная продажа"] = new(EventCategory.Retail, "Розничная продажа"),
            ["поэкземплярный учет по эдо"] = new(EventCategory.Edo, "Поэкземплярный учёт"),
            ["объемно-сортовой учет по эдо"] = new(EventCategory.Edo, "Объёмно-сортовой учёт"),
            ["партионный учет по эдо"] = new(EventCategory.Edo, "Партионный учёт"),
            ["вывод из оборота по иным причинам"] = new(EventCategory.Edo, "Вывод из оборота"),
            ["запрет оборота немаркированной продукции"] = new(EventCategory.Ban, "Запрет оборота"),
            ["разрешительный режим"] = new(EventCategory.Permit, "Разрешительный режим"),
            ["обязательная маркировка (ввод в оборот)"] = new(EventCategory.Marking, "Ввод в оборот"),
            ["маркировка остатков"] = new(EventCategory.Marking, "Маркировка остатков"),
            ["эксперимент"] = new(EventCategory.Marking, "Эксперимент"),
            ["обязательная регистрация"] = new(EventCategory.Registration, "Регистрация")
        };

    public static EventCategory Classify(string? type, string? stage)
    {
        var normalizedType = Normalize(type);
        if (KnownTypes.TryGetValue(normalizedType, out var descriptor))
        {
            return descriptor.Category;
        }

        var normalizedStage = Normalize(stage);
        var text = normalizedType == "другое"
            ? normalizedStage
            : $"{normalizedType} {normalizedStage}";

        if (ContainsAny(text, "запрет", "немаркирован")) return EventCategory.Ban;
        if (text.Contains("разрешительн", StringComparison.Ordinal)) return EventCategory.Permit;
        if (text.Contains("регистрац", StringComparison.Ordinal)) return EventCategory.Registration;
        if (ContainsAny(text, "эдо", "электронн", "поэкземплярн", "объемно-сорт", "партионн")) return EventCategory.Edo;
        if (ContainsAny(text, "рознич", "касс", "ккт")) return EventCategory.Retail;
        if (ContainsAny(text, "маркировк", "ввод в оборот", "нанесен")) return EventCategory.Marking;
        return EventCategory.Other;
    }

    public static string TypeLabel(string? type)
    {
        var normalized = Normalize(type);
        return KnownTypes.TryGetValue(normalized, out var descriptor)
            ? descriptor.Label
            : Display(type) is { Length: > 0 } value ? value : "Событие";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace('ё', 'е')
            .ToLowerInvariant();
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsAny(string text, params ReadOnlySpan<string> needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record EventTypeDescriptor(EventCategory Category, string Label);
}
