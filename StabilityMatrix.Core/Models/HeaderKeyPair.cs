using System.ComponentModel;

namespace StabilityMatrix.Core.Models;

public class HeaderKeyPair : INotifyPropertyChanged
{
    private string key = string.Empty;
    private string value = string.Empty;

    public string Key
    {
        get => key;
        set
        {
            if (key == value) return;
            key = value;
            OnPropertyChanged(nameof(Key));
        }
    }

    public string Value
    {
        get => this.value;
        set
        {
            if (this.value == value) return;
            this.value = value;
            OnPropertyChanged(nameof(Value));
        }
    }

    public HeaderKeyPair(string key = "", string value = "")
    {
        this.key = key;
        this.value = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

