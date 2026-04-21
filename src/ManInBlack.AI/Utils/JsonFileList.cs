using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ManInBlack.AI.Utils;

/// <summary>
/// 基于 JSON 文件的持久化列表，所有写操作自动同步到磁盘。
/// </summary>
public class JsonFileList<T> : IList<T>, IDisposable
{
    private readonly string _filePath;
    private List<T> _list;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public JsonFileList(string filePath)
    {
        _filePath = filePath;

        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            _list = JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? [];
        }
        else
        {
            _list = [];
            Save();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_list, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public IEnumerator<T> GetEnumerator()
    {
        _lock.EnterReadLock();
        try
        {
            return new List<T>(_list).GetEnumerator();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(T item)
    {
        _lock.EnterWriteLock();
        try { _list.Add(item); Save(); }
        finally { _lock.ExitWriteLock(); }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try { _list.Clear(); Save(); }
        finally { _lock.ExitWriteLock(); }
    }

    public bool Contains(T item)
    {
        _lock.EnterReadLock();
        try { return _list.Contains(item); }
        finally { _lock.ExitReadLock(); }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _lock.EnterReadLock();
        try { _list.CopyTo(array, arrayIndex); }
        finally { _lock.ExitReadLock(); }
    }

    public bool Remove(T item)
    {
        _lock.EnterWriteLock();
        try
        {
            var removed = _list.Remove(item);
            if (removed) Save();
            return removed;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _list.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public bool IsReadOnly => false;

    public int IndexOf(T item)
    {
        _lock.EnterReadLock();
        try { return _list.IndexOf(item); }
        finally { _lock.ExitReadLock(); }
    }

    public void Insert(int index, T item)
    {
        _lock.EnterWriteLock();
        try { _list.Insert(index, item); Save(); }
        finally { _lock.ExitWriteLock(); }
    }

    public void RemoveAt(int index)
    {
        _lock.EnterWriteLock();
        try { _list.RemoveAt(index); Save(); }
        finally { _lock.ExitWriteLock(); }
    }

    public T this[int index]
    {
        get
        {
            _lock.EnterReadLock();
            try { return _list[index]; }
            finally { _lock.ExitReadLock(); }
        }
        set
        {
            _lock.EnterWriteLock();
            try { _list[index] = value; Save(); }
            finally { _lock.ExitWriteLock(); }
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
