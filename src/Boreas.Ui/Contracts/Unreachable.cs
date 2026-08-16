using System.Diagnostics;

namespace Boreas.Ui.Contracts;

/// <summary>
/// The switch arm C# cannot prove unreachable.
/// </summary>
/// <remarks>
/// C# does not prove private record hierarchies or enum matches exhaustive, so
/// <c>Match</c> methods need a final arm. Throwing <see cref="UnreachableException"/>
/// keeps an invariant violation loud instead of returning a fallback value.
/// </remarks>
internal static class Unreachable
{
    public static UnreachableException Value<T>(T value) =>
        new($"Unhandled {typeof(T).Name} value: {value}");
}
