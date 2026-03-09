using Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.MultiTenant
{
    public class TenantEncryption : ITenantEncryption
    {
        private readonly byte[] _key;

        public TenantEncryption(IConfiguration config)
        {
            var keyString = config["MultiTenant:EncryptionKey"]
                ?? throw new InvalidOperationException(
                    "MultiTenant:EncryptionKey non configurata.");

            // La chiave deve essere 32 bytes per AES-256
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
        }

        public string Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Salviamo IV + ciphertext concatenati in Base64
            var result = new byte[aes.IV.Length + cipherBytes.Length];
            aes.IV.CopyTo(result, 0);
            cipherBytes.CopyTo(result, aes.IV.Length);

            return Convert.ToBase64String(result);
        }

        public string Decrypt(string cipherText)
        {
            var fullBytes = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = _key;

            // I primi 16 bytes sono l'IV
            var iv = fullBytes[..16];
            var cipherBytes = fullBytes[16..];

            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
