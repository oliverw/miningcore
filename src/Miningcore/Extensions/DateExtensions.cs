namespace Miningcore.Extensions;

public static class DateExtensions
{
    public static double ToUnixSeconds(this DateTime dt)
    {
        return ((DateTimeOffset) dt).ToUnixTimeMilliseconds() / 1000d;
    }

    public static double ToUnixSeconds(this DateTimeOffset dto)
    {
        return dto.ToUnixTimeMilliseconds() / 1000d;
    }
}
