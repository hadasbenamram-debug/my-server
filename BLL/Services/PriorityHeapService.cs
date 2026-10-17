using System;
using System.Collections.Generic;

namespace BLL.Algorithms
{
    public class HeapItem<T>
    {
        public T Value { get; set; }
        public double Priority { get; set; }

        public HeapItem(T value, double priority)
        {
            Value = value;
            Priority = priority;
        }
    }

    /// <summary>
    /// Max-Heap עם אינדקס מפתחות (עדכון/הסרה ב-O(log n)).
    ///
    /// שינויים לעומת הגרסה המקורית:
    ///  * שובר שוויון דטרמיניסטי (Comparison אופציונלי) – בעבר תרומות עם אותו ציון יצאו בסדר שרירותי.
    ///  * LoadOrUpdateAll מטפל במפתחות כפולים (בעבר נוצרו שני צמתים לאותו מפתח והערימה נהרסה).
    ///  * מנוע השידוכים משתמש במופע מקומי לכל ריצה (לא ב-Singleton משותף): ה-Singleton החזיק
    ///    ישויות EF מ-DbContext שכבר נסגר, לא התנקה בין ריצות, ונכתב אליו במקביל מ-Job הדעיכה.
    /// </summary>
    public class PriorityHeapService<T, TKey> where T : class where TKey : notnull
    {
        private readonly List<HeapItem<T>> _heap = new List<HeapItem<T>>();
        private readonly Dictionary<TKey, int> _itemIndices = new Dictionary<TKey, int>();
        private readonly Func<T, TKey> _keySelector;
        private readonly Comparison<T>? _tieBreaker;
        private readonly object _lock = new object();

        /// <param name="keySelector">מפתח ייחודי לפריט (למשל d =&gt; d.Id)</param>
        /// <param name="tieBreaker">כשהעדיפויות שוות: ערך שלילי = a קודם ל-b</param>
        public PriorityHeapService(Func<T, TKey> keySelector, Comparison<T>? tieBreaker = null)
        {
            _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
            _tieBreaker = tieBreaker;
        }

        public int Count
        {
            get { lock (_lock) { return _heap.Count; } }
        }

        public bool Contains(T item)
        {
            lock (_lock) { return _itemIndices.ContainsKey(_keySelector(item)); }
        }

        /// <summary>איפוס וטעינה מחדש של הערימה (Heapify ב-O(n)).</summary>
        public void LoadOrUpdateAll(IEnumerable<(T Item, double Priority)> items)
        {
            lock (_lock)
            {
                _heap.Clear();
                _itemIndices.Clear();

                foreach (var (item, priority) in items)
                {
                    var key = _keySelector(item);
                    if (_itemIndices.TryGetValue(key, out int existing))
                    {
                        _heap[existing].Value = item;
                        _heap[existing].Priority = priority;
                        continue;
                    }

                    _heap.Add(new HeapItem<T>(item, priority));
                    _itemIndices[key] = _heap.Count - 1;
                }

                for (int i = (_heap.Count / 2) - 1; i >= 0; i--)
                {
                    BubbleDown(i);
                }
            }
        }

        public void Insert(T item, double priority)
        {
            lock (_lock)
            {
                TKey key = _keySelector(item);
                if (_itemIndices.ContainsKey(key))
                {
                    UpdatePriorityInternal(item, priority);
                    return;
                }

                _heap.Add(new HeapItem<T>(item, priority));
                int currentIndex = _heap.Count - 1;
                _itemIndices[key] = currentIndex;

                BubbleUp(currentIndex);
            }
        }

        public T? ExtractMax()
        {
            lock (_lock)
            {
                if (_heap.Count == 0) return null;

                var maxVal = _heap[0].Value;
                _itemIndices.Remove(_keySelector(maxVal));

                int lastIndex = _heap.Count - 1;

                if (lastIndex > 0)
                {
                    _heap[0] = _heap[lastIndex];
                    _itemIndices[_keySelector(_heap[0].Value)] = 0;
                }

                _heap.RemoveAt(lastIndex);

                if (_heap.Count > 0)
                {
                    BubbleDown(0);
                }

                return maxVal;
            }
        }

        public T? PeekMax()
        {
            lock (_lock)
            {
                return _heap.Count > 0 ? _heap[0].Value : null;
            }
        }

        public void Remove(T item)
        {
            lock (_lock)
            {
                TKey key = _keySelector(item);
                if (!_itemIndices.TryGetValue(key, out int index)) return;

                _itemIndices.Remove(key);
                int lastIndex = _heap.Count - 1;

                if (index == lastIndex)
                {
                    _heap.RemoveAt(lastIndex);
                    return;
                }

                _heap[index] = _heap[lastIndex];
                _itemIndices[_keySelector(_heap[index].Value)] = index;
                _heap.RemoveAt(lastIndex);

                BubbleUp(index);
                BubbleDown(index);
            }
        }

        /// <summary>מעדכן עדיפות; אם הפריט לא קיים — מוסיף אותו (התנהגות מקורית).</summary>
        public void UpdatePriority(T item, double newPriority)
        {
            lock (_lock)
            {
                UpdatePriorityInternal(item, newPriority);
            }
        }

        private void UpdatePriorityInternal(T item, double newPriority)
        {
            TKey key = _keySelector(item);
            if (!_itemIndices.TryGetValue(key, out int index))
            {
                _heap.Add(new HeapItem<T>(item, newPriority));
                int newIndex = _heap.Count - 1;
                _itemIndices[key] = newIndex;
                BubbleUp(newIndex);
                return;
            }

            _heap[index].Value = item;
            _heap[index].Priority = newPriority;

            // אחד משני הכיוונים לא יזוז – זה בסדר, וכך גם שובר שוויון שהשתנה מטופל נכון
            BubbleUp(index);
            BubbleDown(index);
        }

        private bool IsHigher(HeapItem<T> a, HeapItem<T> b)
        {
            int byPriority = a.Priority.CompareTo(b.Priority);
            if (byPriority != 0) return byPriority > 0;
            return _tieBreaker != null && _tieBreaker(a.Value, b.Value) < 0;
        }

        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (IsHigher(_heap[index], _heap[parentIndex]))
                {
                    Swap(index, parentIndex);
                    index = parentIndex;
                }
                else
                {
                    break;
                }
            }
        }

        private void BubbleDown(int index)
        {
            int lastIndex = _heap.Count - 1;
            while (true)
            {
                int leftChild = 2 * index + 1;
                int rightChild = 2 * index + 2;
                int largest = index;

                if (leftChild <= lastIndex && IsHigher(_heap[leftChild], _heap[largest]))
                    largest = leftChild;

                if (rightChild <= lastIndex && IsHigher(_heap[rightChild], _heap[largest]))
                    largest = rightChild;

                if (largest == index) break;

                Swap(index, largest);
                index = largest;
            }
        }

        private void Swap(int i, int j)
        {
            var temp = _heap[i];
            _heap[i] = _heap[j];
            _heap[j] = temp;

            _itemIndices[_keySelector(_heap[i].Value)] = i;
            _itemIndices[_keySelector(_heap[j].Value)] = j;
        }
    }
}
