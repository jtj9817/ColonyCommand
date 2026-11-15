using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// The mod entry. Deliberately empty: Mod constructors run on a loader thread (LoadAllPlayData is queued with
    /// doAsynchronously: true), where Unity objects can't be created, so startup work lives in <see cref="Startup"/>.
    /// No settings (nothing to store), no save data.
    /// </summary>
    public class ColonyCommandMod : Mod
    {
        public ColonyCommandMod(ModContentPack content) : base(content)
        {
        }
    }
}
