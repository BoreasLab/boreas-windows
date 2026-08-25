using System.Runtime.CompilerServices;

// The arena and the raw declarations are internal so that nothing outside this
// assembly can drop a status or leak a native block. The laws still have to
// reach them, and this is the narrower of the two ways to allow that: the
// alternative is widening the surface itself, which would let production code
// do what only a test should.
[assembly: InternalsVisibleTo("Boreas.Interop.Tests")]
