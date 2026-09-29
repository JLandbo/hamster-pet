namespace Hamster.Tests;

public sealed class ChatOwnerTests
{
    readonly string _file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

    [Fact]
    public void TryOwn_WhenAnotherHamsterOwnsThePlace_ThenFails()
    {
        // Arrange
        using var owner = new ChatOwner();
        owner.TryOwn(_file);

        // Act
        var owned = OwnOnAnotherThread(_file);

        // Assert
        Assert.False(owned);
    }

    [Fact]
    public void TryOwn_WhenTheOwnerLeftThePlace_ThenSucceeds()
    {
        // Arrange
        using var owner = new ChatOwner();
        owner.TryOwn(_file);
        owner.TryOwn($"{_file}.andet");

        // Act
        var owned = OwnOnAnotherThread(_file);

        // Assert
        Assert.True(owned);
    }

    [Fact]
    public void TryOwn_WhenTheOwnerDiedWithoutLeaving_ThenSucceeds()
    {
        // Arrange
        var thread = new Thread(() => new ChatOwner().TryOwn(_file));
        thread.Start();
        thread.Join();

        // Act
        using var owner = new ChatOwner();
        var owned = owner.TryOwn(_file);

        // Assert
        Assert.True(owned);
    }

    [Fact]
    public void TryOwn_WhenTheNewPlaceIsTaken_ThenStillLeavesTheOldOne()
    {
        // Arrange
        using var owner = new ChatOwner();
        owner.TryOwn(_file);
        using var other = new HeldPlace($"{_file}.andet");

        // Act
        var ownedNew = owner.TryOwn($"{_file}.andet");

        // Assert
        Assert.Equal((false, true), (ownedNew, OwnOnAnotherThread(_file)));
    }

    sealed class HeldPlace : IDisposable
    {
        readonly ManualResetEventSlim _leave = new();
        readonly Thread _thread;

        public HeldPlace(string file)
        {
            using var held = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                using var owner = new ChatOwner();
                owner.TryOwn(file);
                held.Set();
                _leave.Wait();
            });
            _thread.Start();
            held.Wait();
        }

        public void Dispose()
        {
            _leave.Set();
            _thread.Join();
            _leave.Dispose();
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
