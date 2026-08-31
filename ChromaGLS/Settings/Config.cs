using System.Runtime.CompilerServices;
using IPA.Config.Stores;
using JetBrains.Annotations;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace ChromaGLS.Settings
{
    // The setting is persisted by BSIPA so the startup gate can decide whether any ChromaGLS Harmony patches are installed.
    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    internal class Config
    {
        public virtual bool Enabled { get; set; } = true;
    }
}
