using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.UnitTests.Organization;

public class EstablishmentChannelsTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EnableChannels_OnNewEstablishment_CreatesOneChannelPerType()
    {
        var establishment = new Establishment();

        establishment.EnableChannels([ChannelType.Virtual, ChannelType.Physical], Now);

        Assert.Equal([ChannelType.Physical, ChannelType.Virtual], establishment.EnabledChannels());
        Assert.All(establishment.Channels, channel =>
        {
            Assert.Equal(establishment.Id, channel.EstablishmentId);
            Assert.Equal(Now, channel.CreatedAt);
        });
    }

    [Fact]
    public void EnableChannels_RemovedType_IsDisabledAndReusedWhenItReturns()
    {
        var establishment = new Establishment();
        establishment.EnableChannels([ChannelType.Physical], Now);
        var physical = Assert.Single(establishment.Channels);

        establishment.EnableChannels([ChannelType.Virtual], Now.AddDays(1));

        Assert.Equal([ChannelType.Virtual], establishment.EnabledChannels());
        Assert.False(physical.IsActive);

        establishment.EnableChannels([ChannelType.Physical, ChannelType.Virtual], Now.AddDays(2));

        Assert.Equal(2, establishment.Channels.Count);
        Assert.True(physical.IsActive);
        Assert.Equal(Now, physical.CreatedAt);
    }

    [Theory]
    [InlineData("Physical")]
    [InlineData(" physical ")]
    [InlineData("PHYSICAL")]
    public void ChannelTypes_IgnoresCaseAndSpaces(string value)
    {
        Assert.Equal([ChannelType.Physical], InputValues.ChannelTypes([value]));
    }

    [Fact]
    public void ChannelTypes_ReturnsTypesInFixedOrder()
    {
        Assert.Equal([ChannelType.Physical, ChannelType.Virtual], InputValues.ChannelTypes(["Virtual", "Physical"]));
    }

    [Fact]
    public void ChannelTypes_WithoutItems_ThrowsRequired()
    {
        AssertInvalid([], OrganizationContract.ChannelsRequired);
        AssertInvalid(null, OrganizationContract.ChannelsRequired);
    }

    [Theory]
    [InlineData("Satelite")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(null)]
    public void ChannelTypes_WithUnknownType_ThrowsInvalid(string? value)
    {
        AssertInvalid([value], OrganizationContract.ChannelTypeInvalid);
    }

    [Fact]
    public void ChannelTypes_WithRepeatedType_ThrowsRepeated()
    {
        AssertInvalid(["Physical", "physical"], OrganizationContract.ChannelsRepeated);
    }

    private static void AssertInvalid(IReadOnlyList<string?>? values, string message)
    {
        var exception = Assert.Throws<RequestValidationException>(() => InputValues.ChannelTypes(values));

        Assert.Equal("Channels", exception.Field);
        Assert.Equal(message, exception.Message);
    }
}
