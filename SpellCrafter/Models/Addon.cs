﻿using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SpellCrafter.Enums;
using SpellCrafter.Messages;
using SpellCrafter.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace SpellCrafter.Models
{
    public class Addon
    {
        private const string baseDownloadLink = "https://www.esoui.com/downloads";
        
        public List<CommonAddon> Dependencies { get; set; } = new();
        public int CommonAddonId { get; set; } = -1;
        [Reactive] public string Name { get; set; } = string.Empty;
        [Reactive] public string Description { get; set; } = string.Empty;
        [Reactive] public AddonState AddonState { get; set; } = AddonState.NotInstalled;
        [Reactive] public AddonInstallationMethod InstallationMethod { get; set; } = AddonInstallationMethod.Other;
        [Reactive] public string Downloads { get; set; } = string.Empty;
        [Reactive] public ObservableCollection<Category> Categories { get; set; } = new();
        [Reactive] public ObservableCollection<Author> Authors { get; set; } = new();
        [Reactive] public string UniqueIdentifier { get; set; } = string.Empty;
        [Reactive] public string FileSize { get; set; } = string.Empty;
        [Reactive] public string Overview { get; set; } = string.Empty;
        [Reactive] public string Version { get; set; } = string.Empty;
        [Reactive] public string DisplayedVersion { get; set; } = string.Empty;
        [Reactive] public string LatestVersion { get; set; } = string.Empty;
        [Reactive] public string DisplayedLatestVersion { get; set; } = string.Empty;
        [Reactive] public string GameVersion { get; set; } = string.Empty;

        public RelayCommand ViewModCommand { get; }
        public RelayCommand InstallCommand { get; }
        public RelayCommand ReinstallCommand { get; }
        public RelayCommand UpdateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand ViewWebsiteCommand { get; }
        public RelayCommand CopyLinkCommand { get; }
        public RelayCommand BrowseFolderCommand { get; }

        public Addon()
        {
            ViewModCommand = new RelayCommand(_ => ViewMod());
            InstallCommand = new RelayCommand(
                _ => Install(),
                _ => AddonState == AddonState.NotInstalled
            );
            ReinstallCommand = new RelayCommand(
                _ => Reinstall(),
                _ => AddonState != AddonState.NotInstalled
            );
            UpdateCommand = new RelayCommand(
                _ => Update(),
                _ => AddonState == AddonState.Outdated || AddonState == AddonState.UpdateError
            );
            DeleteCommand = new RelayCommand(
                _ => Delete(),
                _ => AddonState != AddonState.NotInstalled
            );
            ViewWebsiteCommand = new RelayCommand(_ => ViewWebsite());
            CopyLinkCommand = new RelayCommand(_ => CopyLink());
            BrowseFolderCommand = new RelayCommand(
                _ => BrowseFolder(),
                _ => AddonState != AddonState.NotInstalled
            );
        }

        private void ViewMod()
        {
            Debug.WriteLine("ViewMod!");

            MessageBus.Current.SendMessage(new AddonUpdatedMessage(this));
        }

        private void Install()
        {
            Debug.WriteLine($"InstallMod! {Name}");
        }

        private void Reinstall()
        {
            Debug.WriteLine($"Reinstall! {Name}");
        }

        private void Update()
        {
            Debug.WriteLine("Update!");
        }

        private void Delete()
        {
            Debug.WriteLine("Delete!");
        }

        private void ViewWebsite()
        {
            Debug.WriteLine("ViewWebsite!");
        }

        private void CopyLink()
        {
            Debug.WriteLine("CopyLink!");
        }

        private void BrowseFolder()
        {
            Debug.WriteLine("BrowseFolder!");
        }
    }
}