using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Core.Application.Services;

/// <summary>Owning-dispatcher collection. Ordinary refreshes keep row/container identity;
/// structural churn above 64 displaced positions sends one Reset rather than quadratic moves.</summary>
public sealed class ReconciledCollection<T> : ObservableCollection<T> where T : class
{
    public ReconciledCollection() { }
    public ReconciledCollection(IEnumerable<T> items) : base(items) { }
    public bool Reconcile(IReadOnlyList<T> desired)
    {
        var prefix = 0;
        while (prefix < Count && prefix < desired.Count && ReferenceEquals(this[prefix], desired[prefix])) prefix++;
        if (prefix == Count && desired.Count - Count <= 64) { for (var i = prefix; i < desired.Count; i++) Add(desired[i]); return false; }
        if (prefix == desired.Count && Count - desired.Count <= 64) { while (Count > desired.Count) RemoveAt(Count - 1); return false; }
        var displaced = Math.Abs(Count - desired.Count);
        for (var i = prefix; i < Math.Min(Count, desired.Count) && displaced <= 64; i++)
            if (!ReferenceEquals(this[i], desired[i])) displaced++;
        if (displaced > 64)
        {
            CheckReentrancy();
            Items.Clear(); foreach (var item in desired) Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            return true;
        }
        var wanted = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
        for (var i = Count - 1; i >= prefix; i--) if (!wanted.Contains(this[i])) RemoveAt(i);
        var positions = new Dictionary<T, int>(ReferenceEqualityComparer.Instance);
        for (var i = prefix; i < Count; i++) positions[this[i]] = i;
        for (var i = prefix; i < desired.Count; i++)
        {
            if (i < Count && ReferenceEquals(this[i], desired[i])) continue;
            if (positions.TryGetValue(desired[i], out var old)) Move(old, i); else { old = Count; Insert(i, desired[i]); }
            for (var j = i; j <= Math.Min(old, Count - 1); j++) positions[this[j]] = j;
        }
        return false;
    }
}
