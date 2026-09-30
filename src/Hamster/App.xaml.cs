using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Hamster.Core.Chats;
using Hamster.Core.Claude;
using Hamster.Core.Connections;
using Hamster.Core.Feeds;
using Hamster.Core.Languages;
using Hamster.Core.Pet;
using Hamster.Core.Prompts;
using Hamster.Core.Storage;
using Hamster.Desktop;
using Hamster.Themes;
using Hamster.Views;

namespace Hamster;

public partial class App : Application
{
    ResourceDictionary? _theme;
    ResourceDictionary? _translation;
    ServiceProvider? _services;

    public ResourceDictionary DefaultColors => Resources.MergedDictionaries[0];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = Services(DataFolder.Current).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var pet = _services.GetRequiredService<DataFiles>().Pet.Load();
        Use(Translation.All.FirstOrDefault(translation => translation.Name == pet.LanguageName) ?? Translation.Danish);
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    internal static ServiceCollection Services(string folder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new DataFiles(folder));
        services.AddSingleton(provider =>
        {
            var files = provider.GetRequiredService<DataFiles>();
            return new ClaudeClient(files.Workspace, files.Instructions, files.Settings);
        });
        services.AddSingleton(provider =>
        {
            var (files, claude) = (provider.GetRequiredService<DataFiles>(), provider.GetRequiredService<ClaudeClient>());
            if (!files.ChatOnly)
            {
                SavedChats.Migrate(files.Folder, claude.Settings.WorkingDirectory);
            }
            return new Conversation(claude, files.Chats(claude.Settings.WorkingDirectory));
        });
        services.AddSingleton(provider =>
        {
            var conversation = provider.GetRequiredService<Conversation>();
            return new Connectors(provider.GetRequiredService<ClaudeClient>(), conversation.Restart, () => conversation.RestartPending);
        });
        services.AddSingleton(provider => new WebSession(provider.GetRequiredService<DataFiles>().WebLogin));
        services.AddSingleton<IWebSession>(provider => provider.GetRequiredService<WebSession>());
        services.AddSingleton(provider => new ClaudeFetcher(provider.GetRequiredService<DataFiles>().Workspace));
        services.AddSingleton(provider => provider.GetRequiredService<DataFiles>().Store("subscriptions.json", SubscriptionSettings.Empty));
        services.AddSingleton<Subscriptions>();
        services.AddSingleton(provider =>
        {
            var subscriptions = provider.GetRequiredService<Subscriptions>();
            return new Feed([(Subscriptions.JiraSource, subscriptions.FetchJiraAsync), (Subscriptions.GitHubSource, subscriptions.FetchGitHubAsync)]);
        });
        services.AddSingleton(provider => new CharacterLibrary(provider.GetRequiredService<DataFiles>().Characters));
        services.AddSingleton(provider => new PromptLibrary(provider.GetRequiredService<DataFiles>().Prompts));
        services.AddSingleton(provider => new ThemeLibrary(provider.GetRequiredService<DataFiles>().Themes));
        services.AddSingleton<SystemVolume>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    public void Use(Theme next) => Swap(ref _theme, next.ToResources());

    public void Use(Translation next)
    {
        Swap(ref _translation, ResourcesOf(next));
        Strings.Use(next);
    }

    static ResourceDictionary ResourcesOf(Translation translation)
    {
        var resources = new ResourceDictionary();
        foreach (var key in Translation.Danish.Texts.Keys)
        {
            resources[key] = translation.Of(key);
        }
        return resources;
    }

    void Swap(ref ResourceDictionary? current, ResourceDictionary next)
    {
        if (current is not null)
        {
            Resources.MergedDictionaries.Remove(current);
        }
        current = next;
        Resources.MergedDictionaries.Add(next);
    }

    void Reader_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var copyLink = ((FrameworkElement)sender).ContextMenu.Items.OfType<MenuItem>().Last();
        copyLink.Tag = LinkAt(e.OriginalSource as DependencyObject);
        copyLink.Visibility = copyLink.Tag is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void MenuItem_PreviewMouseRightButton(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void Reader_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject == sender)
        {
            e.Handled = true;
        }
    }

    internal static Uri? LinkAt(DependencyObject? element) => element switch
    {
        null => null,
        Hyperlink link => link.NavigateUri,
        _ => LinkAt(LogicalTreeHelper.GetParent(element)),
    };

    void CopyLink_Click(object sender, RoutedEventArgs e) => ClipboardText.Copy(((Uri)((MenuItem)sender).Tag).AbsoluteUri, _ => { });
}
