using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Inkitty.App.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can add, remove and replace many items with one
/// change notification, so a list showing hundreds of rows updates once rather than per row.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnCountChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void InsertRange(int index, IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        CheckReentrancy();
        for (var i = 0; i < items.Count; i++)
        {
            Items.Insert(index + i, items[i]);
        }

        OnCountChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList(), index));
    }

    public void RemoveRange(int index, int count)
    {
        if (count == 0)
        {
            return;
        }

        CheckReentrancy();
        var removed = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            removed.Add(Items[index]);
            Items.RemoveAt(index);
        }

        OnCountChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }

    private void OnCountChanged()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
