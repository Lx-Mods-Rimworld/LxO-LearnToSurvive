using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            ModCompat.Detect();

            var harmony = new Harmony("Lexxers.LearnToSurvive");

            try
            {
                // Auto-patch all classes with [HarmonyPatch] attributes
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[LearnToSurvive] Attribute-based patches applied.");
            }
            catch (Exception ex)
            {
                Log.Error("[LearnToSurvive] Attribute patches failed: " + ex);
            }

            // Manual patches for non-public methods
            ApplyManualPatches(harmony);

            // Add CompIntelligence to all humanlike defs via C# (catches HAR races
            // that the XML patch might miss)
            RegisterComp();

            if (LTSSettings.logLevel > LogLevel.Off)
            {
                LTSLog.Initialize();
            }

            // Add ITab to all humanlike pawns via C# (ensures it's always last)
            RegisterInspectorTab();

            Log.Message("[LearnToSurvive] Initialization complete. " +
                "Hauling=" + LTSSettings.enableHaulingSense +
                " Work=" + LTSSettings.enableWorkAwareness +
                " Path=" + LTSSettings.enablePathMemory +
                " Combat=" + LTSSettings.enableCombatInstinct +
                " SelfPres=" + LTSSettings.enableSelfPreservation +
                " PUAH=" + ModCompat.PUAHLoaded +
                " CS=" + ModCompat.CommonSenseLoaded +
                " CE=" + ModCompat.CombatExtendedLoaded);
        }

        private static void ApplyManualPatches(Harmony harmony)
        {
            // --- Path Memory patches ---
            TryPatch(harmony, "PathFollower_Track",
                AccessTools.Method(typeof(Pawn_PathFollower), "PatherTick"),
                postfix: typeof(Patch_PathFollower_Track).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));

            TryPatch(harmony, "PathCost",
                AccessTools.Method(typeof(PathGrid), "CalculatedCostAt"),
                postfix: typeof(Patch_PathCost).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));

            // FindPath - search across all PathFinder types (1.6 split into WalkPathFinder, etc.)
            MethodInfo findPathMethod = null;
            string[] pathFinderTypeNames = {
                "Verse.PathFinder", "Verse.AI.PathFinder",
                "Verse.WalkPathFinder", "Verse.SwimPathFinder", "Verse.SimplePathFinder"
            };
            string[] methodNames = { "FindPath", "FindPathNow" };

            foreach (string typeName in pathFinderTypeNames)
            {
                Type pfType = AccessTools.TypeByName(typeName);
                if (pfType == null) continue;

                foreach (string methodName in methodNames)
                {
                    var methods = AccessTools.GetDeclaredMethods(pfType)
                        .FindAll(m => m.Name == methodName);
                    foreach (var m in methods)
                    {
                        var parms = m.GetParameters();
                        if (parms.Any(p => p.ParameterType == typeof(TraverseParms)))
                        {
                            findPathMethod = m;
                            Log.Message("[LearnToSurvive] Found pathfinder method: " + typeName + "." + methodName);
                            break;
                        }
                    }
                    if (findPathMethod != null) break;
                }
                if (findPathMethod != null) break;
            }

            if (findPathMethod != null)
            {
                TryPatch(harmony, "FindPath_SetPawn",
                    findPathMethod,
                    prefix: typeof(Patch_FindPath_SetPawn).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static),
                    postfix: typeof(Patch_FindPath_SetPawn).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));
            }
            else
            {
                Log.Warning("[LearnToSurvive] Could not find any PathFinder.FindPath method. Path memory cost modifiers disabled. This is expected on RimWorld 1.6+ if pathfinding was restructured.");
            }

            // --- Hauling proximity patch ---
            var haulWorkThings = AccessTools.Method(typeof(WorkGiver_Scanner), "PotentialWorkThingsGlobal");
            TryPatch(harmony, "HaulWorkThings_Proximity",
                haulWorkThings,
                postfix: typeof(Patch_HaulWorkThings_Proximity).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));

            // --- Construction batch delivery ---
            var constructDeliver = AccessTools.Method(
                typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor");
            TryPatch(harmony, "ConstructDeliverResources",
                constructDeliver,
                postfix: typeof(Patch_ConstructDeliverResources).GetMethod("Postfix",
                    BindingFlags.Public | BindingFlags.Static));

            // --- Combat patches ---
            TryPatch(harmony, "PawnTick_CombatXP",
                AccessTools.Method(typeof(Pawn), "Tick"),
                postfix: typeof(Patch_PawnTick_CombatXP).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));

            TryPatch(harmony, "FriendlyFire",
                AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot"),
                prefix: typeof(Patch_FriendlyFire).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static));

            // --- Work patches ---
            TryPatch(harmony, "DoBill_PreClean",
                AccessTools.Method(typeof(JobDriver_DoBill), "MakeNewToils"),
                postfix: typeof(Patch_DoBill_PreClean).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));
        }

        private static void TryPatch(Harmony harmony, string name, MethodInfo original,
            MethodInfo prefix = null, MethodInfo postfix = null)
        {
            if (original == null)
            {
                Log.Warning("[LearnToSurvive] Could not find method for patch: " + name + ". Skipping.");
                return;
            }

            try
            {
                HarmonyMethod pre = prefix != null ? new HarmonyMethod(prefix) : null;
                HarmonyMethod post = postfix != null ? new HarmonyMethod(postfix) : null;
                harmony.Patch(original, pre, post);
                Log.Message("[LearnToSurvive] Manual patch applied: " + name);
            }
            catch (Exception ex)
            {
                Log.Warning("[LearnToSurvive] Failed to apply patch " + name + ": " + ex.Message);
            }
        }

        private static void RegisterComp()
        {
            int count = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.race == null || def.race.intelligence != Intelligence.Humanlike) continue;
                if (def.comps == null)
                {
                    def.comps = new List<CompProperties>();
                    Log.Message("[LearnToSurvive] Created comps list for: " + def.defName);
                }
                if (def.comps.Any(c => c is CompProperties_Intelligence)) continue;

                def.comps.Add(new CompProperties_Intelligence());
                count++;
            }
            Log.Message("[LearnToSurvive] CompIntelligence added via C# to " + count + " humanlike ThingDefs.");
        }

        private static void RegisterInspectorTab()
        {
            int count = 0;
            int skippedAlready = 0;
            var tabType = typeof(ITab_Intelligence);

            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.race == null || def.race.intelligence != Intelligence.Humanlike) continue;

                // If inspectorTabs is null, initialize it (HAR races may not have it)
                if (def.inspectorTabs == null)
                {
                    def.inspectorTabs = new List<Type>();
                    Log.Message("[LearnToSurvive] Created inspectorTabs list for: " + def.defName);
                }

                if (def.inspectorTabs.Contains(tabType))
                {
                    skippedAlready++;
                    continue;
                }

                def.inspectorTabs.Add(tabType);

                // Also add to the resolved list
                if (def.inspectorTabsResolved == null)
                    def.inspectorTabsResolved = new List<InspectTabBase>();

                var instance = InspectTabManager.GetSharedInstance(tabType);
                if (instance != null && !def.inspectorTabsResolved.Contains(instance))
                    def.inspectorTabsResolved.Add(instance);

                count++;
                Log.Message("[LearnToSurvive] Added Intelligence tab to: " + def.defName
                    + " (type=" + def.GetType().Name + ")");
            }
            Log.Message("[LearnToSurvive] Intelligence tab added to " + count
                + " ThingDefs. Already had it: " + skippedAlready);
        }
    }

    /// <summary>
    /// Ensure MapComponents are added to new/loaded maps.
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
    public static class Patch_MapInit
    {
        public static void Postfix(Map __instance)
        {
            if (__instance.GetComponent<MapComponent_CombatIntelligence>() == null)
                __instance.components.Add(new MapComponent_CombatIntelligence(__instance));
            if (__instance.GetComponent<MapComponent_MoodManagement>() == null)
                __instance.components.Add(new MapComponent_MoodManagement(__instance));
        }
    }
}
