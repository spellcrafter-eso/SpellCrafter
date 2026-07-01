using Avalonia.Controls;
using Avalonia.Interactivity;
using ReactiveUI;
using ReactiveUI.Avalonia;
using SpellCrafter.ViewModels;

namespace SpellCrafter.Views;

public partial class AddonDetailsView : ReactiveUserControl<AddonDetailsViewModel>
{
    public AddonDetailsView()
    {
        InitializeComponent();
    }

    private void OpenContextMenu_Click(object? sender, RoutedEventArgs e)
    {
        var button = sender as Control;
        if (button?.ContextMenu == null) return;

        var oldPlacement = button.ContextMenu.Placement;
        button.ContextMenu.Placement = PlacementMode.LeftEdgeAlignedTop;
        button.ContextMenu.Open(button);
        button.ContextMenu.Placement = oldPlacement;
    }
}