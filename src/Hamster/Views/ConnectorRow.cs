namespace Hamster.Views;

public sealed class FieldInput(ConnectorField field)
{
    public ConnectorField Field { get; } = field;
    public string Value { get; set; } = "";
}

public sealed class ConnectorRow(Connector connector, ConnectorSource source = ConnectorSource.Hamster) : INotifyPropertyChanged
{
    int claudeAiMisses;
    bool restartPending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Connector Connector { get; } = connector;
    public IReadOnlyList<FieldInput> Inputs { get; private set; } = NewInputs(connector);
    public McpServer? Server { get; private set; }
    public bool Known { get; private set; }
    public bool Idle { get; private set; } = true;
    public bool Editing { get; private set; }
    public bool Restarting { get; private set; }
    public bool AllowWrite { get; set; }
    public ConnectorSource Source { get; private set; } = source;
    public string? Problem { get; private set; }
    public IReadOnlyList<Choice> Choices { get; private set; } = [];
    public IReadOnlyList<Choice> BoardChoices { get; private set; } = [];
    public bool CanFetchChoices { get; private set; }
    public string? ChoicesText { get; private set; }
    public string? LoginSite { get; private set; }
    public bool? LoginConfirmed { get; private set; }
    public string SiteInput { get; set; } = "";
    public bool IsOff => Source == ConnectorSource.Off;
    public bool IsHamster => Source == ConnectorSource.Hamster;
    public bool IsClaudeAi => Source == ConnectorSource.ClaudeAi;
    public bool IsLogin => Source == ConnectorSource.Login;
    public bool NeedsSite => IsLogin && LoginSite is null;
    public string LoginLabel => LoginSite is null ? Strings.Of("Settings.LogIn") : Strings.Of("Settings.LogOut");
    public bool Installed => Server?.IsUsable == true;
    public bool CanInstall => Known && IsHamster && Connector.Installable && !Installed && !Restarting && !restartPending;
    public bool ShowForm => CanInstall || Editing;
    public bool CanEdit => IsHamster && Connector.Installable && Installed && Server?.Name == Connector.Name;
    public bool HasChoices => !IsOff && (Choices.Count > 0 || CanFetchChoices);
    public bool ClaudeAiMissing => claudeAiMisses >= Connectors.ClaudeAiChecks;

    public string State => IsOff ? Strings.Of("Settings.TurnedOff")
        : IsLogin ? LoginSite is null ? Strings.Of("Settings.Site") : LoginConfirmed switch
        {
            true => Strings.Format("Settings.LoggedIn", new Uri(LoginSite).Host),
            null => Strings.Format("Settings.CheckingLogin", new Uri(LoginSite).Host),
            false => Strings.Format("Settings.LoginCheckFailed", new Uri(LoginSite).Host),
        }
        : !Known ? Strings.Of("Common.Fetching")
        : Server is null && restartPending ? Strings.Of("Settings.SwitchesAfterRestart")
        : Restarting && !Installed ? Strings.Of("Settings.SavedAfterRestart")
        : Server is null ? IsClaudeAi ? Connector.MissingOnClaudeAi : Strings.Of("Settings.NotInstalled")
        : Server.Status switch
        {
            "connected" => IsClaudeAi ? Strings.Of("Settings.ConnectedViaClaudeAi") : Strings.Of("Settings.ConnectedWithToken"),
            "needs-auth" => IsClaudeAi ? Strings.Of("Settings.LogInOnClaudeAi") : Strings.Of("Settings.TokenRejected"),
            _ => Connector.StateOf(Server),
        };

    public void Show(IReadOnlyList<McpServer> servers, bool restartPending = false)
    {
        (Server, Known, this.restartPending) = (IsOff ? null : Connector.FindIn(servers, IsClaudeAi), true, restartPending);
        Restarting &= !Installed && Server?.Name != Connector.Name;
        claudeAiMisses = IsClaudeAi && Server is null && !restartPending ? claudeAiMisses + 1 : 0;
        Changed();
    }

    public void SetSource(ConnectorSource source)
    {
        if (source != Source)
            (Source, Editing, claudeAiMisses, Problem, Known, Server) = (source, false, 0, null, false, null);
        Changed();
    }

    public void ShowChoices(IReadOnlyList<Choice> choices, IReadOnlyList<Choice> boardChoices, bool canFetch, string? text)
    {
        (Choices, BoardChoices, CanFetchChoices, ChoicesText) = (choices, boardChoices, canFetch, text);
        Changed();
    }

    public void ShowLogin(string? site, bool? confirmed = true)
    {
        (LoginSite, LoginConfirmed) = (site, confirmed);
        Changed();
    }

    public void Edit()
    {
        Editing = !Editing;
        Changed();
    }

    public void Start()
    {
        (Idle, Problem) = (false, null);
        Changed();
    }

    public void Finish(string? problem)
    {
        (Idle, Problem) = (true, problem);
        Changed();
    }

    public void AwaitRestart()
    {
        (Restarting, Editing, AllowWrite, Inputs) = (true, false, false, NewInputs(Connector));
        Changed();
    }

    static FieldInput[] NewInputs(Connector connector) => [.. connector.Fields.Select(field => new FieldInput(field))];

    void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
