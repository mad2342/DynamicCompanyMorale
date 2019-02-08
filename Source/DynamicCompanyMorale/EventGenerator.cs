using Harmony;
using BattleTech;

namespace DynamicCompanyMorale
{
    class EventGenerator
    {
        internal static int EventAppliedCount = 0;

        [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
        public static class SimGameState_OnDayPassed_Patch
        {
            public static bool Prepare()
            {
                return DynamicCompanyMorale.EnableEventGenerator;
            }

            public static void Postfix(SimGameState __instance, SimGameEventTracker ___companyEventTracker)
            {
                Logger.LogLine("[SimGameState_OnDayPassed_POSTFIX] ForceEvent");

                void ActivateTestEvents()
                {
                    Pilot plt = null;
                    SimGameForcedEvent evt = new SimGameForcedEvent
                    {
                        Scope = EventScope.Company,
                        EventID = "event_mw_moraleTest",
                        MinDaysWait = 5,
                        MaxDaysWait = 10,
                        Probability = 100,
                        RetainPilot = false
                    };
                    if (EventAppliedCount < 3)
                    {
                        __instance.AddSpecialEvent(evt, plt);
                        EventAppliedCount++;
                    }
                }

                //ActivateTestEvents();

                float randomRoll = __instance.NetworkRandom.Float(0f, 100f);
                if (randomRoll > 50f)
                {
                    ___companyEventTracker.ActivateRandomEvent();
                }

                Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
        }
    }
}
