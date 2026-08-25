using System.Runtime.InteropServices;
using System.Text;

namespace Boreas.Interop.Native;

/// <summary>
/// The one place a managed string becomes the NUL-terminated UTF-8 Boreas
/// reads, and the one place that memory is freed.
/// </summary>
/// <remarks>
/// <para>
/// Every string in a <see cref="BoreasConfig"/> is <b>borrowed for the duration
/// of the start call</b> and copied before it returns, so the lifetime this
/// type has to provide is exactly one call. Making it
/// <see cref="IDisposable"/> and scoping it with <c>using</c> states that
/// lifetime in the language rather than in a comment.
/// </para>
/// <para>
/// Unmanaged blocks rather than pinned managed arrays. A filter list is
/// megabytes of text and the pin would sit on the large object heap for the
/// length of a call that blocks for as long as the first connection takes;
/// unmanaged memory has no such effect on collection, and the arena has one
/// owner and one release point.
/// </para>
/// <para>
/// Cost is one allocation per string plus one per pointer array, and
/// <c>O(bytes)</c> to encode. The counts here are a handful of lists and a
/// readable allowlist of hosts, so a bump allocator would buy nothing a
/// profiler could see.
/// </para>
/// </remarks>
internal sealed unsafe class Utf8Block : IDisposable
{
    private readonly List<nint> _blocks = [];

    /// <summary>
    /// Encodes one string, NUL-terminated, or returns null for a null input.
    /// </summary>
    /// <remarks>
    /// Null in, null out is load-bearing rather than convenient: a null
    /// <see cref="BoreasConfig.Resolver"/> means "forward queries untouched",
    /// which is a different tunnel from one pointing at the empty string.
    /// </remarks>
    public byte* Add(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var length = Encoding.UTF8.GetByteCount(value);
        var block = (byte*)NativeMemory.Alloc((nuint)length + 1);
        _blocks.Add((nint)block);

        Encoding.UTF8.GetBytes(value, new Span<byte>(block, length));
        block[length] = 0;

        return block;
    }

    /// <summary>
    /// Encodes a sequence into a <c>const char *const *</c> and its count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count leaves through an out-parameter rather than a tuple
    /// because C# forbids a pointer as a type argument. Keeping the two
    /// together in one call is still worth it: the ABI reads them as a pair,
    /// and a caller that derived the count separately could derive it from a
    /// different sequence than the one it encoded.
    /// </para>
    /// <para>
    /// An empty sequence yields a null array with a zero count, which is what
    /// "no lists" and "no interception" are on the wire. Handing over a
    /// zero-length allocation instead would be a pointer Boreas is entitled to
    /// read nothing from, distinguished from null only by luck.
    /// </para>
    /// </remarks>
    public byte** AddArray(IReadOnlyCollection<string> values, out nuint count)
    {
        count = (nuint)values.Count;

        if (values.Count == 0)
        {
            return null;
        }

        var items = (byte**)NativeMemory.Alloc((nuint)values.Count * (nuint)sizeof(byte*));
        _blocks.Add((nint)items);

        var index = 0;
        foreach (var value in values)
        {
            items[index++] = Add(value);
        }

        return items;
    }

    /// <summary>
    /// Frees every block. Idempotent, because clearing the list is part of it.
    /// </summary>
    public void Dispose()
    {
        foreach (var block in _blocks)
        {
            NativeMemory.Free((void*)block);
        }

        _blocks.Clear();
    }
}
