using System.Runtime.CompilerServices;

// BlastRenderer (the Pyre assembly) drives the moved stateless modifiers' internal per-frame hooks
// (SetLife / SetSeed / SetFrameIndex / SetOrigin) with direct calls — exactly as it did when these modifiers
// lived inside the Pyre assembly, before the stateless family was elevated into Laubrary.SpriteFx. Granting the
// Pyre assembly friend access keeps those calls compiling and byte-identical, without widening the public API.
// Pyre drives the same hooks by reflection (see PyreRenderer.SetPostContext), so it needs no grant here.
[assembly: InternalsVisibleTo("com.Lautaro-Arino.Laubrary.Pyre")]
