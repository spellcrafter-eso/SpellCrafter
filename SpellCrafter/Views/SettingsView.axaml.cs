using ReactiveUI;
using ReactiveUI.Avalonia;

namespace SpellCrafter.Views;

public partial class SettingsView : ReactiveUserControl<ViewModels.SettingsViewModel>
{
    public SettingsView()
    {
        InitializeComponent();
    }
}