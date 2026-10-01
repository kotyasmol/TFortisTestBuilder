using System;
using System.Collections;
using System.Collections.Generic;

namespace TestBuilder.Domain.Execution;

/// <summary>Copies supported value containers without sharing mutable data across branches.</summary>
internal static class ParallelVariableSnapshot
{
    public static object Clone(object value) => Clone(value, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static object Clone(object value, HashSet<object> ancestors)
    {
        if (value == null || IsImmutable(value))
            return value!;
        if (!ancestors.Add(value))
            throw new InvalidOperationException("Циклические значения переменных нельзя использовать в параллельных ветвях.");
        try
        {
            var type = value.GetType();
            if (value is Array array && array.Rank == 1 && array.GetLowerBound(0) == 0)
            {
                var copy = (Array)array.Clone();
                for (var index = 0; index < array.Length; index++)
                    copy.SetValue(Clone(array.GetValue(index)!, ancestors), index);
                return copy;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var comparer = type.GetProperty("Comparer")!.GetValue(value);
                var copy = (IDictionary)Activator.CreateInstance(type, comparer)!;
                foreach (DictionaryEntry pair in (IDictionary)value)
                    copy.Add(Clone(pair.Key, ancestors), Clone(pair.Value!, ancestors));
                return copy;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var copy = (IList)Activator.CreateInstance(type)!;
                foreach (var item in (IList)value)
                    copy.Add(Clone(item!, ancestors));
                return copy;
            }
            throw new InvalidOperationException(
                $"Тип переменной '{type.Name}' не поддерживает изоляцию параллельных ветвей. " +
                "Используйте скаляр, массив, List или Dictionary.");
        }
        finally
        {
            ancestors.Remove(value);
        }
    }

    private static bool IsImmutable(object value) => value.GetType().IsPrimitive || value.GetType().IsEnum ||
        value is string or decimal or DateTime or DateTimeOffset or TimeSpan or Guid or Uri or Version;

    public static bool AreEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.GetType() != right.GetType()) return false;
        if (left is IDictionary firstDictionary && right is IDictionary secondDictionary)
        {
            if (firstDictionary.Count != secondDictionary.Count) return false;
            foreach (DictionaryEntry pair in firstDictionary)
                if (!secondDictionary.Contains(pair.Key) || !AreEqual(pair.Value, secondDictionary[pair.Key])) return false;
            return true;
        }
        if (left is IList firstList && right is IList secondList)
        {
            if (firstList.Count != secondList.Count) return false;
            for (var index = 0; index < firstList.Count; index++)
                if (!AreEqual(firstList[index], secondList[index])) return false;
            return true;
        }
        return left.Equals(right);
    }
}
