using Harmony;
using BattleTech;

namespace DynamicCompanyMorale
{
    class EventGenerator
    {
        internal static int DaysUneventful = 3;
        internal static int DaysUneventfulCounter = 0;
        internal static int SpecialEventCounter = 0;
        internal static int SpecialEventMax = 10;

        [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
        public static class SimGameState_OnDayPassed_Patch
        {
            public static bool Prepare()
            {
                return DynamicCompanyMorale.EnableEventGenerator;
            }

            public static void Postfix(SimGameState __instance, SimGameEventTracker ___companyEventTracker)
            {

                void ActivateAnyEvent(float eventChance)
                {
                    float randomRoll = __instance.NetworkRandom.Float(0f, 1f);
                    if (randomRoll < eventChance)
                    {
                        Logger.Debug("----------------------------------------------------------------------------------------------------");
                        Logger.Debug("[SimGameState_OnDayPassed_POSTFIX] ActivateRandomEvent");
                        Logger.Debug("----------------------------------------------------------------------------------------------------");
                        ___companyEventTracker.ActivateRandomEvent();
                    }
                }

                void ActivateSpecialEvent(string eventId, float eventChance)
                {
                    if (SpecialEventCounter >= SpecialEventMax)
                    {
                        Logger.Debug("[SimGameState_OnDayPassed_POSTFIX] SpecialEventMax(" + SpecialEventMax + ") reached. Exiting.");
                        return;
                    }
                    SpecialEventCounter++;

                    float randomRoll = __instance.NetworkRandom.Float(0f, 1f);
                    if (randomRoll < eventChance)
                    {
                        Pilot plt = null;
                        SimGameForcedEvent evt = new SimGameForcedEvent
                        {
                            Scope = EventScope.Company,
                            EventID = eventId,
                            MinDaysWait = 0,
                            MaxDaysWait = 0,
                            Probability = 100,
                            RetainPilot = false
                        };
                        Logger.Debug("----------------------------------------------------------------------------------------------------");
                        Logger.Debug("[SimGameState_OnDayPassed_POSTFIX] AddSpecialEvent");
                        Logger.Debug("----------------------------------------------------------------------------------------------------");
                        __instance.AddSpecialEvent(evt, plt);
                        
                    }
                }

                
                bool activateEvent = DaysUneventfulCounter++ == DaysUneventful;

                if (activateEvent)
                {
                    DaysUneventfulCounter = 0;
                    ActivateSpecialEvent("event_dcm_test", 1f);
                    //ActivateAnyEvent(0.2f);
                }
            }
        }
    }
}
