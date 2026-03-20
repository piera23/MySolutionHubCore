namespace MasterDb.Entities
{
    /// <summary>
    /// Impostazione chiave-valore specifica per un tenant.
    /// Esempi: "MaxUsers" → "100", "Branding:LogoUrl" → "https://...", "Theme:Color" → "#FF0000"
    /// </summary>
    public class TenantSetting
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
