using System.Globalization;
using System.Text;
using System.Xml;

namespace MarkingCalendar.Core.Changes;

public static class AtomFeedWriter
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    public static string Write(ChangeHistory history, Uri changelogUrl, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(changelogUrl);
        var batches = history.Batches
            .OrderByDescending(batch => batch.CheckedAt)
            .Take(50)
            .ToArray();
        var output = new StringBuilder();
        using var text = new StringWriter(output, CultureInfo.InvariantCulture);
        using var writer = XmlWriter.Create(text, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = false
        });

        writer.WriteStartDocument();
        writer.WriteStartElement("feed", AtomNamespace);
        writer.WriteElementString("title", AtomNamespace, "История изменений календаря маркировки");
        writer.WriteElementString("id", AtomNamespace, changelogUrl.AbsoluteUri);
        writer.WriteStartElement("link", AtomNamespace);
        writer.WriteAttributeString("href", changelogUrl.AbsoluteUri);
        writer.WriteEndElement();
        var feedUpdated = (batches.FirstOrDefault()?.CheckedAt ?? DateTimeOffset.UnixEpoch).ToOffset(MoscowOffset);
        writer.WriteElementString("updated", AtomNamespace, feedUpdated.ToString("O", CultureInfo.InvariantCulture));

        var summaryFactory = new ChangeSummaryFactory();
        foreach (var batch in batches)
        {
            var checkedAt = batch.CheckedAt.ToOffset(MoscowOffset);
            var summary = summaryFactory.Create(batch.Changes, 30, today, new HashSet<string>());
            writer.WriteStartElement("entry", AtomNamespace);
            writer.WriteElementString("title", AtomNamespace, ChangeMarkdownFormatter.BatchTitle(batch));
            writer.WriteElementString("id", AtomNamespace, batch.Id);
            writer.WriteElementString("updated", AtomNamespace, checkedAt.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteStartElement("link", AtomNamespace);
            writer.WriteAttributeString("href", changelogUrl.AbsoluteUri);
            writer.WriteEndElement();
            writer.WriteStartElement("content", AtomNamespace);
            writer.WriteAttributeString("type", "text");
            writer.WriteString(ChangeSummaryTextFormatter.Format(summary, checkedAt, new HashSet<string>()));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();
        return output.ToString();
    }
}
