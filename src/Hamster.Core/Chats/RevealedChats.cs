using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Hamster.Core.Chats;

public sealed class RevealedChats
{
    readonly ObservableCollection<ChatItem> _chats;
    int _oldest = int.MaxValue;

    public RevealedChats(ObservableCollection<ChatItem> chats)
    {
        _chats = chats;
        chats.CollectionChanged += (_, change) =>
        {
            if (change.Action == NotifyCollectionChangedAction.Remove && change.OldStartingIndex < _oldest)
            {
                _oldest--;
            }
        };
    }

    int Oldest => Math.Min(_oldest, _chats.Count - 1);

    public void StartFromNewest() => _oldest = int.MaxValue;

    public bool Contains(int index) => index >= Oldest;

    public bool RevealOlder()
    {
        if (Oldest <= 0)
        {
            return false;
        }
        _oldest = Oldest - 1;
        return true;
    }
}
