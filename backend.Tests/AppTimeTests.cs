using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

public class AppTimeTests
{
    [Fact]
    public void Default_Zone_Is_India_Standard_Time()
    {
        Assert.Equal(TimeSpan.FromMinutes(330), AppTime.Zone.BaseUtcOffset);
    }

    [Fact]
    public void ToLocal_Converts_Utc_To_India_Time()
    {
        var utc = new DateTime(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 30, 1, 30, 0), AppTime.ToLocal(utc));
    }

    [Fact]
    public void ToLocal_Treats_Unspecified_As_Utc()
    {
        var fromDb = new DateTime(2026, 9, 29, 4, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(new DateTime(2026, 9, 29, 9, 30, 0), AppTime.ToLocal(fromDb));
    }

    [Fact]
    public void StartOfDayUtc_Is_Previous_Evening_In_Utc()
    {
        Assert.Equal(new DateTime(2026, 9, 28, 18, 30, 0), AppTime.StartOfDayUtc(new DateOnly(2026, 9, 29)));
    }
}
