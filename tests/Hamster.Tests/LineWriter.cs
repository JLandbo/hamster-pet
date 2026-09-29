using System.Text;
using System.Threading.Channels;

namespace Hamster.Tests;

sealed class LineWriter(bool blocked = false) : TextWriter
{
    readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
    readonly ManualResetEventSlim _open = new(!blocked);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(string? value)
    {
        _open.Wait();
        _lines.Writer.TryWrite(value!.TrimEnd('\n'));
    }

    public void Open() => _open.Set();

    public Task<string> NextAsync() => _lines.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Token);

    protected override void Dispose(bool disposing)
    {
        _open.Set();
        _lines.Writer.TryComplete();
        base.Dispose(disposing);
    }
}
