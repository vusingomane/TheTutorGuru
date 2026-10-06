using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.Repositories;
using TutorGuru.Api.Data;

namespace TutorGuru.Api.Identity;

// Durable shared keys survive restarts and autoscale. Only encrypted XML reaches
// PostgreSQL; the separate root key lives in workspace Secrets.
public sealed class DatabaseKeyRepository(Database db) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var c = db.Open().GetAwaiter().GetResult();
        return c.Query<string>("SELECT xml FROM guru_identity_keys").Select(KeyEnvelope.Unprotect).Select(XElement.Parse).ToArray();
    }
    public void StoreElement(XElement element, string friendlyName)
    {
        using var c = db.Open().GetAwaiter().GetResult();
        c.Execute("INSERT INTO guru_identity_keys(name,xml) VALUES(@name,@xml) ON CONFLICT(name) DO NOTHING",
            new { name = friendlyName, xml = KeyEnvelope.Protect(element.ToString(SaveOptions.DisableFormatting)) });
    }
}
