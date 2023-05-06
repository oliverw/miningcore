namespace Miningcore.Blockchain.Pandanite
{
    public class HashTree
    {
        public HashTree()
        {
            this.hash = new byte[] { 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 };
        }

        public HashTree(byte[] hash)
        {
            this.hash = hash;
        }

        public byte[] hash { get; set; }
        public HashTree parent { get; set; }
        public HashTree left { get; set; }
        public HashTree right { get; set; }
    }
}
