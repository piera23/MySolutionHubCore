using FluentAssertions;
using Infrastructure.MultiTenant;

namespace Tests;

public class TenantContextTests
{
    [Fact]
    public void SetTenant_SetsAllProperties()
    {
        var ctx = new TenantContext();
        ctx.SetTenant("t1", "Azienda Demo", "Data Source=t1.sqlite");

        ctx.TenantId.Should().Be("t1");
        ctx.TenantName.Should().Be("Azienda Demo");
        ctx.ConnectionString.Should().Be("Data Source=t1.sqlite");
    }

    [Fact]
    public void IsFeatureEnabled_ReturnsFalse_WhenNoFeaturesSet()
    {
        var ctx = new TenantContext();
        ctx.SetTenant("t1", "Demo", "conn");

        ctx.IsFeatureEnabled("social:feed").Should().BeFalse();
    }

    [Fact]
    public void IsFeatureEnabled_ReturnsTrue_WhenFeatureIncluded()
    {
        var ctx = new TenantContext();
        ctx.SetTenant("t1", "Demo", "conn", enabledFeatures: ["social:feed", "social:chat"]);

        ctx.IsFeatureEnabled("social:feed").Should().BeTrue();
        ctx.IsFeatureEnabled("social:chat").Should().BeTrue();
        ctx.IsFeatureEnabled("social:notifications").Should().BeFalse();
    }

    [Fact]
    public void SetTenant_CalledTwice_ClearsPreviousFeatures()
    {
        var ctx = new TenantContext();
        ctx.SetTenant("t1", "Demo", "conn", enabledFeatures: ["social:feed"]);
        ctx.SetTenant("t2", "Altro", "conn2", enabledFeatures: ["social:chat"]);

        ctx.TenantId.Should().Be("t2");
        ctx.IsFeatureEnabled("social:feed").Should().BeFalse();
        ctx.IsFeatureEnabled("social:chat").Should().BeTrue();
    }

    [Fact]
    public void DefaultState_HasEmptyTenantId()
    {
        var ctx = new TenantContext();

        ctx.TenantId.Should().BeEmpty();
        ctx.ConnectionString.Should().BeEmpty();
    }
}
