namespace Flafyo.Core;

/// <summary>Thread-safe LRU cache keyed by normalized source text.</summary>
public sealed class TranslationCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, string Value)>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, string Value)> _order = new();
    private readonly object _gate = new();

    public TranslationCache(int capacity = 5000)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool TryGet(string key, out string value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node); _order.AddFirst(node);
                value = node.Value.Value; return true;
            }
        }
        value = ""; return false;
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing)) { _order.Remove(existing); _map.Remove(key); }
            var node = _order.AddFirst((key, value));
            _map[key] = node;
            while (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _map.Remove(last.Value.Key); _order.RemoveLast();
            }
        }
    }

    public int Count { get { lock (_gate) return _map.Count; } }
}
