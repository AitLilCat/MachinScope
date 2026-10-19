using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Core.Testing;
public static class TestAssert
{
    public static void True(bool value) => Assert.IsTrue(value);
    public static void False(bool value) => Assert.IsFalse(value);
    public static void Null(object? value) => Assert.IsNull(value);
    public static void Equal<T>(T expected, T actual) => Assert.AreEqual(expected, actual);
    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual) => CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
    public static void NotEqual<T>(T expected, T actual) => Assert.AreNotEqual(expected, actual);
    public static void InRange(double actual, double min, double max) => Assert.IsTrue(actual >= min && actual <= max);
    public static void Contains<T>(IEnumerable<T> values, Predicate<T> match) => Assert.IsTrue(values.Any(v => match(v)), "Expected matching entry.");
    public static void Contains(string expected, string actual) => StringAssert.Contains(actual, expected);
    public static void NotEmpty<T>(IEnumerable<T> values) => Assert.IsTrue(values.Any());
    public static T Single<T>(IEnumerable<T> values) { var array = values.ToArray(); Assert.AreEqual(1, array.Length); return array[0]; }
    public static void All<T>(IEnumerable<T> values, Action<T> action) { foreach (var v in values) action(v); }
    public static void Throws<T>(Action action) where T : Exception => Assert.ThrowsExactly<T>(action);
    public static async Task ThrowsAnyAsync<T>(Func<Task> action) where T : Exception => await Assert.ThrowsAsync<T>(action);
}
