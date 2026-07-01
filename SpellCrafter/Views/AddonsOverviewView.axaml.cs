using ReactiveUI;
using ReactiveUI.Avalonia;
using SpellCrafter.ViewModels;

namespace SpellCrafter.Views;

public partial class AddonsOverviewView : ReactiveUserControl<AddonsOverviewViewModel>
{
    public AddonsOverviewView()
    {
        InitializeComponent();
    }
}