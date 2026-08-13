namespace Boreas.Ui.Contracts;

/// <summary>
/// The residue C# cannot rule out.
/// </summary>
/// <remarks>
/// Every closed set in this application is eliminated by a <c>Match</c> method
/// taking one delegate per case, and that is where the real exhaustiveness
/// lives: adding a case changes the method's signature, so every site that
/// renders the set stops compiling until it handles the new one.
///
/// The compiler cannot see that. A record hierarchy with a private constructor
/// is closed in fact but not in the language, and an enum can always hold a
/// value nobody declared, so the switch expressions inside those Match methods
/// still need a final arm. This is that arm: unreachable by construction,
/// loud if construction is ever wrong, and never a silent fallback that
/// returns a plausible-looking default.
/// </remarks>
internal static class Unreachable
{
    public static InvalidOperationException Value<T>(T value) =>
        new($"Unhandled {typeof(T).Name} value: {value}");
}
