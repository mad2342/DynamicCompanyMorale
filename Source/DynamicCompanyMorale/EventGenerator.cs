using Harmony;
using BattleTech;

namespace DynamicCompanyMorale
{
    class EventGenerator
    {
        /*
        [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
        public static class SimGameState_OnDayPassed_Patch
        {
            public static void Postfix(SimGameState __instance, SimGameEventTracker ___companyEventTracker)
            {
                Logger.LogLine("[SimGameState_OnDayPassed_POSTFIX] ForceEvent");

                Pilot plt = null;
                SimGameForcedEvent evt = new SimGameForcedEvent
                {
                    Scope = EventScope.Company,
                    EventID = "event_mw_moraleTest",
                    MinDaysWait = 3,
                    MaxDaysWait = 5,
                    Probability = 100,
                    RetainPilot = false
                };
                __instance.AddSpecialEvent(evt, plt);

                //float randomRoll = __instance.NetworkRandom.Float(0f, 100f);
                //if (randomRoll > 50f)
                //{
                //    ___companyEventTracker.ActivateRandomEvent();
                //}

                Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
        }
        */
    }
}
