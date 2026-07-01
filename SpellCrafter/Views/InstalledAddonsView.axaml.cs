using ReactiveUI;
using ReactiveUI.Avalonia;
using SpellCrafter.ViewModels;

namespace SpellCrafter.Views;

public partial class InstalledAddonsView : ReactiveUserControl<InstalledAddonsViewModel>
{
    public InstalledAddonsView()
    {
        InitializeComponent();
    }
}