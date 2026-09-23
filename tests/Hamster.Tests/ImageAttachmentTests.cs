namespace Hamster.Tests;

public sealed class ImageAttachmentTests : IDisposable
{
    readonly string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())).FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void FromFile_WhenImage_ThenReadsItWithMediaType()
    {
        // Arrange
        var path = Path.Combine(directory, "Skærm.JPG");
        File.WriteAllBytes(path, [1, 2, 3]);

        // Act
        var image = ImageAttachment.FromFile(path);

        // Assert
        Assert.Equal(("Skærm.JPG", "image/jpeg"), (image!.Name, image.MediaType));
        Assert.Equal([1, 2, 3], image.Data);
    }

    [Fact]
    public void FromFile_WhenNotAnImage_ThenNull()
    {
        // Arrange
        var path = Path.Combine(directory, "noter.txt");
        File.WriteAllText(path, "hej");

        // Act
        var image = ImageAttachment.FromFile(path);

        // Assert
        Assert.Null(image);
    }
}
