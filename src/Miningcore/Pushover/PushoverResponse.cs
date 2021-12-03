namespace Miningcore.Pushover;

public class PushoverReponse
{
    public int Status { get; set; }
    public string Request { get; set; }
    public string User { get; set; }
    public string[] Errors { get; set; }
}
