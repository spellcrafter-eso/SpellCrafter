using ReactiveUI;
using ReactiveUI.Avalonia;
using SpellCrafter.ViewModels;

namespace SpellCrafter.Views;

public partial class BrowseView : ReactiveUserControl<BrowseViewModel>
{
    public BrowseView()
    {
        InitializeComponent();
    }
}