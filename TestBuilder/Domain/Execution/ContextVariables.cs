using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace TestBuilder.Domain.Execution;

/// <summary>Branch-owned variables with write tracking, including writes equal to the initial value.</summary>
public sealed class ContextVariables : IDictionary<string, object>, IReadOnlyDictionary<string, object>
{
    private readonly Dictionary<string, object> _values = new();
    internal HashSet<string> WrittenKeys { get; } = new();

    public object this[string key]
    {
        get => _values[key];
        set { _values[key] = value; WrittenKeys.Add(key); }
    }

    public ICollection<string> Keys => _values.Keys;
    public ICollection<object> Values => _values.Values;
    IEnumerable<string> IReadOnlyDictionary<string, object>.Keys => Keys;
    IEnumerable<object> IReadOnlyDictionary<string, object>.Values => Values;
    public int Count => _values.Count;
    public bool IsReadOnly => false;

    internal void Seed(string key, object value) => _values.Add(key, value);
    public void Add(string key, object value) { _values.Add(key, value); WrittenKeys.Add(key); }
    public void Add(KeyValuePair<string, object> item) => Add(item.Key, item.Value);
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value!);
    public bool Remove(string key) { WrittenKeys.Add(key); return _values.Remove(key); }
    public void Clear() { WrittenKeys.UnionWith(_values.Keys); _values.Clear(); }
    public bool Contains(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Contains(item);
    public bool Remove(KeyValuePair<string, object> item) => Contains(item) && Remove(item.Key);
    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, object>>)_values).CopyTo(array, arrayIndex);
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
