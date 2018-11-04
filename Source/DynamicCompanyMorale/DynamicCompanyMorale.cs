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
        
        // Ben: Temporary morale events
        internal static int EventMoraleDurationBase = 15;
        internal static int EventMoraleDurationNumerator = 240;

        // BEN: Debug (0: nothing, 1: errors, 2:all)
        internal static int DebugLevel = 2;

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

            Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] Will handle result with Stat.name Morale");
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
                Logger.LogLine("[SimGameState_RemoveSimGameEventResult_PREFIX] Handled event with Stat.name Morale, will skip original method so Funds and/or Reputation values won't reset!");
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
        public static void Prefix(SimGameState __instance, SimGameEventOption option)
        {
            Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] Selected option: " + option.Description.Id);
            foreach (SimGameEventResultSet eventResultSet in option.ResultSets)
            {
                string outcomeID = eventResultSet.Description.Id;
                Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] Modifying morale value of possible outcome: " + outcomeID + " (if any)");

                foreach (SimGameEventResult eventResult in eventResultSet.Results)
                {
                    if (eventResult.Stats != null)
                    {
                        for (int i = 0; i < eventResult.Stats.Length; i++)
                        {
                            if (eventResult.Stats[i].name == "Morale")
                            {
                                Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] eventResultMoraleValue (before): " + eventResult.Stats[i].value);
                                eventResult.Stats[i].value = (int.Parse(eventResult.Stats[i].value) * DynamicCompanyMorale.EventMoraleMultiplier).ToString();
                                Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] eventResultMoraleValue (after): " + eventResult.Stats[i].value);

                                // BEN: Temporary morale events
                                eventResult.TemporaryResult = true;
                                //eventResult.ResultDuration = 30;
                                eventResult.ResultDuration = DynamicCompanyMorale.EventMoraleDurationBase + Math.Abs(DynamicCompanyMorale.EventMoraleDurationNumerator / int.Parse(eventResult.Stats[i].value));

                                Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] eventResult.TemporaryResult: " + eventResult.TemporaryResult.ToString());
                                Logger.LogLine("[SimGameState_OnEventOptionSelected_PREFIX] eventResult.ResultDuration: " + eventResult.ResultDuration.ToString());
                            }
                        }
                    }
                }
            }
            DynamicCompanyMorale.LastModifiedOption = option;

            Logger.LogLine("----------------------------------------------------------------------------------------------------");
        }
    }

    [HarmonyPatch(typeof(SimGameEventTracker), "OnOptionSelected")]
    public static class SSimGameEventTracker_OnOptionSelected_Patch
    {
        public static void Postfix(SimGameEventTracker __instance, SimGameEventResultSet __result)
        {
            string outcomeID = __result.Description.Id;
            Logger.LogLine("[SimGameEventTracker_OnOptionSelected_POSTFIX] Saving morale value of " + outcomeID + " (if any)");
            foreach (SimGameEventResult eventResult in __result.Results)
            {
                if (eventResult.Stats != null)
                {
                    for (int i = 0; i < eventResult.Stats.Length; i++)
                    {
                        if (eventResult.Stats[i].name == "Morale")
                        {
                            Logger.LogLine("[SimGameEventTracker_OnOptionSelected_POSTFIX] eventResultMoraleValue: " + eventResult.Stats[i].value);

                            int CurrentEventMoraleModifier = Fields.EventMoraleModifier;
                            int UpdatedEventMoraleModifier = CurrentEventMoraleModifier + int.Parse(eventResult.Stats[i].value);
                            //No need to set here anymore. It's already in the TemporaryResultTracker which is checked in SGNavigationWidgetLeft_UpdateData_POSTFIX 
                            //Fields.EventMoraleModifier = UpdatedEventMoraleModifier;

                            // Set early
                            Fields.IsTemporaryMoraleEventActive = true;
                        }
                    }
                }
            }
            Logger.LogLine("----------------------------------------------------------------------------------------------------");
        }
    }

    [HarmonyPatch(typeof(SimGameState), "BuildSimGameStatsResults")]
    public static class SimGameState_BuildSimGameStatsResults_Patch
    {
        public static bool Prefix(SimGameState __instance, ref SimGameStat[] stats, ref GameContext context, ref SimGameStatDescDef.DescriptionTense tense, ref List<ResultDescriptionEntry> __result, string prefix = "•")
        {
            Logger.LogLine("[SimGameState_BuildSimGameStatsResults_PREFIX] Called");
            // BEN: Check for morale stat
            // NOTE: Incoming stats are ALL stats from ONE result!
            bool isMoraleStatInResult = false;
            foreach (SimGameStat stat in stats)
            {
                if (!string.IsNullOrEmpty(stat.name) && stat.value != null)
                {
                    if (stat.name == "Morale")
                    {
                        isMoraleStatInResult = true;
                    }
                }
            }
            if (isMoraleStatInResult)
            {
                Logger.LogLine("[SimGameState_BuildSimGameStatsResults_PREFIX] Reset all other stats of a moral result entry back to non-temporary so the summary screen works out");

                //----------------------------------------------------------------------------------------------------
                // Ben: Copy and adapt original method

                List<ResultDescriptionEntry> list = new List<ResultDescriptionEntry>();
                foreach (SimGameStat simGameStat in stats)
                {
                    if (!string.IsNullOrEmpty(simGameStat.name) && simGameStat.value != null)
                    {
                        SimGameStatDescDef simGameStatDescDef = null;
                        GameContext gameContext = new GameContext(context);

                        // BEN: Adapt
                        if (simGameStat.name == "Morale")
                        {
                            tense = SimGameStatDescDef.DescriptionTense.Temporal;
                        }
                        else
                        {
                            tense = SimGameStatDescDef.DescriptionTense.Default;
                            // Reset to instance.context to get rid of the temporal part
                            gameContext = new GameContext(__instance.Context);
                        }
                        Logger.LogLine("[SimGameState_BuildSimGameStatsResults_PREFIX] Stat: " + simGameStat.name + ", tense: " + tense.ToString());
                        // :NEB

                        // BEN: Copy
                        if (__instance.DataManager.SimGameStatDescDefs.Exists("SimGameStatDesc_" + simGameStat.name))
                        {
                            simGameStatDescDef = __instance.DataManager.GetStatDescDef(simGameStat);
                        }
                        else
                        {
                            int num = simGameStat.name.IndexOf('.');
                            if (num >= 0)
                            {
                                string text = simGameStat.name.Substring(0, num);
                                if (__instance.DataManager.SimGameStatDescDefs.Exists("SimGameStatDesc_" + text))
                                {
                                    simGameStatDescDef = __instance.DataManager.SimGameStatDescDefs.Get("SimGameStatDesc_" + text);
                                    string[] array = simGameStat.name.Split(new char[]
                                    {
                                    '.'
                                    });
                                    BattleTechResourceType? battleTechResourceType = null;
                                    object obj = null;
                                    string id;
                                    if (array.Length < 3)
                                    {
                                        if (text == "Reputation")
                                        {
                                            id = "faction_" + array[1];
                                            battleTechResourceType = new BattleTechResourceType?(BattleTechResourceType.FactionDef);
                                        }
                                        else
                                        {
                                            id = null;
                                            battleTechResourceType = null;
                                        }
                                    }
                                    else
                                    {
                                        string value = array[1];
                                        id = array[2];
                                        try
                                        {
                                            battleTechResourceType = new BattleTechResourceType?((BattleTechResourceType)Enum.Parse(typeof(BattleTechResourceType), value));
                                        }
                                        catch
                                        {
                                            battleTechResourceType = null;
                                        }
                                    }
                                    if (battleTechResourceType != null)
                                    {
                                        obj = __instance.DataManager.Get(battleTechResourceType.Value, id);
                                    }
                                    if (obj != null)
                                    {
                                        gameContext.SetObject(GameContextObjectTagEnum.ResultObject, obj);
                                    }
                                    else
                                    {
                                        simGameStatDescDef = null;
                                    }
                                }
                            }
                        }
                        if (simGameStatDescDef != null)
                        {
                            if (!simGameStatDescDef.hidden)
                            {
                                gameContext.SetObject(GameContextObjectTagEnum.ResultValue, Mathf.Abs(simGameStat.ToSingle()));
                                if (simGameStat.set)
                                {
                                    list.Add(new ResultDescriptionEntry(new Text("{0} {1}{2}", new object[]
                                    {
                                    prefix,
                                    Interpolator.Interpolate(simGameStatDescDef.GetResultString(SimGameStatDescDef.DescriptionType.Set, tense), gameContext),
                                    Environment.NewLine
                                    }), gameContext));
                                }
                                else if (simGameStat.Type == typeof(int) || simGameStat.Type == typeof(float))
                                {
                                    if (simGameStat.ToSingle() > 0f)
                                    {
                                        Logger.LogLine("[SimGameState_BuildSimGameStatsResults_PREFIX] Final string: " + Interpolator.Interpolate(simGameStatDescDef.GetResultString(SimGameStatDescDef.DescriptionType.Positive, tense), gameContext));

                                        list.Add(new ResultDescriptionEntry(new Text("{0} {1}{2}", new object[]
                                        {
                                        prefix,
                                        Interpolator.Interpolate(simGameStatDescDef.GetResultString(SimGameStatDescDef.DescriptionType.Positive, tense), gameContext),
                                        Environment.NewLine
                                        }), gameContext));
                                    }
                                    else if (simGameStat.ToSingle() < 0f)
                                    {
                                        Logger.LogLine("[SimGameState_BuildSimGameStatsResults_PREFIX] Final string: " + Interpolator.Interpolate(simGameStatDescDef.GetResultString(SimGameStatDescDef.DescriptionType.Negative, tense), gameContext));

                                        list.Add(new ResultDescriptionEntry(new Text("{0} {1}{2}", new object[]
                                        {
                                        prefix,
                                        Interpolator.Interpolate(simGameStatDescDef.GetResultString(SimGameStatDescDef.DescriptionType.Negative, tense), gameContext),
                                        Environment.NewLine
                                        }), gameContext));
                                    }
                                }
                            }
                        }
                        else
                        {
                            string tooltipString = __instance.DataManager.GetTooltipString(simGameStat, null);
                            list.Add(new ResultDescriptionEntry(new Text("{0} {1} {2}{3}", new object[]
                            {
                            prefix,
                            tooltipString,
                            simGameStat.value,
                            Environment.NewLine
                            }), gameContext));
                        }
                        // :NEB
                    }
                }
                // BEN: Modify result, refresh UI and skip original method
                __result = list;
                __instance.RoomManager.RefreshLeftNavFromShipScreen();
                Logger.LogLine("----------------------------------------------------------------------------------------------------");
                return false;

                // :NEB
                //----------------------------------------------------------------------------------------------------
            }
            else
            {
                Logger.LogLine("----------------------------------------------------------------------------------------------------");
                // Call original method
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnEventDismissed")]
    public static class SimGameState_OnEventDismissed_Patch
    {
        public static void Postfix(SimGameState __instance, BattleTech.UI.SimGameInterruptManager.EventPopupEntry entry)
        {
            Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] Reset morale values (if any)");
            foreach (SimGameEventResultSet eventResultSet in DynamicCompanyMorale.LastModifiedOption.ResultSets)
            {
                foreach (SimGameEventResult eventResult in eventResultSet.Results)
                {
                    if (eventResult.Stats != null)
                    {
                        for (int i = 0; i < eventResult.Stats.Length; i++)
                        {
                            if (eventResult.Stats[i].name == "Morale")
                            {
                                // BEN: Seems like at this point the eventResult is already added to the TemporaryResultTracker. So this reset will do nothing (to tracked temporary results).
                                Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResultMoraleValue (before): " + eventResult.Stats[i].value);
                                eventResult.Stats[i].value = (int.Parse(eventResult.Stats[i].value) / DynamicCompanyMorale.EventMoraleMultiplier).ToString();
                                Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResultMoraleValue (after): " + eventResult.Stats[i].value);

                                // BEN: Reset temporary morale events
                                eventResult.TemporaryResult = false;
                                eventResult.ResultDuration = 0;
                                Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResult.TemporaryResult: " + eventResult.TemporaryResult.ToString());
                                Logger.LogLine("[SimGameState_OnEventDismissed_POSTFIX] eventResult.ResultDuration: " + eventResult.ResultDuration.ToString());
                            }
                        }
                    }
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
            //int AbsoluteMoraleValueOfAllTemporaryResults = ___simState.GetAbsoluteMoraleValueOfAllTemporaryResults();
            Fields.EventMoraleModifier = ___simState.GetAbsoluteMoraleValueOfAllTemporaryResults();

            //Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check AbsoluteMoraleValueOfAllTemporaryResults: " + AbsoluteMoraleValueOfAllTemporaryResults);
            Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.EventMoraleModifier: " + Fields.EventMoraleModifier);
            Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.IsTemporaryMoraleEventActive: " + Fields.IsTemporaryMoraleEventActive);
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
                                Logger.LogLine("[Custom.IsTemporaryMoraleEventActive] True");
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
