using System.Windows.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace Hamster.Tests;

public sealed class AppTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Services_WhenBuilt_ThenEveryServiceTheWindowNeedsCanBeCreated()
    {
        // Arrange
        var registrations = App.Services(_folder);
        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        // Act
        var failures = registrations.Select(registration => registration.ServiceType).Where(type => type != typeof(MainWindow)).Where(type => Record.Exception(() => services.GetRequiredService(type)) is not null);

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void LinkAt_WhenTextInALinkIsRightClicked_ThenGivesTheLinkAddress()
    {
        // Arrange
        var text = new Run("Markdig");
        _ = new Hyperlink(text) { NavigateUri = new Uri("https://github.com/xoofx/markdig") };

        // Act
        var link = App.LinkAt(text);

        // Assert
        Assert.Equal(new Uri("https://github.com/xoofx/markdig"), link);
    }

    [Fact]
    public void LinkAt_WhenPlainTextIsRightClicked_ThenGivesNothing()
    {
        // Arrange
        var text = new Run("Hej");
        _ = new Paragraph(text);

        // Act
        var link = App.LinkAt(text);

        // Assert
        Assert.Null(link);
    }
}
