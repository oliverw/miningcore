using Miningcore.Blockchain;
using Miningcore.Stratum;

namespace Miningcore.Mining;

public record StratumShare(StratumConnection Connection, Share Share);
