using Miningcore.Api.Responses;

namespace Miningcore.Api.Requests;

public class UpdateMinerSettingsRequest
{
    public string IpAddress { get; set; }
    public MinerSettings Settings { get; set; }
}
