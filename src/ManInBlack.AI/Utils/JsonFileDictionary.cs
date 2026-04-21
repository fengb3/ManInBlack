using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ManInBlack.AI.Utils;

/// <summary>
/// 基于 JSON 文件的持久化字典，所有写操作自动同步到磁盘。
/// </summary>
public class JsonFileDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDisposable
    where TKey : notnull
{
    private readonly string _filePath;
    private Dictionary<TKey, TValue> _dict;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public JsonFileDictionary(string filePath)
    {
        _filePath = filePath;

        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            _dict = JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(json, _jsonOptions) ?? [];
        }
        else
        {
            _dict = new Dictionary<TKey, TValue>();
            Save();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_dict, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        _lock.EnterReadLock();
        try
        {
            return new List<KeyValuePair<TKey, TValue>>(_dict).GetEnumerator();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _dict.Clear();
            Save();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        _lock.EnterReadLock();
        try
        {
            return _dict.TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        _lock.EnterReadLock();
        try
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)_dict).CopyTo(array, arrayIndex);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_dict.TryGetValue(item.Key, out var value) || !EqualityComparer<TValue>.Default.Equals(value, item.Value))
                return false;

            _dict.Remove(item.Key);
            Save();
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _dict.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public bool IsReadOnly => false;

    public void Add(TKey key, TValue value)
    {
        _lock.EnterWriteLock();
        try
        {
            _dict.Add(key, value);
            Save();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool ContainsKey(TKey key)
    {
        _lock.EnterReadLock();
        try { return _dict.ContainsKey(key); }
        finally { _lock.ExitReadLock(); }
    }

    public bool Remove(TKey key)
    {
        _lock.EnterWriteLock();
        try
        {
            var removed = _dict.Remove(key);
            if (removed) Save();
            return removed;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        _lock.EnterReadLock();
        try { return _dict.TryGetValue(key, out value); }
        finally { _lock.ExitReadLock(); }
    }

    public TValue this[TKey key]
    {
        get
        {
            _lock.EnterReadLock();
            try { return _dict[key]; }
            finally { _lock.ExitReadLock(); }
        }
        set
        {
            _lock.EnterWriteLock();
            try { _dict[key] = value; Save(); }
            finally { _lock.ExitWriteLock(); }
        }
    }

    public ICollection<TKey> Keys
    {
        get
        {
            _lock.EnterReadLock();
            try { return new List<TKey>(_dict.Keys); }
            finally { _lock.ExitReadLock(); }
        }
    }

    public ICollection<TValue> Values
    {
        get
        {
            _lock.EnterReadLock();
            try { return new List<TValue>(_dict.Values); }
            finally { _lock.ExitReadLock(); }
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
