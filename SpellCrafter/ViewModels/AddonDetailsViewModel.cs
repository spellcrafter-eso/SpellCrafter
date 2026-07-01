using System;
using ReactiveUI;
using SpellCrafter.Models;
using Splat;

namespace SpellCrafter.ViewModels;

public class AddonDetailsViewModel : ViewModelBase, IRoutableViewModel
{
    public Addon Addon { get; }

    public string? UrlPathSegment => "/details";

    public IScreen HostScreen { get; }

    public AddonDetailsViewModel(Addon addon, IScreen? screen = null)
    {
        Addon = addon ?? throw new ArgumentNullException(nameof(addon));
        HostScreen = screen ?? Locator.Current.GetService<IScreen>()!;
    }
}