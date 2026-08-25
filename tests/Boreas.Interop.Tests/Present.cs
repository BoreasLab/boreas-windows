namespace Boreas.Interop.Tests;

/// <summary>
/// Asserts a smart constructor succeeded and hands back what it made.
/// </summary>
/// <remarks>
/// xunit's <c>Assert.NotNull</c> returns void for a reference type, so the
/// value has to be recovered afterwards. Doing that with <c>!</c> at every call
/// site would put a null-forgiving operator in the one kind of test whose
/// subject is whether the value is there.
/// </remarks>
internal static class Present
{
    public static T Value<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }
}
