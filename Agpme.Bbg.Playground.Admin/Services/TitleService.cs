namespace Agpme.Bbg.Playground.Admin.Services;

public sealed class TitleService
{
    private string _current = string.Empty;
    public string Current => _current;

    public event Action? OnChanged;

    public void Set(string title)
    {
        var newValue = title ?? string.Empty;
        if (!string.Equals(_current, newValue, StringComparison.Ordinal))
        {
            _current = newValue;
            OnChanged?.Invoke();
        }
    }
}