using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Material.Styles.Themes;
using ReactiveUI;
using SpellCrafter.Services;
using SpellCrafter.ViewModels;
using SpellCrafter.Views;
using Splat;

namespace SpellCrafter
{
    public class App : Application
    {
        //public override void Initialize()
        //{
        //    AvaloniaXamlLoader.Load(this);
        //}

        public override void OnFrameworkInitializationCompleted()
        {

            Current!.Resources["MaterialPaperBrush"] = new SolidColorBrush(Color.Parse("#FFFFFF"));

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Locator.CurrentMutable.RegisterConstant<IScreen>(new MainWindowViewModel());
                Locator.CurrentMutable.Register<IViewFor<InstalledAddonsViewModel>>(() => new InstalledAddonsView());
                Locator.CurrentMutable.Register<IViewFor<BrowseViewModel>>(() => new BrowseView());
                Locator.CurrentMutable.Register<IViewFor<SettingsViewModel>>(() => new SettingsView());
                Locator.CurrentMutable.Register<IViewFor<AddonDetailsViewModel>>(() => new AddonDetailsView());

                var mainWindow = new MainWindowView { DataContext = Locator.Current.GetService<IScreen>() };
                StorageProviderService.Initialize(mainWindow);
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();

            var lightPrimaryColor = Color.Parse("#FF204E");
            var midPrimaryColor = Color.Parse("#A0153E");
            var darkPrimaryColor = Color.Parse("#5D0E41");
            var accentColor = Color.Parse("#00224D");

            var theme = Theme.Create(Theme.Dark, midPrimaryColor, accentColor);
            Current!.Resources["MaterialPaperBrush"] = new SolidColorBrush(theme.Paper);

            theme.PrimaryLight = lightPrimaryColor;
            theme.PrimaryDark = darkPrimaryColor;

            var themeBootstrap = this.LocateMaterialTheme<MaterialThemeBase>();
            themeBootstrap.CurrentTheme = theme;
        }
    }
}
