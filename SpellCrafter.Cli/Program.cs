using System;
using System.IO;
using System.Threading.Tasks;
using ReactiveUI.Builder;

namespace SpellCrafter.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Initialize ReactiveUI for CLI usage (normally done by Avalonia app builder)
        try
        {
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to initialize ReactiveUI: {ex.Message}");
            return CliExitCodes.SystemError;
        }

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