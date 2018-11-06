using Harmony;
using BattleTech;
using System.Reflection;
using BattleTech.UI;
using TMPro;
using HBS;
using UnityEngine;
using System;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using BattleTech.Data;
using BattleTech.StringInterpolation;
using System.Collections.Generic;
using Localize;



namespace DynamicCompanyMorale
{
    public static class DynamicCompanyMorale
    {
        internal static string ModDirectory;
        internal static SimGameEventOption LastModifiedOption;
        internal static int EventMoraleMultiplier = 4;
        
        // BEN: Temporary morale events
        internal static int EventMoraleDurationBase = 15;
        internal static int EventMoraleDurationNumerator = 240;

        // BEN: Debug (0: nothing, 1: errors, 2:all)
        internal static int DebugLevel = 1;

        public static void Init(string directory, string settingsJSON)
        {
            ModDirectory = directory;
            HarmonyInstance.Create("de.ben.DynamicCompanyMorale").PatchAll(Assembly.GetExecutingAssembly());
        }
    }



    /*
    [HarmonyPatch(typeof(GameInstanceSave))]
    [HarmonyPatch(new Type[] { typeof(GameInstance), typeof(SaveReason) })]
    public static class GameInstanceSave_Constructor_Patch
    {
        static void Postfix(GameInstanceSave __instance)
        {
            Helper.SaveState(__instance.InstanceGUID, __instance.SaveTime);
        }
    }

    [HarmonyPatch(typeof(GameInstance), "Load")]
    public static class GameInstance_Load_Patch
    {
        static void Prefix(GameInstanceSave save)
        {
            Helper.LoadState(save.InstanceGUID, save.SaveTime);
        }
    }
    */



    [HarmonyPatch(typeof(SimGameState), "RemoveSimGameEventResult")]
    public static class SimGameState_RemoveSimGameEventResult_Patch
    {
        public static bool Prefix(SimGameState __instance, ref TemporarySimGameResult result)
        {
            bool SkipOriginalMethod = false;

            Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] Will handle result with Stat.name Morale (if any)");
            if (result.Stats != null)
            {
                for (int i = 0; i < result.Stats.Length; i++)
                {
                    if (result.Stats[i].name == "Morale")
                    {
                        Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] Found TemporarySimGameResult with Stat.name Morale(" + result.Stats[i].value + ")");

                        int AdjustedEventResultMoraleValue = (-1) * int.Parse(result.Stats[i].value);
                        Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] AdjustedEventResultMoraleValue: " + AdjustedEventResultMoraleValue.ToString());

                        // Modify Fields.EventMoraleModifier;
                        //Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] Fields.EventMoraleModifier (before): " + Fields.EventMoraleModifier.ToString());

                        int CurrentEventMoraleModifier = Fields.EventMoraleModifier;
                        int UpdatedEventMoraleModifier = CurrentEventMoraleModifier + AdjustedEventResultMoraleValue;
                        //Fields.EventMoraleModifier = UpdatedEventMoraleModifier;

                        //Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] Fields.EventMoraleModifier (after): " + Fields.EventMoraleModifier.ToString());


                        // Set "Morale" stat directly
                        int CurrentBaseMorale = __instance.GetCurrentBaseMorale();
                        int CurrentExpenditureMoraleValue = __instance.ExpenditureMoraleValue[__instance.ExpenditureLevel];
                        int CorrectedMoraleRating = CurrentBaseMorale + CurrentExpenditureMoraleValue + UpdatedEventMoraleModifier;
                        StatCollection statCol = __instance.CompanyStats;
                        SimGameStat stat = new SimGameStat("Morale", CorrectedMoraleRating, true);
                        SimGameState.SetSimGameStat(stat, statCol);


                        // Push message out
                        Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] Call SimGameState.RoomManager.ShipRoom.AddEventToast with custom message");
                        List<ResultDescriptionEntry> list = new List<ResultDescriptionEntry>();
                        GameContext gameContext = new GameContext(__instance.Context);
                        string valuePrefix = int.Parse(result.Stats[i].value) > 0 ? "+" : "";
                        string valuePostfix = int.Parse(result.Stats[i].value) > 0? "boost" : "penalty";

                        gameContext.SetObject(GameContextObjectTagEnum.ResultDuration, result.ResultDuration); // Add more?
                        list.Add(new ResultDescriptionEntry(new Text("{0} {1} {2}{3}", new object[]
                        {
                            "The",
                            valuePrefix + result.Stats[i].value + " Morale ",
                            valuePostfix + " has expired",
                            Environment.NewLine
                        }), gameContext));
                        if (list != null)
                        {
                            foreach (ResultDescriptionEntry resultDescriptionEntry in list)
                            {
                                __instance.RoomManager.ShipRoom.AddEventToast(resultDescriptionEntry.Text);
                            }
                        }
                        SkipOriginalMethod = true;
                    }
                }
            }
            if (SkipOriginalMethod)
            {
                Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] Handled event with Stat.name Morale, will skip original method...");
                Logger.LogLine("----------------------------------------------------------------------------------------------------");
                return false;
            }
            else
            {
                Logger.LogLine("----------------------------------------------------------------------------------------------------");
                return true;
            }
        }
    }



    // BEN: This is called BEFORE the finances-selection screen
    [HarmonyPatch(typeof(SimGameState), "OnNewQuarterBegin")]
    public static class SimGameState_OnNewQuarterBegin_Patch
    {

        public static void Postfix(SimGameState __instance)
        {
            int CurrentBaseMorale = __instance.GetCurrentBaseMorale();
            Logger.LogLine("[SimGameState_OnNewQuarterBegin_POSTFIX] Resetting Morale to CurrentBaseMorale(" + CurrentBaseMorale + ")");
            __instance.CompanyStats.ModifyStat<int>("Mission", 0, "COMPANY_MonthlyStartingMorale", StatCollection.StatOperation.Set, CurrentBaseMorale, -1, true);
            __instance.CompanyStats.ModifyStat<int>("Mission", 0, "Morale", StatCollection.StatOperation.Set, CurrentBaseMorale, -1, true);
            Logger.LogLine("----------------------------------------------------------------------------------------------------");
        }
    }



    [HarmonyPatch(typeof(SimGameState), "OnEventOptionSelected")]
    public static class SimGameState_OnEventOptionSelected_Patch
    {
        public static void Prefix(SimGameState __instance, ref SimGameEventOption option)
        {
            try
            {
                Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] Selected option: " + option.Description.Id);
                foreach (SimGameEventResultSet eventResultSet in option.ResultSets)
                {
                    Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] Modifying morale value of possible outcome: " + eventResultSet.Description.Id + " (if any)");

                    // Prepare result for extraction of morale stat
                    SimGameEventResult extractedMoraleResult = new SimGameEventResult();
                    bool foundMoraleResult = false;

                    foreach (SimGameEventResult eventResult in eventResultSet.Results)
                    {
                        if (eventResult.Stats != null)
                        {
                            for (int i = 0; i < eventResult.Stats.Length; i++)
                            {
                                if (eventResult.Stats[i].name == "Morale")
                                {
                                    Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] eventResultMoraleValue (unaltered): " + eventResult.Stats[i].value);

                                    // Mark Results[] as containing Morale Stat
                                    foundMoraleResult = true;
                                    // Create new temporary result with ONLY Morale Stat in it, value already modified with multiplier
                                    extractedMoraleResult.Scope = EventScope.Company;
                                    extractedMoraleResult.Requirements = new RequirementDef(null);
                                    extractedMoraleResult.AddedTags = new HBS.Collections.TagSet();
                                    extractedMoraleResult.RemovedTags = new HBS.Collections.TagSet();
                                    int adjustedMoraleValue = (int.Parse(eventResult.Stats[i].value) * DynamicCompanyMorale.EventMoraleMultiplier);
                                    SimGameStat Stat = new SimGameStat("Morale", adjustedMoraleValue, false);
                                    extractedMoraleResult.Stats = new SimGameStat[] { Stat };
                                    extractedMoraleResult.Actions = new SimGameResultAction[] { };
                                    extractedMoraleResult.ForceEvents = new SimGameForcedEvent[] { };
                                    extractedMoraleResult.TemporaryResult = true;
                                    extractedMoraleResult.ResultDuration = DynamicCompanyMorale.EventMoraleDurationBase + Math.Abs(DynamicCompanyMorale.EventMoraleDurationNumerator / adjustedMoraleValue);
                                    Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] extractedMoraleResultValue: " + extractedMoraleResult.Stats[0].value);

                                    // Invalidate original morale stat
                                    //eventResult.Stats[i].name = ""; // Breaks code
                                    //eventResult.Stats[i].value = "0"; // This is enough
                                    eventResult.Stats[i].typeString = ""; // Better
                                    Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] eventResultIsValid: " + !String.IsNullOrEmpty(eventResult.Stats[i].typeString));
                                }
                            }
                        }
                    }

                    if (foundMoraleResult)
                    {
                        Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] eventResultSet.Results.Length (before): " + eventResultSet.Results.Length.ToString());
                        Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] Adding extractedMoraleResult to eventResultSet.Results now");

                        Array.Resize<SimGameEventResult>(ref eventResultSet.Results, eventResultSet.Results.Length + 1);
                        eventResultSet.Results[eventResultSet.Results.Length - 1] = extractedMoraleResult;

                        Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] eventResultSet.Results.Length (after): " + eventResultSet.Results.Length.ToString());

                        // Set early for UI
                        Fields.IsTemporaryMoraleEventActive = true;
                    }
                }
                DynamicCompanyMorale.LastModifiedOption = option;
                // BEN: After the original method is called, everything is already in the TemporaryResultTracker & several "updateData"-Calls are made...
                Logger.LogLine("----------------------------------------------------------------------------------------------------");

            } catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnEventDismissed")]
    public static class SimGameState_OnEventDismissed_Patch
    {
        public static void Postfix(SimGameState __instance, BattleTech.UI.SimGameInterruptManager.EventPopupEntry entry)
        {
            foreach (SimGameEventResultSet eventResultSet in DynamicCompanyMorale.LastModifiedOption.ResultSets)
            {
                Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] Resetting morale value of saved possible outcome: " + eventResultSet.Description.Id + " (if any)");
                bool foundMoraleResult = false;

                foreach (SimGameEventResult eventResult in eventResultSet.Results)
                {
                    if (eventResult.Stats != null)
                    {
                        for (int i = 0; i < eventResult.Stats.Length; i++)
                        {
                            if (eventResult.Stats[i].name == "Morale")
                            {
                                foundMoraleResult = true;

                                // BEN: Seems like at this point the eventResult is already added to the TemporaryResultTracker.
                                // So this reset will do nothing (to tracked temporary results).
                                // Doing this only to minimize potential side-effects
                                Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResultMoraleValue: " + eventResult.Stats[i].value);
                                Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResultIsValid (before): " + !String.IsNullOrEmpty(eventResult.Stats[i].typeString));
                                if (String.IsNullOrEmpty(eventResult.Stats[i].typeString))
                                {
                                    eventResult.Stats[i].typeString = "System.Int32";
                                }
                                Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResultIsValid (after): " + !String.IsNullOrEmpty(eventResult.Stats[i].typeString));
                            }
                        }
                    }
                }
                if (foundMoraleResult)
                {
                    Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResultSet.Results.Length (before): " + eventResultSet.Results.Length.ToString());
                    Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] Removing extractedMoraleResult from eventResultSet.Results now");

                    Array.Resize<SimGameEventResult>(ref eventResultSet.Results, eventResultSet.Results.Length - 1);

                    Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResultSet.Results.Length (after): " + eventResultSet.Results.Length.ToString());
                }
            }
            // Refresh dependent widgets
            __instance.RoomManager.RefreshLeftNavFromShipScreen();
            
            // @ToDo: Test
            SGCaptainsQuartersStatusScreen captainsQuartersStatusScreen = Traverse.Create(__instance.RoomManager.CptQuartersRoom).GetValue<SGCaptainsQuartersStatusScreen>("quarterlyReport");
            captainsQuartersStatusScreen.RefreshData();

            Logger.LogLine("----------------------------------------------------------------------------------------------------");
        }
    }



    // BEN: This screen has actually 3 states (ScreensModes)[DefaultStatus, QuarterlyReport, NextQuarterProjections].Keep in mind! 
    // It's probably better to NOT rely on global morale getters here...
    [HarmonyPatch(typeof(SGCaptainsQuartersStatusScreen), "RefreshData")]
    public static class SGCaptainsQuartersStatusScreen_RefreshData_Patch
    {
        public static void Postfix(SGCaptainsQuartersStatusScreen __instance, SGMoraleBar ___MoralBar, SimGameState ___simState)
        {
            Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_POSTFIX] SimGameState.Morale is " + ___simState.Morale.ToString());

            int CurrentBaseMorale = ___simState.GetCurrentBaseMorale();
            int CurrentExpenditureMoraleValue = ___simState.ExpenditureMoraleValue[___simState.ExpenditureLevel];
            Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_POSTFIX] CurrentExpenditureMoraleValue is " + CurrentExpenditureMoraleValue.ToString());
            int EventMoraleModifier = Fields.EventMoraleModifier;
            Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_POSTFIX] EventMoraleModifier is " + EventMoraleModifier.ToString());
            int ProjectedMoraleRating = CurrentBaseMorale + CurrentExpenditureMoraleValue + EventMoraleModifier;

            if (ProjectedMoraleRating < 0)
            {
                ProjectedMoraleRating = 0;
            }
            else if (ProjectedMoraleRating > 50)
            {
                ProjectedMoraleRating = 50;
            }

            // No setting of values here, just preview!
            float percentage = (float)ProjectedMoraleRating / (float)___simState.Constants.Story.MaxMorale;
            ___MoralBar.SetMorale(percentage, ProjectedMoraleRating);

            Logger.LogLine("----------------------------------------------------------------------------------------------------");
        }
    }

    [HarmonyPatch(typeof(SGCaptainsQuartersStatusScreen), "Dismiss")]
    public static class SGCaptainsQuartersStatusScreen_Dismiss_Patch
    {
        public static void Prefix(SGCaptainsQuartersStatusScreen __instance, SGMoraleBar ___MoralBar, SimGameState ___simState)
        {
            Logger.LogLine("[SGCaptainsQuartersStatusScreen_Dismiss_PREFIX] Check before setting SimGameState.Morale: " + ___simState.Morale.ToString());

            int CurrentBaseMorale = ___simState.GetCurrentBaseMorale();
            int CurrentExpenditureMoraleValue = ___simState.ExpenditureMoraleValue[___simState.ExpenditureLevel];
            int EventMoraleModifier = Fields.EventMoraleModifier;

            int CorrectedMoraleRating = CurrentBaseMorale + CurrentExpenditureMoraleValue + EventMoraleModifier;
            if (CorrectedMoraleRating < 0)
            {
                CorrectedMoraleRating = 0;
            }
            else if (CorrectedMoraleRating > 50)
            {
                CorrectedMoraleRating = 50;
            }
            //___simState.CompanyStats.ModifyStat<int>("Mission", 0, "COMPANY_MonthlyStartingMorale", StatCollection.StatOperation.Set, CurrentBaseMorale, -1, true);
            ___simState.CompanyStats.ModifyStat<int>("Mission", 0, "Morale", StatCollection.StatOperation.Set, CorrectedMoraleRating, -1, true);

            Logger.LogLine("[SGCaptainsQuartersStatusScreen_Dismiss_PREFIX] Check after setting SimGameState.Morale: " + ___simState.Morale.ToString());

            // Refresh dependent widgets
            ___simState.RoomManager.RefreshLeftNavFromShipScreen();

            Logger.LogLine("----------------------------------------------------------------------------------------------------");
        }
    }

    [HarmonyPatch(typeof(SGFinancialForecastWidget), "RefreshData")]
    public static class SSGFinancialForecastWidget_RefreshData_Patch
    {
        public static void Postfix(SGFinancialForecastWidget __instance, TextMeshProUGUI ___CurrSpendingStyleText, SimGameState ___simState)
        {
            string ExpenditureLevelString = ___simState.ExpenditureLevel.ToString();
            int CurrentExpenditureMoraleValue = ___simState.ExpenditureMoraleValue[___simState.ExpenditureLevel];
            string text = ExpenditureLevelString;

            if (CurrentExpenditureMoraleValue > 0)
            {
                text += " (+" + CurrentExpenditureMoraleValue.ToString() + " Morale)";
            }
            else if (CurrentExpenditureMoraleValue < 0)
            {
                text += " (" + CurrentExpenditureMoraleValue.ToString() + " Morale)";
            }

            ___CurrSpendingStyleText.SetText("Finances: {0}", new object[]
            {
                text
            });
        }
    }

    [HarmonyPatch(typeof(SGNavigationWidgetLeft), "UpdateData")]
    public static class SGNavigationWidgetLeft_UpdateData_Patch
    {
        public static void Postfix(SGNavigationWidgetLeft __instance, SimGameState ___simState)
        {
            Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.Morale: " + ___simState.Morale.ToString());
            
            // Validate that this is called FIRST after Load||Mission||Conversations... otherwise Save/Load has to be re-enabled!
            Fields.IsTemporaryMoraleEventActive = ___simState.IsTemporaryMoraleEventActive();
            Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.IsTemporaryMoraleEventActive: " + Fields.IsTemporaryMoraleEventActive);

            Fields.EventMoraleModifier = ___simState.GetAbsoluteMoraleValueOfAllTemporaryResults();
            Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.EventMoraleModifier: " + Fields.EventMoraleModifier);
            Logger.LogLine("----------------------------------------------------------------------------------------------------");
        }
    }

    [HarmonyPatch(typeof(SGMoraleBar), "SetMorale")]
    public static class SGMoraleBar_SetMorale_Patch
    {
        public static void Postfix(SGMoraleBar __instance)
        {
            int EventMoraleModifier = Fields.EventMoraleModifier;
            TextMeshProUGUI moraleTotal = Traverse.Create(__instance).Field("moraleTotal").GetValue<TextMeshProUGUI>();
            TextMeshProUGUI moraleTitle = Traverse.Create(__instance).Field("moraleTitle").GetValue<TextMeshProUGUI>();
            Color color;
            string text = "Morale";

            //Logger.LogLine("[SGMoraleBar_SetMorale_POSTFIX] IsTemporaryMoraleEventActive: " + simGameState.IsTemporaryMoraleEventActive().ToString());

            if (EventMoraleModifier > 0)
            {
                color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.green;
                Logger.LogLine("[SGMoraleBar_SetMorale_POSTFIX] Green");
                text += " (+" + EventMoraleModifier.ToString() + " from events)";
            }
            else if (EventMoraleModifier < 0)
            {
                color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.red;
                Logger.LogLine("[SGMoraleBar_SetMorale_POSTFIX] Red");
                text += " (" + EventMoraleModifier.ToString() + " from events)";
            }
            // Zero from two or more temp results
            else if (Fields.IsTemporaryMoraleEventActive)
            {
                color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.blue;
                Logger.LogLine("[SGMoraleBar_SetMorale_POSTFIX] Blue");
                text += " (+ -" + EventMoraleModifier.ToString() + " from events)";
            }
            // Zero from no active temp results
            else
            {
                color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.white;
                Logger.LogLine("[SGMoraleBar_SetMorale_POSTFIX] White");
            }
            moraleTotal.color = color;
            moraleTitle.SetText("{0}", new object[]
            {
                text
            });
            Logger.LogLine("----------------------------------------------------------------------------------------------------");
        }
    }



    // Add custom methods to SimGameState
    public static class Custom
    {
        public static int GetCurrentBaseMorale(this SimGameState simGameState)
        {
            int ArgoMoraleModifier = 0;
            if (simGameState.CurDropship == DropshipType.Argo)
            {
                foreach (ShipModuleUpgrade shipModuleUpgrade in simGameState.ShipUpgrades)
                {
                    foreach (SimGameStat companyStat in shipModuleUpgrade.Stats)
                    {
                        bool isNumeric = false;
                        int modifier = 0;
                        if (companyStat.name == "Morale")
                        {
                            isNumeric = int.TryParse(companyStat.value, out modifier);
                            if (isNumeric)
                            {
                                ArgoMoraleModifier += modifier;
                            }
                        }
                    }
                }
            }
            int CurrentBaseMorale = simGameState.Constants.Story.StartingMorale + ArgoMoraleModifier;
            Logger.LogLine("[Custom.GetCurrentBaseMorale]" + CurrentBaseMorale.ToString());
            return CurrentBaseMorale;
        }

        public static int GetAbsoluteMoraleValueOfAllTemporaryResults(this SimGameState simGameState)
        {
            int AbsoluteMoraleValueOfAllTemporaryResults = 0;
            foreach (TemporarySimGameResult temporarySimGameResult in simGameState.TemporaryResultTracker)
            {
                if (temporarySimGameResult.Scope == EventScope.Company)
                {
                    if (temporarySimGameResult.Stats != null)
                    {
                        foreach (SimGameStat simGameStat in temporarySimGameResult.Stats)
                        {
                            Logger.LogLine("[Custom.GetAbsoluteMoraleValueOfAllTemporaryResults] Check Stats: " + simGameStat.name + " (" + simGameStat.value + ") for " + (temporarySimGameResult.ResultDuration - temporarySimGameResult.DaysElapsed) + " days");
                            if (simGameStat.name == "Morale")
                            {
                                AbsoluteMoraleValueOfAllTemporaryResults += int.Parse(simGameStat.value);
                            }
                        }
                    }
                }
            }
            return AbsoluteMoraleValueOfAllTemporaryResults;
        }

        public static bool IsTemporaryMoraleEventActive(this SimGameState simGameState)
        {
            bool IsTemporaryMoraleEventActive = false;
            foreach (TemporarySimGameResult temporarySimGameResult in simGameState.TemporaryResultTracker)
            {
                if (temporarySimGameResult.Scope == EventScope.Company)
                {
                    if (temporarySimGameResult.Stats != null)
                    {
                        foreach (SimGameStat simGameStat in temporarySimGameResult.Stats)
                        {
                            if (simGameStat.name == "Morale")
                            {
                                //Logger.LogLine("[Custom.IsTemporaryMoraleEventActive] True");
                                IsTemporaryMoraleEventActive = true;
                            }
                        }
                    }
                }
            }
            return IsTemporaryMoraleEventActive;
        }
    }
}
