using System.Security.Cryptography;
using System.Text;

namespace MarkingCalendar.Core.Events;

public static class EventId
{
    public static string FromCanonicalContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}

