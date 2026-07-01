using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace SpellCrafter.Tests;

internal static class ReactiveUIModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }
}