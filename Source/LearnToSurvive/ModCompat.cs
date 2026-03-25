using System.Linq;
using Verse;

namespace LearnToSurvive
{
    public static class ModCompat
    {
        public static bool PUAHLoaded { get; private set; }
        public static bool CommonSenseLoaded { get; private set; }
        public static bool CombatExtendedLoaded { get; private set; }

        public static void Detect()
        {
            PUAHLoaded = ModsConfig.ActiveModsInLoadOrder.Any(
                m => m.PackageIdPlayerFacing != null &&
                     m.PackageIdPlayerFacing.ToLower().Contains("pickupandhaul"));

            CommonSenseLoaded = ModsConfig.ActiveModsInLoadOrder.Any(
                m => m.PackageIdPlayerFacing != null &&
                     m.PackageIdPlayerFacing.ToLower().Contains("commonsense"));

            CombatExtendedLoaded = ModsConfig.ActiveModsInLoadOrder.Any(
                m => m.PackageIdPlayerFacing != null &&
                     m.PackageIdPlayerFacing.ToLower().Contains("combatextended"));

            if (PUAHLoaded)
                Log.Message("[LearnToSurvive] Detected Pick Up And Haul - adjusting hauling behavior overlap.");
            if (CommonSenseLoaded)
                Log.Message("[LearnToSurvive] Detected Common Sense - adjusting work behavior overlap.");
            if (CombatExtendedLoaded)
                Log.Message("[LearnToSurvive] Detected Combat Extended - adjusting combat behavior.");
        }
    }
}
