using SpellCrafter.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace SpellCrafter.Views;

public partial class SettingsView : ReactiveUserControl<SettingsViewModel>
{
    public SettingsView()
    {
        InitializeComponent();

        var vm = new SettingsViewModel();
        DataContext = vm;
    }
}