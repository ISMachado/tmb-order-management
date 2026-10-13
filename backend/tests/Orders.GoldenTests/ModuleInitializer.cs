using System.Runtime.CompilerServices;

namespace Orders.GoldenTests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        // Comparacao deterministica e sem abrir ferramentas de diff graficas (CI e terminal).
        DiffEngine.DiffRunner.Disabled = true;
        VerifierSettings.SortJsonObjects();
        VerifierSettings.ScrubInlineGuids();
    }
}
