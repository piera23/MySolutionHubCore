using System.Text;
using System.Text.Json;

namespace Application.Common
{
    /// <summary>
    /// Risultato di una query con keyset/cursor pagination.
    /// Più performante e consistente dell'offset pagination con dati in aggiornamento continuo.
    /// </summary>
    public record CursorPage<T>(
        IEnumerable<T> Items,
        string? NextCursor,
        bool HasMore);

    /// <summary>
    /// Utility per codificare/decodificare il cursore come Base64-URL di JSON.
    /// Il cursore codifica { id, ts } dell'ultimo elemento restituito.
    /// </summary>
    public static class CursorEncoder
    {
        public static string Encode(int id, DateTime ts)
        {
            var json = JsonSerializer.Serialize(new { id, ts = ts.ToUniversalTime() });
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static (int Id, DateTime Ts)? Decode(string? cursor)
        {
            if (string.IsNullOrEmpty(cursor))
                return null;

            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                using var doc = JsonDocument.Parse(json);
                var id = doc.RootElement.GetProperty("id").GetInt32();
                var ts = doc.RootElement.GetProperty("ts").GetDateTime();
                return (id, ts);
            }
            catch
            {
                return null;
            }
        }
    }
}
