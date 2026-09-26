using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Hamster;

public sealed class ChatOwner : IDisposable
{
    Mutex? held;

    public bool TryOwn(string file)
    {
        Release();
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, $@"Local\Hamster-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..32]}");
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
        }
        held = mutex;
        return true;
    }

    public void Dispose() => Release();

    void Release()
    {
        held?.ReleaseMutex();
        held?.Dispose();
        held = null;
    }
}
