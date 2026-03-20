using FluentAssertions;
using Infrastructure.MultiTenant;
using Microsoft.Extensions.Configuration;

namespace Tests;

public class TenantEncryptionTests
{
    private static TenantEncryption CreateEncryption(string key = "chiave-test-unitari-32-caratteri!!")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MultiTenant:EncryptionKey"] = key
            })
            .Build();

        return new TenantEncryption(config);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnOriginalString()
    {
        var enc = CreateEncryption();
        var plain = "Data Source=tenant001.sqlite";

        var cipher = enc.Encrypt(plain);
        var result = enc.Decrypt(cipher);

        result.Should().Be(plain);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCipherEachTime_DueToRandomIV()
    {
        var enc = CreateEncryption();
        var plain = "Data Source=tenant001.sqlite";

        var cipher1 = enc.Encrypt(plain);
        var cipher2 = enc.Encrypt(plain);

        cipher1.Should().NotBe(cipher2);
    }

    [Fact]
    public void Encrypt_ReturnsBase64String()
    {
        var enc = CreateEncryption();
        var cipher = enc.Encrypt("test");

        var act = () => Convert.FromBase64String(cipher);
        act.Should().NotThrow();
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsException()
    {
        var enc1 = CreateEncryption("chiave-test-unitari-32-caratteri!!");
        var enc2 = CreateEncryption("altra-chiave-diversa-32-caratteri!");

        var cipher = enc1.Encrypt("dati-riservati");

        var act = () => enc2.Decrypt(cipher);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Constructor_ThrowsInvalidOperation_WhenKeyMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var act = () => new TenantEncryption(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EncryptionKey*");
    }
}
