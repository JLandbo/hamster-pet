using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace Hamster;

public sealed class CharacterChoice(string name, Character? character, ImageSource? preview, string? problem) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;
    public Character? Character { get; private set; } = character;
    public ImageSource? Preview { get; private set; } = preview;
    public string? Problem { get; private set; } = problem;
    public bool Selected { get; private set; }

    public void Update(CharacterChoice fresh, bool selected)
    {
        (Character, Preview, Problem, Selected) = (fresh.Character, fresh.Preview, fresh.Problem, selected);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

public sealed record ThemeChoice(Theme Theme, ResourceDictionary Defaults, bool Selected)
{
    public string Name => Theme.Name;
    public Brush Surface => Theme.BrushOf(nameof(Surface), Defaults);
    public Brush Edge => Theme.BrushOf(nameof(Edge), Defaults);
    public Brush Text => Theme.BrushOf(nameof(Text), Defaults);
    public Brush Muted => Theme.BrushOf(nameof(Muted), Defaults);
    public Brush Attention => Theme.BrushOf(nameof(Attention), Defaults);
}

public partial class SettingsWindow : Window
{
    readonly Action<Translation> chooseLanguage;
    readonly ThemeLibrary themes;
    readonly Action<Theme> chooseTheme;
    readonly CharacterLibrary characters;
    readonly Action<Character> choose;
    readonly Connectors connectors;
    readonly Subscriptions subscriptions;
    readonly ObservableCollection<CharacterChoice> tiles = [];
    readonly ConnectorRow[] rows;
    string chosen;
    string? chosenTheme;

    public SettingsWindow(Action<Translation> chooseLanguage, ThemeLibrary themes, string? chosenTheme, Action<Theme> chooseTheme, CharacterLibrary characters, string chosen,
        Action<Character> choose, Connectors connectors, Subscriptions subscriptions)
    {
        InitializeComponent();
        this.chooseLanguage = chooseLanguage;
        ShowLanguage();
        (this.themes, this.chosenTheme, this.chooseTheme) = (themes, chosenTheme, chooseTheme);
        (this.characters, this.chosen, this.choose, this.connectors, this.subscriptions) = (characters, chosen, choose, connectors, subscriptions);
        rows = [.. Connectors.All.Select(connector => new ConnectorRow(connector, connectors.SourceOf(connector)))];
        CharacterList.ItemsSource = tiles;
        ConnectorList.ItemsSource = rows;
        foreach (var row in rows)
        {
            ShowChoices(row);
            row.ShowLogin(subscriptions.LoginSiteOf(row.Connector), confirmed: null);
            row.SiteInput = subscriptions.KnownSiteOf(row.Connector) is { } site ? new Uri(site).Host : "";
        }
    }

    async void Login_Click(object sender, RoutedEventArgs e)
    {
        var row = (ConnectorRow)((FrameworkElement)sender).DataContext;
        if (row.LoginSite is null)
        {
            if (row.Connector.Login!.SiteFrom(row.SiteInput) is not { } site)
            {
                row.Finish(Strings.Of("Settings.EnterSite"));
                return;
            }
            row.Finish(null);
            subscriptions.Login(this, row.Connector, site);
            row.ShowLogin(subscriptions.LoginSiteOf(row.Connector));
            if (row.LoginSite is not null && row.CanFetchChoices)
                await FetchChoicesAsync(row);
            return;
        }
        try
        {
            await subscriptions.LogoutAsync();
        }
        catch (Exception exception) when (Subscriptions.IsFetchError(exception))
        {
            row.Finish(exception.Message);
        }
        foreach (var other in rows)
            other.ShowLogin(subscriptions.LoginSiteOf(other.Connector));
    }

    void ShowChoices(ConnectorRow row, string? text = null)
    {
        var choices = subscriptions.ChoicesFor(row.Connector);
        var canFetch = row.Connector == Subscriptions.Jira;
        row.ShowChoices(choices, subscriptions.BoardChoicesFor(row.Connector), canFetch, text);
    }

    async void Choice_Click(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        var choice = (Choice)box.DataContext;
        var row = rows.First(row => row.Choices.Contains(choice) || row.BoardChoices.Contains(choice));
        row.Start();
        try
        {
            await subscriptions.ChooseAsync(choice, box.IsChecked == true);
            ShowChoices(row);
            row.Finish(null);
        }
        catch (Exception exception) when (Subscriptions.IsFetchError(exception))
        {
            ShowChoices(row);
            row.Finish(exception.Message);
        }
    }

    async void FetchStatuses_Click(object sender, RoutedEventArgs e) => await FetchChoicesAsync((ConnectorRow)((FrameworkElement)sender).DataContext);

    async Task FetchChoicesAsync(ConnectorRow row)
    {
        row.Start();
        ShowChoices(row, Strings.Of("Common.Fetching"));
        try
        {
            await subscriptions.FetchJiraBoardsAsync();
            ShowChoices(row);
        }
        catch (Exception exception) when (Subscriptions.IsFetchError(exception))
        {
            ShowChoices(row, exception.Message);
        }
        finally
        {
            row.Finish(null);
        }
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        subscriptions.Changed += ShowLoggedOut;
        Closed += (_, _) => subscriptions.Changed -= ShowLoggedOut;
        _ = CheckLoginsAsync();
        while (IsVisible)
        {
            await ShowConnectorsAsync();
            await Task.Delay(Connectors.StatusInterval);
        }
    }

    async Task CheckLoginsAsync()
    {
        foreach (var row in rows.Where(row => row.IsLogin && row.LoginSite is not null))
            await CheckLoginAsync(row);
    }

    async Task CheckLoginAsync(ConnectorRow row)
    {
        row.Start();
        try
        {
            row.ShowLogin(await subscriptions.CheckLoginAsync(row.Connector));
            row.Finish(null);
        }
        catch (Exception exception) when (Subscriptions.IsFetchError(exception))
        {
            row.ShowLogin(row.LoginSite, confirmed: false);
            row.Finish(exception.Message);
        }
    }

    void ShowLoggedOut()
    {
        foreach (var row in rows.Where(row => row.LoginSite is not null && subscriptions.LoginSiteOf(row.Connector) is null))
            row.ShowLogin(null);
    }

    async void Window_Activated(object sender, EventArgs e)
    {
        ShowThemes();
        await ShowCharactersAsync();
    }

    void ShowLanguage() => (DanishButton.IsChecked, EnglishButton.IsChecked) = (Strings.Current == Translation.Danish, Strings.Current == Translation.English);

    void Language_Click(object sender, RoutedEventArgs e)
    {
        chooseLanguage(sender == EnglishButton ? Translation.English : Translation.Danish);
        ShowLanguage();
        foreach (var row in rows)
            ShowChoices(row, row.ChoicesText);
    }

    void ShowThemes()
    {
        Theme[] all = [.. themes.Themes];
        var current = ThemeLibrary.Find(all, chosenTheme).Name;
        ThemeList.ItemsSource = all.Select(theme => new ThemeChoice(theme, ((App)Application.Current).DefaultColors, theme.Name == current)).ToArray();
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        var choice = (ThemeChoice)((FrameworkElement)sender).DataContext;
        chosenTheme = choice.Name;
        chooseTheme(choice.Theme);
        ShowThemes();
    }

    async Task ShowCharactersAsync()
    {
        var fresh = await Task.Run(() => characters.Names.Select(Choice).ToArray());
        for (var i = tiles.Count - 1; i >= 0; i--)
            if (!fresh.Any(choice => choice.Name == tiles[i].Name))
                tiles.RemoveAt(i);
        for (var i = 0; i < fresh.Length; i++)
        {
            if (i == tiles.Count || tiles[i].Name != fresh[i].Name)
                tiles.Insert(i, fresh[i]);
            tiles[i].Update(fresh[i], fresh[i].Name == chosen);
        }
    }

    CharacterChoice Choice(string name)
    {
        try
        {
            var character = characters.Load(name);
            return new(name, character, SpriteRenderer.Render(character.Animations[Mood.Awake][0].Rows, character.Palette), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(name, null, null, exception.Message);
        }
    }

    async void Character_Click(object sender, RoutedEventArgs e)
    {
        var choice = (CharacterChoice)((FrameworkElement)sender).DataContext;
        if (choice.Character is not { } character)
            return;
        chosen = choice.Name;
        choose(character);
        await ShowCharactersAsync();
    }

    async void AddCharacter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await Task.Run(characters.AddCopy);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Hamster", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await ShowCharactersAsync();
    }

    async Task ShowConnectorsAsync()
    {
        try
        {
            var servers = await connectors.ServersAsync();
            await connectors.EnableAsync(servers);
            foreach (var row in rows)
            {
                row.Show(servers, connectors.RestartPending);
                if (!row.ClaudeAiMissing)
                    continue;
                SetSource(row, ConnectorSource.Off);
                row.Finish($"{Connector.MissingOnClaudeAi}.");
            }
            ConnectorProblem.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ObjectDisposedException)
        {
            (ConnectorProblem.Text, ConnectorProblem.Visibility) = (exception.Message, Visibility.Visible);
        }
    }

    void Source_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        SetSource((ConnectorRow)button.DataContext, (ConnectorSource)button.Tag);
    }

    void SetSource(ConnectorRow row, ConnectorSource source)
    {
        if (source != row.Source)
            connectors.SetSource(row.Connector, source);
        row.SetSource(source);
        if (row.IsLogin && row.LoginSite is not null)
            _ = CheckLoginAsync(row);
    }

    void Edit_Click(object sender, RoutedEventArgs e) => ((ConnectorRow)((FrameworkElement)sender).DataContext).Edit();

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        var row = (ConnectorRow)((FrameworkElement)sender).DataContext;
        var values = row.Inputs.Select(input => input.Value.Trim()).ToArray();
        if (values.Any(value => value.Length == 0))
        {
            row.Finish(Strings.Of("Settings.FillInAllFields"));
            return;
        }
        if (!await RunAsync(row, () => connectors.InstallAsync(row.Connector, values, row.AllowWrite)))
            return;
        row.AwaitRestart();
    }

    static async Task<bool> RunAsync(ConnectorRow row, Func<Task> action)
    {
        row.Start();
        try
        {
            await action();
            row.Finish(null);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or IOException or ObjectDisposedException or Win32Exception)
        {
            row.Finish(exception.Message);
            return false;
        }
    }

    void Secret_PasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        ((FieldInput)box.DataContext).Value = box.Password;
    }

    void TokenPage_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
        }
        e.Handled = true;
    }

    void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
