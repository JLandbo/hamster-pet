using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Hamster.Core.Connections;
using Hamster.Core.Feeds;
using Hamster.Core.Languages;
using Hamster.Core.Pet;
using Hamster.Desktop;
using Hamster.Rendering;
using Hamster.Themes;

namespace Hamster.Views;

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
    readonly Action<Translation> _chooseLanguage;
    readonly Action<int> _chooseHideSeconds;
    readonly Action<int> _chooseLastResponseSeconds;
    readonly Action<bool> _chooseWebImages;
    readonly Action<bool> _choosePartialMessages;
    readonly ThemeLibrary _themes;
    readonly Action<Theme> _chooseTheme;
    readonly CharacterLibrary _characters;
    readonly Action<Character> _chooseCharacter;
    readonly WebSession _web;
    readonly Connectors _connectors;
    readonly Subscriptions _subscriptions;
    readonly ObservableCollection<CharacterChoice> _tiles = [];
    readonly ConnectorRow[] _rows;
    string _chosenCharacter;
    string? _chosenTheme;
    bool _partialMessagesAwaitingRestart;

    public SettingsWindow(Action<Translation> chooseLanguage, int hideSeconds, Action<int> chooseHideSeconds, int lastResponseSeconds, Action<int> chooseLastResponseSeconds, bool webImages, Action<bool> chooseWebImages, bool partialMessages, Action<bool> choosePartialMessages, ThemeLibrary themes, string? chosenTheme, Action<Theme> chooseTheme, CharacterLibrary characters, string chosenCharacter,
        Action<Character> chooseCharacter, Connectors connectors, Subscriptions subscriptions, WebSession web)
    {
        InitializeComponent();
        (_chooseLanguage, _chooseHideSeconds, _chooseLastResponseSeconds) = (chooseLanguage, chooseHideSeconds, chooseLastResponseSeconds);
        ShowLanguage();
        HideSecondsSlider.Value = hideSeconds;
        ShowHideSeconds();
        HideSecondsSlider.ValueChanged += (_, _) => ShowHideSeconds();
        LastResponseSecondsSlider.Value = lastResponseSeconds;
        ShowLastResponseSeconds();
        LastResponseSecondsSlider.ValueChanged += (_, _) => ShowLastResponseSeconds();
        (WebImagesBox.IsChecked, _chooseWebImages) = (webImages, chooseWebImages);
        (PartialMessagesBox.IsChecked, _choosePartialMessages) = (partialMessages, choosePartialMessages);
        (_themes, _chosenTheme, _chooseTheme) = (themes, chosenTheme, chooseTheme);
        (_characters, _chosenCharacter, _chooseCharacter, _connectors, _subscriptions, _web) = (characters, chosenCharacter, chooseCharacter, connectors, subscriptions, web);
        _rows = [.. Connectors.All.Select(connector => new ConnectorRow(connector, connectors.SourceOf(connector)))];
        CharacterList.ItemsSource = _tiles;
        ConnectorList.ItemsSource = _rows;
        foreach (var row in _rows)
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
            var login = new LoginWindow(_web, row.Connector, site) { Owner = this };
            login.ShowDialog();
            if (login.Site is { } loggedIn)
            {
                _subscriptions.RememberLogin(row.Connector, loggedIn);
            }
            row.ShowLogin(_subscriptions.LoginSiteOf(row.Connector));
            if (row.LoginSite is not null && row.CanFetchChoices)
            {
                await FetchChoicesAsync(row);
            }
            return;
        }
        try
        {
            await _subscriptions.LogoutAsync();
        }
        catch (Exception exception) when (Subscriptions.IsFetchError(exception))
        {
            row.Finish(exception.Message);
        }
        foreach (var other in _rows)
        {
            other.ShowLogin(_subscriptions.LoginSiteOf(other.Connector));
        }
    }

    void ShowChoices(ConnectorRow row, string? text = null)
    {
        var choices = _subscriptions.ChoicesFor(row.Connector);
        var canFetch = row.Connector == Subscriptions.Jira;
        row.ShowChoices(choices, _subscriptions.BoardChoicesFor(row.Connector), canFetch, text);
    }

    async void Choice_Click(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        var choice = (Choice)box.DataContext;
        var row = _rows.First(row => row.Choices.Contains(choice) || row.BoardChoices.Contains(choice));
        row.Start();
        try
        {
            await _subscriptions.ChooseAsync(choice, box.IsChecked == true);
            ShowChoices(row);
            row.Finish(null);
        }
        catch (Exception exception) when (Subscriptions.IsFetchError(exception))
        {
            ShowChoices(row);
            row.Finish(exception.Message);
        }
    }

    async void FetchChoices_Click(object sender, RoutedEventArgs e) => await FetchChoicesAsync((ConnectorRow)((FrameworkElement)sender).DataContext);

    async Task FetchChoicesAsync(ConnectorRow row)
    {
        row.Start();
        ShowChoices(row, Strings.Of("Common.Fetching"));
        try
        {
            await _subscriptions.FetchJiraBoardsAsync();
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
        _subscriptions.Changed += ShowLoggedOut;
        Closed += (_, _) => _subscriptions.Changed -= ShowLoggedOut;
        _ = CheckLoginsAsync();
        while (IsVisible)
        {
            ShowPartialMessagesRestart();
            await ShowConnectorsAsync();
            await Task.Delay(Connectors.StatusInterval);
        }
    }

    async Task CheckLoginsAsync()
    {
        foreach (var row in _rows.Where(row => row.IsLogin && row.LoginSite is not null))
        {
            await CheckLoginAsync(row);
        }
    }

    async Task CheckLoginAsync(ConnectorRow row)
    {
        row.Start();
        try
        {
            row.ShowLogin(await _subscriptions.CheckLoginAsync(row.Connector));
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
        foreach (var row in _rows.Where(row => row.LoginSite is not null && _subscriptions.LoginSiteOf(row.Connector) is null))
        {
            row.ShowLogin(null);
        }
    }

    async void Window_Activated(object sender, EventArgs e)
    {
        ShowPartialMessagesRestart();
        ShowThemes();
        await ShowCharactersAsync();
    }

    void ShowLanguage() => (DanishButton.IsChecked, EnglishButton.IsChecked) = (Strings.Current == Translation.Danish, Strings.Current == Translation.English);

    void ShowHideSeconds() => HideSecondsText.Text = $"{HideSecondsSlider.Value} s";

    void HideSecondsSlider_LostMouseCapture(object sender, MouseEventArgs e) => _chooseHideSeconds((int)HideSecondsSlider.Value);

    void ShowLastResponseSeconds() => LastResponseSecondsText.Text = $"{LastResponseSecondsSlider.Value} s";

    void LastResponseSecondsSlider_LostMouseCapture(object sender, MouseEventArgs e) => _chooseLastResponseSeconds((int)LastResponseSecondsSlider.Value);

    void WebImages_Click(object sender, RoutedEventArgs e) => _chooseWebImages(WebImagesBox.IsChecked == true);

    void PartialMessages_Click(object sender, RoutedEventArgs e)
    {
        _choosePartialMessages(PartialMessagesBox.IsChecked == true);
        _partialMessagesAwaitingRestart = _connectors.RestartPending;
        ShowPartialMessagesRestart();
    }

    void ShowPartialMessagesRestart()
    {
        _partialMessagesAwaitingRestart &= _connectors.RestartPending;
        PartialMessagesRestartText.Visibility = _partialMessagesAwaitingRestart ? Visibility.Visible : Visibility.Collapsed;
    }

    void Language_Click(object sender, RoutedEventArgs e)
    {
        _chooseLanguage(sender == EnglishButton ? Translation.English : Translation.Danish);
        ShowLanguage();
        foreach (var row in _rows)
        {
            ShowChoices(row, row.ChoicesText);
        }
    }

    void ShowThemes()
    {
        Theme[] all = [.. _themes.Themes];
        var current = ThemeLibrary.Find(all, _chosenTheme).Name;
        ThemeList.ItemsSource = all.Select(theme => new ThemeChoice(theme, ((App)Application.Current).DefaultColors, theme.Name == current)).ToArray();
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        var choice = (ThemeChoice)((FrameworkElement)sender).DataContext;
        _chosenTheme = choice.Name;
        _chooseTheme(choice.Theme);
        ShowThemes();
    }

    async Task ShowCharactersAsync()
    {
        var fresh = await Task.Run(() => _characters.Names.Select(CharacterChoiceOf).ToArray());
        for (var i = _tiles.Count - 1; i >= 0; i--)
        {
            if (!fresh.Any(choice => choice.Name == _tiles[i].Name))
            {
                _tiles.RemoveAt(i);
            }
        }
        for (var i = 0; i < fresh.Length; i++)
        {
            if (i == _tiles.Count || _tiles[i].Name != fresh[i].Name)
            {
                _tiles.Insert(i, fresh[i]);
            }
            _tiles[i].Update(fresh[i], fresh[i].Name == _chosenCharacter);
        }
    }

    CharacterChoice CharacterChoiceOf(string name)
    {
        try
        {
            var character = _characters.Load(name);
            // Decodes every sheet, so a broken one shows here instead of silently falling back to the hamster when chosen.
            return new(name, character, SpriteRenderer.Render([character.Animations[Mood.Awake][0], .. character.Animations.Values.Select(frames => frames[0])])[0], null);
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
        {
            return;
        }
        _chosenCharacter = choice.Name;
        _chooseCharacter(character);
        await ShowCharactersAsync();
    }

    void OpenCharacters_Click(object sender, RoutedEventArgs e) => OpenFolder(_characters.Folder);

    void OpenThemes_Click(object sender, RoutedEventArgs e) => OpenFolder(_themes.Folder);

    void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Directory.CreateDirectory(folder).FullName) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warning.Show(this, exception.Message);
        }
    }

    async void AddCharacter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await Task.Run(_characters.AddCopy);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warning.Show(this, exception.Message);
        }
        await ShowCharactersAsync();
    }

    async Task ShowConnectorsAsync()
    {
        try
        {
            var servers = await _connectors.ServersAsync();
            await _connectors.EnableAsync(servers);
            foreach (var row in _rows)
            {
                row.Show(servers, _connectors.RestartPending);
                if (!row.ClaudeAiMissing)
                {
                    continue;
                }
                SetSource(row, ConnectorSource.Off);
                row.Finish($"{Connector.MissingOnClaudeAi}.");
            }
            ConnectorProblemText.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ObjectDisposedException)
        {
            (ConnectorProblemText.Text, ConnectorProblemText.Visibility) = (exception.Message, Visibility.Visible);
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
        {
            _connectors.SetSource(row.Connector, source);
        }
        row.SetSource(source);
        if (row.IsLogin && row.LoginSite is not null)
        {
            _ = CheckLoginAsync(row);
        }
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
        if (!await RunAsync(row, () => _connectors.InstallAsync(row.Connector, values, row.AllowWrite)))
        {
            return;
        }
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
