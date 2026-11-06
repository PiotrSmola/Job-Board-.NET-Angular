namespace Backend.Helpers;

// Asynchroniczna blokada per klucz (np. znormalizowany e-mail): żądania z tym samym kluczem wykonują się po kolei,
// z różnymi - równolegle. Wpis klucza żyje tylko, dopóki ktoś go trzyma albo na niego czeka (licznik użytkowników),
// więc pamięć nie rośnie z liczbą różnych kluczy. Blokada działa w obrębie jednego procesu.
public sealed class KeyedLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    // liczba kluczy, które ktoś aktualnie trzyma albo na które czeka
    public int ActiveKeys
    {
        get
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.Users++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            // anulowane czekanie - wpis zwalniany bez oddawania semafora, którego nie zajęto
            Leave(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Leave(string key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.Users == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private sealed class Releaser(KeyedLock owner, string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                entry.Semaphore.Release();
                owner.Leave(key, entry);
            }
        }
    }
}
