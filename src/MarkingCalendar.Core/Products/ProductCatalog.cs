namespace MarkingCalendar.Core.Products;

public sealed record ProductCodeMeaning(string Code, string Name, IReadOnlyList<string> SearchTerms, string SourceUrl, string SourceContext);
public sealed record ProductListRow(string Section, string SourceName, string TnvedText, string Okpd2Text, string Conditions, IReadOnlyList<ProductCodeMeaning> Meanings);
public sealed record ProductListGroup(string Id, string Name, string SourceUrl, string SourceHeading, string SourceHash, string Revision,
    DateTimeOffset ChangedAt, DateTimeOffset CheckedAt, string Conditions, IReadOnlyList<string> Examples, IReadOnlyList<ProductListRow> Rows);
public sealed record ProductCatalog(int SchemaVersion, string Revision, IReadOnlyList<ProductListGroup> Groups);

public sealed class ProductCatalogValidationException(IReadOnlyList<string> errors) : Exception(string.Join(' ', errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class ProductCatalogValidator
{
    public static void EnsureValid(ProductCatalog? catalog)
    {
        var errors = Validate(catalog);
        if (errors.Count > 0) throw new ProductCatalogValidationException(errors);
    }

    public static IReadOnlyList<string> Validate(ProductCatalog? catalog)
    {
        if (catalog is null) return ["Товарный справочник содержит пустой JSON."];
        var errors = new List<string>();
        if (catalog.SchemaVersion != 1) errors.Add($"Версия схемы товарного справочника не поддерживается: {catalog.SchemaVersion}.");
        Required(catalog.Revision, "ревизия справочника", errors);
        if (catalog.Groups is null || catalog.Groups.Count == 0) errors.Add("В справочнике отсутствуют группы.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in catalog.Groups ?? [])
        {
            if (group is null) { errors.Add("Пустая группа справочника."); continue; }
            if (!ids.Add(group.Id ?? string.Empty)) errors.Add($"Идентификатор товарной группы повторяется: {group.Id}.");
            Required(group.Id, "id группы", errors); Required(group.Name, "название группы", errors);
            Required(group.SourceHeading, "заголовок источника", errors); Required(group.SourceHash, "хеш источника", errors);
            Required(group.Revision, "ревизия группы", errors);
            if (group.Conditions is null || group.Examples is null || group.Examples.Any(item => item is null)) errors.Add("Не заданы условия или примеры группы.");
            if (group.Rows is null || group.Rows.Count == 0) errors.Add("В группе отсутствуют товарные строки.");
            if (!Allowed(group.SourceUrl, "xn--80ajghhoc2aj1c8b.xn--p1ai", "/business/projects/")) errors.Add($"Источник группы «{group.Name}» должен вести на Честный знак.");
            if (group.ChangedAt == default || group.CheckedAt == default || group.ChangedAt > group.CheckedAt) errors.Add($"У группы «{group.Name}» некорректны времена проверки.");
            foreach (var row in group.Rows ?? [])
            {
                if (row is null) { errors.Add("Пустая товарная строка."); continue; }
                Required(row.SourceName, "исходное название", errors);
                if (row.Section is null || row.TnvedText is null || row.Okpd2Text is null || row.Conditions is null || row.Meanings is null) errors.Add("Не заданы поля товарной строки.");
                if (string.IsNullOrWhiteSpace(row.TnvedText) && string.IsNullOrWhiteSpace(row.Okpd2Text)) errors.Add($"У строки «{row.SourceName}» не указан ни один код.");
                foreach (var meaning in row.Meanings ?? [])
                {
                    if (meaning is null) { errors.Add("Пустая расшифровка кода."); continue; }
                    Required(meaning.Code, "код", errors); Required(meaning.Name, "название кода", errors); Required(meaning.SourceContext, "контекст кода", errors);
                    if (meaning.SearchTerms is null || meaning.SearchTerms.Any(item => item is null)) errors.Add("Не заданы поисковые названия кода.");
                    if (!Allowed(meaning.SourceUrl, "eec.eaeunion.org", "/")) errors.Add($"Источник кода {meaning.Code} должен вести на ЕЭК.");
                }
            }
        }
        return errors;
    }

    private static void Required(string? value, string label, List<string> errors) { if (string.IsNullOrWhiteSpace(value)) errors.Add($"Не заполнено поле: {label}."); }
    private static bool Allowed(string? value, string host, string prefix) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal);
}
