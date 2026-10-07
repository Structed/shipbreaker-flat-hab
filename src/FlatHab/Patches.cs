using BBI.Unity.Game;
using HarmonyLib;

namespace FlatHab
{
    [HarmonyPatch(typeof(Hab3DController), "Awake")]
    internal static class Hab3DControllerAwakePatch
    {
        private static void Postfix(Hab3DController __instance) => HabMenu.RegisterController(__instance);
    }

    // The hidden 3D location UI must not react to keyboard/controller input while the flat menu owns it.
    [HarmonyPatch(typeof(Hab3DLocationNavigation), "Update")]
    internal static class Hab3DLocationNavigationUpdatePatch
    {
        private static bool Prefix(Hab3DLocationNavigation __instance) => !HabMenu.ShouldSuppressLocationInput(__instance);
    }
}
