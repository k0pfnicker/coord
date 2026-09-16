using Coord.Config;
using Coord.Host;
using Coord.Protocol;
using System.Text.Json;
using Xunit;

namespace Coord.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void DefaultConfigurationHasDevelopmentDefaults()
    {
        var config = CoordConfig.Default();

        Assert.Equal("127.0.0.1:4242", config.Network.Address);
        Assert.Equal("data", config.Storage.DataDirectory);
    }

    [Fact]
    public void CurrentProtocolIsVersionOne()
    {
        Assert.Equal(1, ProtocolConstants.CurrentVersion.Major);
    }

    [Fact]
    public void ProtocolRoundTripsAdmission()
    {
        var original = new AdmissionRequest("COORD", "Ada");
        var decoded = ProtocolCodec.Deserialize(ProtocolCodec.Serialize(original));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void AdmissionTransitionsAreDeterministic()
    {
        var machine = new AdmissionStateMachine("ROOM", 1);
        Assert.Equal(AdmissionState.Rejected, machine.Admit("a", "Ada", "other").State);
        Assert.Equal(AdmissionState.Waiting, machine.Admit("a", "Ada", "room").State);
        Assert.Equal(AdmissionState.Accepted, machine.Approve("a", true, "approved").State);
        Assert.Equal(AdmissionState.Rejected, machine.Admit("b", "Bob", "ROOM").State);
        Assert.Equal(AdmissionState.Disconnected, machine.Disconnect("a").State);
    }

    [Fact]
    public void UnknownProtocolTypeIsRejected()
    {
        var line = """{"versionMajor":1,"versionMinor":0,"type":"nope","payload":{}}""";
        Assert.Throws<JsonException>(() => ProtocolCodec.Deserialize(line));
    }
}
