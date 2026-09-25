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

public partial class SettingsWindow : Window
{
    static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);

    readonly CharacterLibrary characters;
    readonly Action<Character> choose;
    readonly Connectors connectors;
    readonly ObservableCollection<CharacterChoice> tiles = [];
    readonly ConnectorRow[] rows;
    string chosen;

    public SettingsWindow(CharacterLibrary characters, string chosen, Action<Character> choose, Connectors connectors)
    {
        InitializeComponent();
        (this.characters, this.chosen, this.choose, this.connectors) = (characters, chosen, choose, connectors);
        rows = [.. Connectors.All.Select(connector => new ConnectorRow(connector, connectors.UsesClaudeAi(connector)))];
        CharacterList.ItemsSource = tiles;
        ConnectorList.ItemsSource = rows;
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        while (IsVisible)
        {
            await ShowConnectorsAsync();
            await Task.Delay(StatusInterval);
        }
    }

    async void Window_Activated(object sender, EventArgs e) => await ShowCharactersAsync();

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
            foreach (var row in rows)
            {
                row.Show(servers, connectors.RestartPending);
                if (!row.ClaudeAiMissing)
                    continue;
                UseClaudeAi(row, false);
                row.Finish("Ikke fundet på claude.ai.");
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

    async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        var (row, enabled) = ((ConnectorRow)toggle.DataContext, toggle.IsChecked == true);
        await RunAsync(row, () => connectors.ToggleAsync(row.Server!, enabled));
        await ShowConnectorsAsync();
    }

    void ClaudeAi_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        UseClaudeAi((ConnectorRow)toggle.DataContext, toggle.IsChecked == true);
    }

    void UseClaudeAi(ConnectorRow row, bool use)
    {
        connectors.UseClaudeAi(row.Connector, use);
        row.UseClaudeAi(use);
    }

    void Edit_Click(object sender, RoutedEventArgs e) => ((ConnectorRow)((FrameworkElement)sender).DataContext).Edit();

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        var row = (ConnectorRow)((FrameworkElement)sender).DataContext;
        var values = row.Inputs.Select(input => input.Value.Trim()).ToArray();
        if (values.Any(value => value.Length == 0))
        {
            row.Finish("Udfyld alle felter.");
            return;
        }
        if (await RunAsync(row, () => connectors.InstallAsync(row.Connector, values, row.AllowWrite)))
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
