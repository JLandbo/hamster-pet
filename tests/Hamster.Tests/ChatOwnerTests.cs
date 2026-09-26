namespace Hamster.Tests;

public sealed class ChatOwnerTests
{
    readonly string file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

    [Fact]
    public void TryOwn_WhenAnotherHamsterOwnsThePlace_ThenFails()
    {
        // Arrange
        using var owner = new ChatOwner();
        owner.TryOwn(file);

        // Act
        var owned = OwnOnAnotherThread(file);

        // Assert
        Assert.False(owned);
    }

    [Fact]
    public void TryOwn_WhenTheOwnerLeftThePlace_ThenSucceeds()
    {
        // Arrange
        using var owner = new ChatOwner();
        owner.TryOwn(file);
        owner.TryOwn($"{file}.andet");

        // Act
        var owned = OwnOnAnotherThread(file);

        // Assert
        Assert.True(owned);
    }

    [Fact]
    public void TryOwn_WhenTheOwnerDiedWithoutLeaving_ThenSucceeds()
    {
        // Arrange
        var thread = new Thread(() => new ChatOwner().TryOwn(file));
        thread.Start();
        thread.Join();

        // Act
        using var owner = new ChatOwner();
        var owned = owner.TryOwn(file);

        // Assert
        Assert.True(owned);
    }

    [Fact]
    public void TryOwn_WhenTheNewPlaceIsTaken_ThenStillLeavesTheOldOne()
    {
        // Arrange
        using var owner = new ChatOwner();
        owner.TryOwn(file);
        using var other = new HeldPlace($"{file}.andet");

        // Act
        var ownedNew = owner.TryOwn($"{file}.andet");

        // Assert
        Assert.Equal((false, true), (ownedNew, OwnOnAnotherThread(file)));
    }

    sealed class HeldPlace : IDisposable
    {
        readonly ManualResetEventSlim leave = new();
        readonly Thread thread;

        public HeldPlace(string file)
        {
            using var held = new ManualResetEventSlim();
            thread = new Thread(() =>
            {
                using var owner = new ChatOwner();
                owner.TryOwn(file);
                held.Set();
                leave.Wait();
            });
            thread.Start();
            held.Wait();
        }

        public void Dispose()
        {
            leave.Set();
            thread.Join();
            leave.Dispose();
        }
    }

    static bool OwnOnAnotherThread(string file)
    {
        var owned = false;
        var thread = new Thread(() =>
        {
            using var other = new ChatOwner();
            owned = other.TryOwn(file);
        });
        thread.Start();
        thread.Join();
        return owned;
    }
}
