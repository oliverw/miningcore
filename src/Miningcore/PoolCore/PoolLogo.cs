/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.PoolCore
{
    class PoolLogo
    {


        public static void Logo()
        {
            Console.WriteLine($@"
 ███╗   ███╗██╗███╗   ██╗██╗███╗   ██╗ ██████╗  ██████╗ ██████╗ ██████╗ ███████╗
 ████╗ ████║██║████╗  ██║██║████╗  ██║██╔════╝ ██╔════╝██╔═══██╗██╔══██╗██╔════╝
 ██╔████╔██║██║██╔██╗ ██║██║██╔██╗ ██║██║  ███╗██║     ██║   ██║██████╔╝█████╗
 ██║╚██╔╝██║██║██║╚██╗██║██║██║╚██╗██║██║   ██║██║     ██║   ██║██╔══██╗██╔══╝
 ██║ ╚═╝ ██║██║██║ ╚████║██║██║ ╚████║╚██████╔╝╚██████╗╚██████╔╝██║  ██║███████╗
");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($" MININGCORE - making mining easy");
            Console.WriteLine($" https://github.com/minernl/miningcore\n");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($" Part off all donation goes to the core developers");
            Console.WriteLine($" If you want to donate to them yourself:\n");
            Console.WriteLine($" BTC  - 3QT2WreQtanPHcMneg9LT2aH3s5nrSZsxr");
            Console.WriteLine($" LTC  - LTVnLEv8Xj6emGbf981nTyN54Mnyjbfgrg");
            Console.WriteLine($" DASH - Xc2vm9SfRn8t1hyQgqi8Zrt3oFeGcQtwTh");
            Console.WriteLine($" ETH  - 0xBfD360CDd9014Bc5B348B65cBf79F78381694f4E");
            Console.WriteLine($" ETC  - 0xF4BFFC324bbeB63348F137B84f8d1Ade17B507E4");
            Console.WriteLine($" UMA  - 0x10c42769a8a07421C168c19612A434A72D460d08");
            Console.WriteLine($" XLM  - GDQP2KPQGKIHYJGXNUIYOMHARUARCA7DJT5FO2FFOOKY3B2WSQHG4W37:::ucl:::864367071");
            Console.WriteLine($" XMR  - 44riGcQcDp4EsboDJP284CFCnJ2qP7y8DAqGC4D9WtVbEqzxQ3qYXAUST57u5FkrVF7CXhsEc63QNWazJ5b9ygwBJBtB2kT");
            Console.WriteLine($" XPR  - rw2ciyaNshpHe7bCHo4bRWq6pqqynnWKQg:::ucl:::2242232925");
            Console.WriteLine($" ZEC  - t1JtJtxTdgXCaYm1wzRfMRkGTJM4qLcm4FQ");
            Console.WriteLine();
            Console.ResetColor();
        }


    }
}
