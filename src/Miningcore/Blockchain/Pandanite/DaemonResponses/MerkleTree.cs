using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;

namespace Miningcore.Blockchain.Pandanite
{
    public class MerkleTree
    {
        HashTree root;

        public byte[] RootHash { get { return root.hash; } }

        public MerkleTree(List<Transaction> transactions)
        {
            setItems(transactions);
        }

        void setItems(List<Transaction> transactions)
        {
            transactions.Sort((a, b) => a.CalculateHash().AsString().CompareTo(b.CalculateHash().AsString()) * -1);

            var queue = new Queue<HashTree>();

            foreach(var transaction in transactions)
            {
                var hash = transaction.CalculateHash();
                var hashTree = new HashTree(hash);

                queue.Enqueue(hashTree);
            }

            if (queue.Count() % 2 == 1)
            {
                var repeat = new HashTree(queue.Last().hash);
                queue.Enqueue(repeat);
            }

            while (queue.Count() > 1)
            {
                var left = queue.Dequeue();
                var right = queue.Dequeue();
                
                var root = new HashTree();
                root.left = left;
                root.right = right;
                left.parent = root;
                right.parent = root;
                
                using (var sha256 = SHA256.Create())
                {
                    root.hash = sha256.ComputeHash(left.hash.Concat(right.hash).ToArray());
                }

                queue.Enqueue(root);
            }

            root = queue.Dequeue();
        }
    }
}
