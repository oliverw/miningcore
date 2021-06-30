namespace Miningcore.DaemonInterface
{
    public class DaemonCmd
    {
        public DaemonCmd()
        {
        }

        public DaemonCmd(string method)
        {
            Method = method;
        }

        public DaemonCmd(string method, object payload)
        {
            Method = method;
            Payload = payload;
        }

        public string Method { get; set; }
        public object Payload { get; set; }
    }
}
