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
using System.Collections.Generic;
using Localize;
using BattleTech.UI.Tooltips;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BattleTech.UI.TMProWrapper;

namespace DynamicCompanyMorale
{
    public static class DynamicCompanyMorale
    {
        internal static string LogPath;
        internal static string ModDirectory;
        internal static Settings Settings;
        internal static SimGameEventOption LastModifiedOption;

        //internal static int EventMoraleMultiplier = 4;
        //internal static int EventMoraleDurationBase = 15;
        //internal static int EventMoraleDurationNumerator = 240;

        // BEN: Enable EventGenerator?
        internal static bool EnableEventGenerator = false;

        // BEN: Debug (0: nothing, 1: errors, 2:all)
        internal static int DebugLevel = 2;

        public static void Init(string directory, string settings)
        {
            HarmonyInstance.Create("de.ben.DynamicCompanyMorale").PatchAll(Assembly.GetExecutingAssembly());

            ModDirectory = directory;

            LogPath = Path.Combine(ModDirectory, "DynamicCompanyMorale.log");
            File.CreateText(DynamicCompanyMorale.LogPath);

            try
            {
                Settings = JsonConvert.DeserializeObject<Settings>(settings);
            }
            catch (Exception)
            {
                Settings = new Settings();
            }
        }
    }

    [HarmonyPatch(typeof(GameInstanceSave), MethodType.Constructor)]
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


    // Fix wrong getters for Resolve/Turn
    [HarmonyPatch(typeof(SimGameState), "CurResolvePerTurn", MethodType.Getter)]
    public static class SimGameState_CurResolvePerTurn_Patch
    {
        public static void Postfix(SimGameState __instance, ref int __result)
        {
            try
            {
                Logger.LogLine("[SimGameState_CurResolvePerTurn_POSTFIX] __result BEFORE: " + __result);
                MoraleConstantsDef moraleConstants = __instance.CombatConstants.MoraleConstants;
                for (int i = moraleConstants.BaselineAddFromSimGameThresholds.Length - 1; i >= 0; i--)
                {
                    // Comparison was > but must be >=
                    if (__instance.Morale >= moraleConstants.BaselineAddFromSimGameThresholds[i])
                    {
                        __result = moraleConstants.BaselineAddFromSimGameValues[i];
                        break;
                    }
                }

                Logger.LogLine("[SimGameState_CurResolvePerTurn_POSTFIX] __result AFTER: " + __result);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    // Fix another wrong calculation for Resolve/Turn
    [HarmonyPatch(typeof(Team), "CollectSimGameBaseline")]
    public static class Team_CollectSimGameBaseline_Patch
    {
        public static void Postfix(Team __instance, MoraleConstantsDef moraleConstants, ref int __result)
        {
            try
            {
                Logger.LogLine("[Team_CollectSimGameBaseline_POSTFIX] __result BEFORE: " + __result);

                if (moraleConstants.BaselineAddFromSimGame)
                {
                    for (int i = moraleConstants.BaselineAddFromSimGameThresholds.Length - 1; i >= 0; i--)
                    {
                        // Comparison was > but must be >=
                        if (__instance.CompanyMorale >= moraleConstants.BaselineAddFromSimGameThresholds[i])
                        {
                            __result = moraleConstants.BaselineAddFromSimGameValues[i];
                            break;
                        }
                    }
                }

                Logger.LogLine("[Team_CollectSimGameBaseline_POSTFIX] __result AFTER: " + __result);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    // Fix hardcoded(!) tooltip descriptions
    [HarmonyPatch(typeof(MoraleTooltipData), MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof(SimGameState), typeof(int) })]
    public static class MoraleTooltipData_Constructor_Patch
    {
        static void Postfix(MoraleTooltipData __instance, SimGameState sim, int moraleLevel)
        {
            try
            {
                SimGameState simGameState = sim ?? UnityGameInstance.BattleTechGame.Simulation;
                if (simGameState == null)
                {
                    return;
                }

                // Get
                //BaseDescriptionDef levelDescription = (BaseDescriptionDef)AccessTools.Property(typeof(MoraleTooltipData), "levelDescription").GetValue(__instance, null);

                string levelDescriptionName = "Morale: " + simGameState.GetDescriptorForMoraleLevel(moraleLevel);
                string levelDescriptionDetails = "At this level of morale, you will gain " + __instance.resolvePerTurn + " Resolve points per round of battle.";
                Logger.LogLine("[MoraleTooltipData_Constructor_POSTFIX] levelDescriptionName: " + levelDescriptionName);
                Logger.LogLine("[MoraleTooltipData_Constructor_POSTFIX] levelDescriptionDetails: " + levelDescriptionDetails);
                BaseDescriptionDef customLevelDescription = new BaseDescriptionDef("TooltipMoraleCustom", levelDescriptionName, levelDescriptionDetails, "");

                // Set
                //BaseDescriptionDef levelDescription = AccessTools.Property(typeof(MoraleTooltipData), "levelDescription").SetValue(__instance, customLevelDescription, null);
                new Traverse(__instance).Property("levelDescription").SetValue(customLevelDescription);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }




    [HarmonyPatch(typeof(SimGameState), "RemoveSimGameEventResult")]
    public static class SimGameState_RemoveSimGameEventResult_Patch
    {
        public static bool Prefix(SimGameState __instance, ref TemporarySimGameResult result)
        {
            try
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
                            string valuePostfix = int.Parse(result.Stats[i].value) > 0 ? "boost" : "penalty";

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
            catch (Exception e)
            {
                Logger.LogError(e);
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
            try
            {
                // Reset is not advised anymore as the new morale widget shows "Current Morale" in Quarters summary. Probably better to set regularly in LeftWidgetUpdate?
                /*
                int CurrentBaseMorale = __instance.GetCurrentBaseMorale();
                Logger.LogLine("[SimGameState_OnNewQuarterBegin_POSTFIX] Resetting Morale to CurrentBaseMorale(" + CurrentBaseMorale + ")");
                __instance.CompanyStats.ModifyStat<int>("Mission", 0, "COMPANY_MonthlyStartingMorale", StatCollection.StatOperation.Set, CurrentBaseMorale, -1, true);
                __instance.CompanyStats.ModifyStat<int>("Mission", 0, "Morale", StatCollection.StatOperation.Set, CurrentBaseMorale, -1, true);
                Logger.LogLine("----------------------------------------------------------------------------------------------------");
                */

                // A reset to the absolute morale value according to the new ruleset (and including temp events) is still advisory to "fix" savegames prior to this mod enabled...
                Logger.LogLine("[SimGameState_OnNewQuarterBegin_POSTFIX] Check before setting SimGameState.Morale: " + __instance.Morale.ToString());

                int CurrentAbsoluteMorale = __instance.GetCurrentAbsoluteMorale();
                //__instance.CompanyStats.ModifyStat<int>("Mission", 0, "COMPANY_MonthlyStartingMorale", StatCollection.StatOperation.Set, CurrentAbsoluteMorale, -1, true);
                __instance.CompanyStats.ModifyStat<int>("Mission", 0, "Morale", StatCollection.StatOperation.Set, CurrentAbsoluteMorale, -1, true);

                Logger.LogLine("[SimGameState_OnNewQuarterBegin_POSTFIX] Check after setting SimGameState.Morale: " + __instance.Morale.ToString());
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    // Flashpoint additions
    [HarmonyPatch(typeof(SGFlashpointEndScreen), "InitializeData")]
    public static class SGFlashpointEndScreen_InitializeData_Patch
    {
        public static void Prefix(SGFlashpointEndScreen __instance, ref SimGameEventResult[] eventResults)
        {
            try
            {
                Logger.LogLine("[SGFlashpointEndScreen_InitializeData_PREFIX] Check eventResults");

                // Prepare result for extraction of morale stat
                SimGameEventResult extractedMoraleResult = new SimGameEventResult();
                bool foundMoraleResult = false;

                foreach (SimGameEventResult eventResult in eventResults)
                {
                    if (eventResult.Stats != null)
                    {
                        for (int i = 0; i < eventResult.Stats.Length; i++)
                        {
                            if (eventResult.Stats[i].name == "Morale")
                            {
                                Logger.LogLine("[SGFlashpointEndScreen_InitializeData_PREFIX] eventResultMoraleValue (unaltered): " + eventResult.Stats[i].value);

                                // Mark eventResults[] as containing Morale Stat
                                foundMoraleResult = true;
                                // Create new temporary result with ONLY Morale Stat in it, value already modified with multiplier
                                extractedMoraleResult.Scope = EventScope.Company;
                                extractedMoraleResult.Requirements = new RequirementDef(null);
                                extractedMoraleResult.AddedTags = new HBS.Collections.TagSet();
                                extractedMoraleResult.RemovedTags = new HBS.Collections.TagSet();
                                int adjustedMoraleValue = (int.Parse(eventResult.Stats[i].value) * DynamicCompanyMorale.Settings.EventMoraleMultiplier);
                                SimGameStat Stat = new SimGameStat("Morale", adjustedMoraleValue, false);
                                extractedMoraleResult.Stats = new SimGameStat[] { Stat };
                                extractedMoraleResult.Actions = new SimGameResultAction[] { };
                                extractedMoraleResult.ForceEvents = new SimGameForcedEvent[] { };
                                extractedMoraleResult.TemporaryResult = true;
                                extractedMoraleResult.ResultDuration = DynamicCompanyMorale.Settings.EventMoraleDurationBase + Math.Abs(DynamicCompanyMorale.Settings.EventMoraleDurationNumerator / adjustedMoraleValue);
                                Logger.LogLine("[SGFlashpointEndScreen_InitializeData_PREFIX] extractedMoraleResultValue: " + extractedMoraleResult.Stats[0].value);

                                // Invalidate original morale stat
                                //eventResult.Stats[i].name = ""; // Breaks code
                                //eventResult.Stats[i].value = "0"; // This is enough
                                eventResult.Stats[i].typeString = ""; // Better
                                Logger.LogLine("[SGFlashpointEndScreen_InitializeData_PREFIX] eventResultIsValid: " + !String.IsNullOrEmpty(eventResult.Stats[i].typeString));
                            }
                        }
                    }
                }

                if (foundMoraleResult)
                {
                    Logger.LogLine("[SGFlashpointEndScreen_InitializeData_PREFIX] eventResults.Length (before): " + eventResults.Length.ToString());
                    Logger.LogLine("[SGFlashpointEndScreen_InitializeData_PREFIX] Adding extractedMoraleResult to eventResults now");

                    Array.Resize<SimGameEventResult>(ref eventResults, eventResults.Length + 1);
                    eventResults[eventResults.Length - 1] = extractedMoraleResult;

                    Logger.LogLine("[SGFlashpointEndScreen_InitializeData_PREFIX] eventResults.Length (after): " + eventResults.Length.ToString());

                    // Set early for UI
                    Fields.IsTemporaryMoraleEventActive = true;
                }
                Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
        public static void Postfix(SGFlashpointEndScreen __instance, SimGameState sim)
        {
            try
            {
                // At this point a morale event result was already applied
                // If companys morale was at a capped extreme before (0,50) the resulting morale is likely wrong
                // Example: Morale was displayed at 0 beforehand, with BaseMorale at 35, Expenditure at 0, and EventModifier at -40 -> real Morale was in the negative: -5
                // Event result of +16 is applied, resulting in 16 Morale
                // But, according to this mods logic, Morale *should* be at: 35 + 0 -40 + 16 = 11
                // So i set Morale to the correct (calculated) value again in this Postfix

                int CurrentAbsoluteMorale = sim.GetCurrentAbsoluteMorale();
                StatCollection statCol = sim.CompanyStats;
                SimGameStat stat = new SimGameStat("Morale", CurrentAbsoluteMorale, true);
                SimGameState.SetSimGameStat(stat, statCol);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    // This additional patch is needed because HBS calls SimGameState.BuildSimGameResults with a tenseOverride set :-\
    // Without patch the morale result is displayed as default without the TemporaryResult fluff
    // PROBABLY also possible with a POSTFIX of SimGameState.BuildSimGameResults (forcing all Stat.Morale results to temporary there?) -> TEST
    [HarmonyPatch(typeof(AAR_OtherResultsWidget), "DisplayData")]
    public static class AAR_OtherResultsWidget_DisplayData_Patch
    {
        public static void Prefix(AAR_OtherResultsWidget __instance, ref List<ResultDescriptionEntry> entries, TextMeshProUGUI ___resultsText)
        {
            try
            {
                ___resultsText.fontSize = 18; // Default is 20

                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    Logger.LogLine("[AAR_OtherResultsWidget_DisplayData_PREFIX] entries[i].Text: " + entries[i].Text.ToString());
                    string text = entries[i].Text.ToString();

                    if (text.Contains("Morale"))
                    {
                        string entry = Regex.Replace(text, @"\r\n?|\n", ""); // Kill linebreaks
                        string suffix = "";
                        int moraleValue = int.Parse(new String(text.Where(Char.IsDigit).ToArray()));
                        int duration = DynamicCompanyMorale.Settings.EventMoraleDurationBase + Math.Abs(DynamicCompanyMorale.Settings.EventMoraleDurationNumerator / moraleValue);
                        suffix += " for " + duration + " days";
                        entries[i].Text = new Text(entry + suffix, new object[0]);
                    }
                    Logger.LogLine("[AAR_OtherResultsWidget_DisplayData_PREFIX] entries[i].Text: " + entries[i].Text.ToString()); 
                }
                Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
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
                                    int adjustedMoraleValue = (int.Parse(eventResult.Stats[i].value) * DynamicCompanyMorale.Settings.EventMoraleMultiplier);
                                    SimGameStat Stat = new SimGameStat("Morale", adjustedMoraleValue, false);
                                    extractedMoraleResult.Stats = new SimGameStat[] { Stat };
                                    extractedMoraleResult.Actions = new SimGameResultAction[] { };
                                    extractedMoraleResult.ForceEvents = new SimGameForcedEvent[] { };
                                    extractedMoraleResult.TemporaryResult = true;
                                    extractedMoraleResult.ResultDuration = DynamicCompanyMorale.Settings.EventMoraleDurationBase + Math.Abs(DynamicCompanyMorale.Settings.EventMoraleDurationNumerator / adjustedMoraleValue);
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

            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
        public static void Postfix(SimGameState __instance, ref SimGameEventOption option)
        {
            try
            {
                // At this point a morale event result was already applied
                // If companys morale was at a capped extreme before (0,50) the resulting morale is likely wrong
                // Example: Morale was displayed at 0 beforehand, with BaseMorale at 35, Expenditure at 0, and EventModifier at -40 -> real Morale was in the negative: -5
                // Event result of +16 is applied, resulting in 16 Morale
                // But, according to this mods logic, Morale *should* be at: 35 + 0 -40 + 16 = 11
                // So i set Morale to the correct (calculated) value again in this Postfix

                int CurrentAbsoluteMorale = __instance.GetCurrentAbsoluteMorale();
                StatCollection statCol = __instance.CompanyStats;
                SimGameStat stat = new SimGameStat("Morale", CurrentAbsoluteMorale, true);
                SimGameState.SetSimGameStat(stat, statCol);
            }
            catch (Exception e)
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
            try
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

                Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    // The really dirty hack to override the wrong moraleValue tooltip during budget selection for next quarter (NextQuarterProjections)
    [HarmonyPatch(typeof(TooltipManager), "SpawnTooltip")]
    public static class TooltipManager_SpawnTooltip_Patch
    {
        public static void Prefix(TooltipManager __instance, ref object data)
        {
            try
            {
                if (data == null)
                {
                    return;
                }
                if (data.GetType().Name == "String")
                {
                    string tooltipText = (string)data;
                    bool IsMoraleValueTextTooltip = tooltipText.Contains("Resolve/Turn");
                    bool IsNextQuarterProjections = Fields.IsNextQuarterProjections;

                    if (IsMoraleValueTextTooltip && IsNextQuarterProjections)
                    {
                        Logger.LogLine("[TooltipManager_SpawnTooltip_PREFIX] tooltipText: " + tooltipText);
                        data = "You will generate <color=#F79B26>" + Fields.ProjectedResolvePerTurn + " Resolve/Turn</color> in combat";
                        Logger.LogLine("[TooltipManager_SpawnTooltip_PREFIX] Modified data: " + data);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    // BEN: This screen has actually 3 states (ScreenModes): DefaultStatus, QuarterlyReport, NextQuarterProjections. Keep in mind!
    [HarmonyPatch(typeof(SGCaptainsQuartersStatusScreen), "RefreshData")]
    public static class SGCaptainsQuartersStatusScreen_RefreshData_Patch
    {
        public static void Prefix(SGCaptainsQuartersStatusScreen __instance, bool showMoraleChange, SimGameState ___simState)
        {
            try
            {
                // This is only true on ScreenMode.NextQuarterProjections when ExpenditureLevel is selected
                if (showMoraleChange)
                {
                    // For (dirty) tooltip correction of moraleValueText when showing a projected value
                    Fields.IsNextQuarterProjections = true;
                    Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] Fields.IsNextQuarterProjections: " + Fields.IsNextQuarterProjections.ToString());

                    int ProjectedExpenditureMoraleValue = ___simState.ExpenditureMoraleValue[___simState.ExpenditureLevel];
                    Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] ProjectedExpenditureMoraleValue: " + ProjectedExpenditureMoraleValue);

                    int ProjectedMorale = ___simState.GetCurrentAbsoluteMorale();
                    Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] ProjectedMorale: " + ProjectedMorale);

                    MoraleConstantsDef moraleConstants = ___simState.CombatConstants.MoraleConstants;
                    for (int i = moraleConstants.BaselineAddFromSimGameThresholds.Length - 1; i >= 0; i--)
                    {
                        if (ProjectedMorale >= moraleConstants.BaselineAddFromSimGameThresholds[i])
                        {
                            Fields.ProjectedResolvePerTurn = moraleConstants.BaselineAddFromSimGameValues[i];
                            Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] Fields.ProjectedResolvePerTurn: " + Fields.ProjectedResolvePerTurn);
                            break;
                        }
                    }
                }
                else
                {
                    Fields.IsNextQuarterProjections = false;
                    Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] Fields.IsNextQuarterProjections: " + Fields.IsNextQuarterProjections.ToString());
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        public static void Postfix(SGCaptainsQuartersStatusScreen __instance, bool showMoraleChange, SGMoraleBar ___MoralBar, SimGameState ___simState)
        {
            try
            {
                //Logger.LogLine("[SGCaptainsQuartersStatusScreen_RefreshData_POSTFIX]");
                if (showMoraleChange)
                {
                    int CurrentMorale = ___simState.Morale;
                    // GetCurrentAbsoluteMorale incorporates ExpenditureMoraleValue
                    int ProjectedMorale = ___simState.GetCurrentAbsoluteMorale();
                    ___MoralBar.ShowMoraleChange(CurrentMorale, ProjectedMorale);
                }
                //Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }
    
    [HarmonyPatch(typeof(SGCaptainsQuartersStatusScreen), "Dismiss")]
    public static class SGCaptainsQuartersStatusScreen_Dismiss_Patch
    {
        public static void Prefix(SGCaptainsQuartersStatusScreen __instance, SGMoraleBar ___MoralBar, SimGameState ___simState)
        {
            try
            {
                Logger.LogLine("[SGCaptainsQuartersStatusScreen_Dismiss_PREFIX] Check before setting SimGameState.Morale: " + ___simState.Morale.ToString());

                int CurrentAbsoluteMorale = ___simState.GetCurrentAbsoluteMorale();
                //___simState.CompanyStats.ModifyStat<int>("Mission", 0, "COMPANY_MonthlyStartingMorale", StatCollection.StatOperation.Set, CurrentAbsoluteMorale, -1, true);
                ___simState.CompanyStats.ModifyStat<int>("Mission", 0, "Morale", StatCollection.StatOperation.Set, CurrentAbsoluteMorale, -1, true);

                Logger.LogLine("[SGCaptainsQuartersStatusScreen_Dismiss_PREFIX] Check after setting SimGameState.Morale: " + ___simState.Morale.ToString());



                // Workaround vanilla bug and saving just set ExpenseLevel in custom field too
                StatCollection companyStats = (StatCollection)AccessTools.Field(typeof(SimGameState), "companyStats").GetValue(___simState);
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] SimGameState.companyStats.ExpenseLevel: " + companyStats.GetValue<int>("ExpenseLevel"));
                Fields.SavedExpenseLevel = companyStats.GetValue<int>("ExpenseLevel");
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Fields.SavedExpenseLevel: " + Fields.SavedExpenseLevel);


                // Refresh dependent widgets!
                ___simState.RoomManager.RefreshLeftNavFromShipScreen();

                Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    [HarmonyPatch(typeof(SGFinancialForecastWidget), "RefreshData")]
    public static class SSGFinancialForecastWidget_RefreshData_Patch
    {
        public static void Postfix(SGFinancialForecastWidget __instance, LocalizableText ___CurrSpendingStyleText, SimGameState ___simState)
        {
            try
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
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    [HarmonyPatch(typeof(SGNavigationWidgetLeft), "UpdateData")]
    public static class SGNavigationWidgetLeft_UpdateData_Patch
    {
        public static void Postfix(SGNavigationWidgetLeft __instance, SimGameState ___simState)
        {
            try
            {
                // Validate that this is called FIRST after Load||Mission||Conversations... otherwise Save/Load has to be re-enabled!
                Fields.IsTemporaryMoraleEventActive = ___simState.IsTemporaryMoraleEventActive();
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.IsTemporaryMoraleEventActive: " + Fields.IsTemporaryMoraleEventActive);

                Fields.EventMoraleModifier = ___simState.GetAbsoluteMoraleValueOfAllTemporaryResults();
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.EventMoraleModifier: " + Fields.EventMoraleModifier);

                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.Morale: " + ___simState.Morale.ToString());
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check CurrentAbsoluteMorale(uncapped): " + ___simState.GetCurrentAbsoluteMorale(false).ToString());
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.ExpenditureLevel: " + ___simState.ExpenditureLevel.ToString());

                StatCollection companyStats = (StatCollection)AccessTools.Field(typeof(SimGameState), "companyStats").GetValue(___simState);
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.companyStats.ExpenseLevel: " + companyStats.GetValue<int>("ExpenseLevel"));
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.SavedExpenseLevel: " + Fields.SavedExpenseLevel);



                // Test
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Fields.ShouldFixExpenseLevel: " + Fields.ShouldFixExpenseLevel);
                if (Fields.ShouldFixExpenseLevel == true)
                {
                    ___simState.FixExpenditureLevel();
                    Fields.ShouldFixExpenseLevel = false;
                }

                // Set correct morale NOW
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Calling SimGameState.SetMorale NOW");
                int CurrentAbsoluteMorale = ___simState.GetCurrentAbsoluteMorale();
                ___simState.SetMorale(CurrentAbsoluteMorale);
                Logger.LogLine("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.Morale: " + ___simState.Morale.ToString());

                Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

    [HarmonyPatch(typeof(SGMoraleBar), "RefreshTextForMoraleValue")]
    public static class SGMoraleBar_RefreshTextForMoraleValue_Patch
    {
        public static void Postfix(SGMoraleBar __instance, int value, SimGameState ___simState)
        {
            try
            {
                int EventMoraleModifier = Fields.EventMoraleModifier;

                // Works too, not sure if there's a difference in performance...
                //TextMeshProUGUI moraleValueText = Traverse.Create(__instance).Field("moraleValueText").GetValue<TextMeshProUGUI>();
                //TextMeshProUGUI moraleTitleText = Traverse.Create(__instance).Field("moraleTitleText").GetValue<TextMeshProUGUI>();

                LocalizableText moraleValueText = (LocalizableText)AccessTools.Field(typeof(SGMoraleBar), "moraleValueText").GetValue(__instance);
                LocalizableText moraleTitleText = (LocalizableText)AccessTools.Field(typeof(SGMoraleBar), "moraleTitleText").GetValue(__instance);
                Color color;
                int moraleLevelFromMoraleValue = ___simState.GetMoraleLevelFromMoraleValue(value);
                string text = "MORALE: " + ___simState.GetDescriptorForMoraleLevel(moraleLevelFromMoraleValue);

                //Logger.LogLine("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] IsTemporaryMoraleEventActive: " + ___simState.IsTemporaryMoraleEventActive().ToString());

                if (EventMoraleModifier > 0)
                {
                    color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.green;
                    Logger.LogLine("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] Green");
                    text += " (+" + EventMoraleModifier.ToString() + " from events)";
                }
                else if (EventMoraleModifier < 0)
                {
                    color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.red;
                    Logger.LogLine("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] Red");
                    text += " (" + EventMoraleModifier.ToString() + " from events)";
                }
                // Zero from two or more temp results
                else if (Fields.IsTemporaryMoraleEventActive)
                {
                    color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.blue;
                    Logger.LogLine("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] Blue");
                    text += " (+ - " + EventMoraleModifier.ToString() + " from events)";
                }
                // Zero from no active temp results
                else
                {
                    color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.white;
                    Logger.LogLine("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] White");
                }
                //moraleValueText.color = color;
                moraleValueText.faceColor = color;
                moraleTitleText.SetText("{0}", new object[]
                {
                text
                });

                Logger.LogLine("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }



    // Add custom methods to SimGameState
    public static class Custom
    {
        // Fix potential vanilla bug with randomly setting ExpenseLevel at GameLoad
        public static void FixExpenditureLevel(this SimGameState simGameState)
        {
            StatCollection companyStats = (StatCollection)AccessTools.Field(typeof(SimGameState), "companyStats").GetValue(simGameState);
            int ExpectedExpenseLevel = Fields.SavedExpenseLevel;
            Logger.LogLine("[Custom.FixExpenditureLevel] ExpectedExpenseLevel: " + ExpectedExpenseLevel.ToString());

            if (companyStats.ContainsStatistic("ExpenseLevel"))
            {
                int CurrentExpenseLevel = companyStats.GetValue<int>("ExpenseLevel");
                Logger.LogLine("[Custom.FixExpenditureLevel] CurrentExpenseLevel: " + CurrentExpenseLevel.ToString());
                if (CurrentExpenseLevel != ExpectedExpenseLevel)
                {
                    Logger.LogLine("[Custom.FixExpenditureLevel] APPLY FIX");
                    companyStats.ModifyStat("Expenditure Change", 0, "ExpenseLevel", StatCollection.StatOperation.Set, ExpectedExpenseLevel, -1, true);
                }
            }
            else
            {
                companyStats.AddStatistic("ExpenseLevel", ExpectedExpenseLevel);
            }
        }

        public static void SetMorale(this SimGameState simGameState, int val)
        {
            StatCollection companyStats = (StatCollection)AccessTools.Field(typeof(SimGameState), "companyStats").GetValue(simGameState);

            if (companyStats.ContainsStatistic("Morale"))
            {
                companyStats.ModifyStat<int>("SimGameState", 0, "Morale", StatCollection.StatOperation.Set, val, -1, true);
            }
            else
            {
                companyStats.AddStatistic<int>("Morale", val, new Statistic.Validator<int>(simGameState.MinimumZeroMaximumFiftyValidator<int>));
            }
        }

        public static int GetCurrentAbsoluteMorale(this SimGameState simGameState, bool CapAtLimits = true)
        {
            int CurrentBaseMorale = simGameState.GetCurrentBaseMorale();
            int CurrentExpenditureMoraleValue = simGameState.ExpenditureMoraleValue[simGameState.ExpenditureLevel];
            int EventMoraleModifier = Fields.EventMoraleModifier;

            int CurrentAbsoluteMorale = CurrentBaseMorale + CurrentExpenditureMoraleValue + EventMoraleModifier;
            Logger.LogLine("[Custom.GetCurrentAbsoluteMorale]" + CurrentAbsoluteMorale.ToString());

            if (CapAtLimits)
            {
                Logger.LogLine("[Custom.GetCurrentAbsoluteMorale] CapAtLimits: " + CapAtLimits.ToString());
                if (CurrentAbsoluteMorale < 0)
                {
                    CurrentAbsoluteMorale = 0;
                }
                else if (CurrentAbsoluteMorale > 50)
                {
                    CurrentAbsoluteMorale = 50;
                }
            }
            return CurrentAbsoluteMorale;
        }

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
