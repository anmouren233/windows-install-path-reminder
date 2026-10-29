using System;
using System.Threading;

internal static class FakeSetup
{
    private static void Main(string[] args)
    {
        int seconds = args.Length == 0 ? 12 : Int32.Parse(args[0]);
        Thread.Sleep(seconds * 1000);
    }
}
