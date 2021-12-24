namespace Miningcore.Mining;

public class PoolStartupException : Exception
{
    public PoolStartupException(string msg) : base(msg)
    {
    }

    public PoolStartupException()
    {
    }
}
