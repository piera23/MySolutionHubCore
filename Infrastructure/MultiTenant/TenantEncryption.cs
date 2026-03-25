using Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.MultiTenant
{
    /// <summary>
    /// Cifra/decifra le connection string dei tenant con AES-256-CBC.
    /// Supporta il versioning della chiave per consentire la rotazione senza downtime:
    ///
    ///   Formato cifrato: "v{N}:{base64(IV+ciphertext)}"
    ///   Backward-compat: se non c'è prefisso "v{N}:" si usa EncryptionKey (legacy v1).
    ///
    /// Configurazione:
    ///   "MultiTenant": {
    ///     "EncryptionKey": "legacy-key (usata se EncryptionKeys:v1 non è impostata)",
    ///     "EncryptionKeyVersion": "v1",          // versione usata per cifrare
    ///     "EncryptionKeys": {
    ///       "v1": "chiave-v1-32byte-o-più",
    ///       "v2": "chiave-v2-nuova-dopo-rotazione"
    ///     }
    ///   }
    ///
    /// Rotazione:
    ///   1. Aggiungere "v2" in EncryptionKeys
    ///   2. Impostare EncryptionKeyVersion = "v2"
    ///   3. Chiamare POST /api/v1/admin/tenants/{id}/rotate-key per ogni tenant
    ///      (o tutti in batch via POST /api/v1/admin/rotate-all-keys)
    ///   4. Rimuovere "v1" dopo la rotazione completa
    /// </summary>
    public class TenantEncryption : ITenantEncryption
    {
        private readonly string _currentVersion;
        private readonly Dictionary<string, byte[]> _keys; // version → 32-byte AES key

        public TenantEncryption(IConfiguration config)
        {
            // Carica tutte le versioni della chiave
            _keys = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            var keysSection = config.GetSection("MultiTenant:EncryptionKeys");
            foreach (var child in keysSection.GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(child.Value))
                    _keys[child.Key] = DeriveKey(child.Value);
            }

            // Backward compat: se EncryptionKeys:v1 non è impostata, usa EncryptionKey legacy
            if (!_keys.ContainsKey("v1"))
            {
                var legacy = config["MultiTenant:EncryptionKey"];
                if (!string.IsNullOrWhiteSpace(legacy))
                    _keys["v1"] = DeriveKey(legacy);
            }

            if (_keys.Count == 0)
                throw new InvalidOperationException(
                    "Nessuna chiave di cifratura configurata in MultiTenant:EncryptionKey " +
                    "o MultiTenant:EncryptionKeys.");

            _currentVersion = config["MultiTenant:EncryptionKeyVersion"]?.Trim() ?? "v1";

            if (!_keys.ContainsKey(_currentVersion))
                throw new InvalidOperationException(
                    $"La versione chiave corrente '{_currentVersion}' non è presente in " +
                    "MultiTenant:EncryptionKeys.");
        }

        public string Encrypt(string plainText)
        {
            var key = _keys[_currentVersion];

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var enc = aes.CreateEncryptor();
            var plain  = Encoding.UTF8.GetBytes(plainText);
            var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);

            var combined = new byte[aes.IV.Length + cipher.Length];
            aes.IV.CopyTo(combined, 0);
            cipher.CopyTo(combined, aes.IV.Length);

            return $"{_currentVersion}:{Convert.ToBase64String(combined)}";
        }

        public string Decrypt(string cipherText)
        {
            string version;
            string base64;

            // Formato versioned: "v1:base64..." oppure "v2:base64..."
            var colon = cipherText.IndexOf(':');
            if (colon > 0 && colon <= 5 && cipherText[0] == 'v')
            {
                version = cipherText[..colon];
                base64  = cipherText[(colon + 1)..];
            }
            else
            {
                // Legacy (nessun prefisso versione): usa v1
                version = "v1";
                base64  = cipherText;
            }

            if (!_keys.TryGetValue(version, out var key))
                throw new InvalidOperationException(
                    $"Chiave di cifratura per versione '{version}' non trovata. " +
                    "Aggiungere la chiave in MultiTenant:EncryptionKeys:{version}.");

            var fullBytes   = Convert.FromBase64String(base64);
            var iv          = fullBytes[..16];
            var cipherBytes = fullBytes[16..];

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV  = iv;

            using var dec = aes.CreateDecryptor();
            var plain = dec.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plain);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static byte[] DeriveKey(string keyString)
            => SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }
}
