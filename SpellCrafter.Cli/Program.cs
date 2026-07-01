using System;
using System.IO;
using System.Threading.Tasks;

namespace SpellCrafter.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            return await CliCommandRunner.RunAsync(args, Console.Out, Console.Error);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation was canceled.");
            return CliExitCodes.Canceled;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return CliExitCodes.SystemError;
        }
    }
}