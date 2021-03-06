﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BattleTech;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using BattleTech.UI;
using BattleTech.UI.TMProWrapper;
using BattleTech.UI.Tooltips;
using Harmony;
using HBS;
using Localize;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

namespace DynamicCompanyMorale
{
    public static class DynamicCompanyMorale
    {
        internal static string LogPath;
        internal static string ModDirectory;
        internal static Settings Settings;
        // BEN: DebugLevel (0: nothing, 1: error, 2: debug, 3: info)
        internal static int DebugLevel = 1;

        internal static SimGameEventOption LastModifiedOption;
        internal static bool EnableEventGenerator = false;

        public static void Init(string directory, string settings)
        {
            ModDirectory = directory;
            LogPath = Path.Combine(ModDirectory, "DynamicCompanyMorale.log");

            Logger.Initialize(LogPath, DebugLevel, ModDirectory, nameof(DynamicCompanyMorale));

            try
            {
                Settings = JsonConvert.DeserializeObject<Settings>(settings);
            }
            catch (Exception e)
            {
                Settings = new Settings();
                Logger.Error(e);
            }

            // Harmony calls need to go last here because their Prepare() methods directly check Settings...
            HarmonyInstance harmony = HarmonyInstance.Create("de.mad.DynamicCompanyMorale");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
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



    [HarmonyPatch(typeof(SimGameState), "RemoveSimGameEventResult")]
    public static class SimGameState_RemoveSimGameEventResult_Patch
    {
        public static bool Prefix(SimGameState __instance, ref TemporarySimGameResult result)
        {
            try
            {
                bool SkipOriginalMethod = false;

                Logger.Debug("[SimGameState_RemoveSimGameEventResult_PREFIX] Will handle result with Stat.name Morale (if any)");
                if (result.Stats != null)
                {
                    for (int i = 0; i < result.Stats.Length; i++)
                    {
                        if (result.Stats[i].name == "Morale")
                        {
                            Logger.Debug("[SimGameState_RemoveSimGameEventResult_PREFIX] Found TemporarySimGameResult with Stat.name Morale(" + result.Stats[i].value + ")");

                            int AdjustedEventResultMoraleValue = (-1) * int.Parse(result.Stats[i].value);
                            Logger.Debug("[SimGameState_RemoveSimGameEventResult_PREFIX] AdjustedEventResultMoraleValue: " + AdjustedEventResultMoraleValue.ToString());

                            // Modify Fields.EventMoraleModifier;
                            //Logger.Debug("[SimGameState_RemoveSimGameEventResult_PREFIX] Fields.EventMoraleModifier (before): " + Fields.EventMoraleModifier.ToString());

                            int CurrentEventMoraleModifier = Fields.EventMoraleModifier;
                            int UpdatedEventMoraleModifier = CurrentEventMoraleModifier + AdjustedEventResultMoraleValue;
                            //Fields.EventMoraleModifier = UpdatedEventMoraleModifier;

                            //Logger.Debug("[SimGameState_RemoveSimGameEventResult_PREFIX] Fields.EventMoraleModifier (after): " + Fields.EventMoraleModifier.ToString());


                            // Set "Morale" stat directly
                            int CurrentBaseMorale = __instance.GetCurrentBaseMorale();
                            int CurrentExpenditureMoraleValue = __instance.ExpenditureMoraleValue[__instance.ExpenditureLevel];
                            int CorrectedMoraleRating = CurrentBaseMorale + CurrentExpenditureMoraleValue + UpdatedEventMoraleModifier;
                            StatCollection statCol = __instance.CompanyStats;
                            SimGameStat stat = new SimGameStat("Morale", CorrectedMoraleRating, true);
                            SimGameState.SetSimGameStat(stat, statCol);


                            // Push message out
                            Logger.Debug("[SimGameState_RemoveSimGameEventResult_PREFIX] Call SimGameState.RoomManager.ShipRoom.AddEventToast with custom message");
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
                    Logger.Debug("[SimGameState_RemoveSimGameEventResult_PREFIX] Handled event with Stat.name Morale, will skip original method...");
                    Logger.Debug("----------------------------------------------------------------------------------------------------");
                    return false;
                }
                else
                {
                    Logger.Debug("----------------------------------------------------------------------------------------------------");
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                Logger.Debug("[SimGameState_OnNewQuarterBegin_POSTFIX] Resetting Morale to CurrentBaseMorale(" + CurrentBaseMorale + ")");
                __instance.CompanyStats.ModifyStat<int>("Mission", 0, "COMPANY_MonthlyStartingMorale", StatCollection.StatOperation.Set, CurrentBaseMorale, -1, true);
                __instance.CompanyStats.ModifyStat<int>("Mission", 0, "Morale", StatCollection.StatOperation.Set, CurrentBaseMorale, -1, true);
                Logger.Debug("----------------------------------------------------------------------------------------------------");
                */

                // A reset to the absolute morale value according to the new ruleset (and including temp events) is still advisory to "fix" savegames prior to this mod enabled...
                Logger.Debug("[SimGameState_OnNewQuarterBegin_POSTFIX] Check before setting SimGameState.Morale: " + __instance.Morale.ToString());

                int CurrentAbsoluteMorale = __instance.GetCurrentAbsoluteMorale();
                //__instance.CompanyStats.ModifyStat<int>("Mission", 0, "COMPANY_MonthlyStartingMorale", StatCollection.StatOperation.Set, CurrentAbsoluteMorale, -1, true);
                __instance.CompanyStats.ModifyStat<int>("Mission", 0, "Morale", StatCollection.StatOperation.Set, CurrentAbsoluteMorale, -1, true);

                Logger.Debug("[SimGameState_OnNewQuarterBegin_POSTFIX] Check after setting SimGameState.Morale: " + __instance.Morale.ToString());
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                Logger.Debug("[SGFlashpointEndScreen_InitializeData_PREFIX] Check eventResults");

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
                                Logger.Debug("[SGFlashpointEndScreen_InitializeData_PREFIX] eventResultMoraleValue (unaltered): " + eventResult.Stats[i].value);

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
                                Logger.Debug("[SGFlashpointEndScreen_InitializeData_PREFIX] extractedMoraleResultValue: " + extractedMoraleResult.Stats[0].value);

                                // Invalidate original morale stat
                                //eventResult.Stats[i].name = ""; // Breaks code
                                //eventResult.Stats[i].value = "0"; // This is enough
                                eventResult.Stats[i].typeString = ""; // Better
                                Logger.Debug("[SGFlashpointEndScreen_InitializeData_PREFIX] eventResultIsValid: " + !String.IsNullOrEmpty(eventResult.Stats[i].typeString));
                            }
                        }
                    }
                }

                if (foundMoraleResult)
                {
                    Logger.Debug("[SGFlashpointEndScreen_InitializeData_PREFIX] eventResults.Length (before): " + eventResults.Length.ToString());
                    Logger.Debug("[SGFlashpointEndScreen_InitializeData_PREFIX] Adding extractedMoraleResult to eventResults now");

                    Array.Resize<SimGameEventResult>(ref eventResults, eventResults.Length + 1);
                    eventResults[eventResults.Length - 1] = extractedMoraleResult;

                    Logger.Debug("[SGFlashpointEndScreen_InitializeData_PREFIX] eventResults.Length (after): " + eventResults.Length.ToString());

                    // Set early for UI
                    Fields.IsTemporaryMoraleEventActive = true;
                }
                Logger.Debug("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                Logger.Error(e);
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
                    Logger.Debug("[AAR_OtherResultsWidget_DisplayData_PREFIX] entries[i].Text: " + entries[i].Text.ToString());
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
                    Logger.Debug("[AAR_OtherResultsWidget_DisplayData_PREFIX] entries[i].Text: " + entries[i].Text.ToString()); 
                }
                Logger.Debug("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] Selected option: " + option.Description.Id);
                foreach (SimGameEventResultSet eventResultSet in option.ResultSets)
                {
                    Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] Modifying morale value of possible outcome: " + eventResultSet.Description.Id + " (if any)");

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
                                    Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] eventResultMoraleValue (unaltered): " + eventResult.Stats[i].value);

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
                                    Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] extractedMoraleResultValue: " + extractedMoraleResult.Stats[0].value);

                                    // Invalidate original morale stat
                                    //eventResult.Stats[i].name = ""; // Breaks code
                                    //eventResult.Stats[i].value = "0"; // This is enough
                                    eventResult.Stats[i].typeString = ""; // Better
                                    Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] eventResultIsValid: " + !String.IsNullOrEmpty(eventResult.Stats[i].typeString));
                                }
                            }
                        }
                    }

                    if (foundMoraleResult)
                    {
                        Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] eventResultSet.Results.Length (before): " + eventResultSet.Results.Length.ToString());
                        Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] Adding extractedMoraleResult to eventResultSet.Results now");

                        Array.Resize<SimGameEventResult>(ref eventResultSet.Results, eventResultSet.Results.Length + 1);
                        eventResultSet.Results[eventResultSet.Results.Length - 1] = extractedMoraleResult;

                        Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] eventResultSet.Results.Length (after): " + eventResultSet.Results.Length.ToString());

                        // Set early for UI
                        Fields.IsTemporaryMoraleEventActive = true;
                    }
                }
                DynamicCompanyMorale.LastModifiedOption = option;
                // BEN: After the original method is called, everything is already in the TemporaryResultTracker & several "updateData"-Calls are made...
                Logger.Debug("----------------------------------------------------------------------------------------------------");

            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                Logger.Error(e);
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
                    Logger.Debug("[SimGameState_OnEventOptionSelected_PREFIX] Resetting morale value of saved possible outcome: " + eventResultSet.Description.Id + " (if any)");
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
                                    Logger.Debug("[SimGameState_OnEventDismissed_POSTFIX] eventResultMoraleValue: " + eventResult.Stats[i].value);
                                    Logger.Debug("[SimGameState_OnEventDismissed_POSTFIX] eventResultIsValid (before): " + !String.IsNullOrEmpty(eventResult.Stats[i].typeString));
                                    if (String.IsNullOrEmpty(eventResult.Stats[i].typeString))
                                    {
                                        eventResult.Stats[i].typeString = "System.Int32";
                                    }
                                    Logger.Debug("[SimGameState_OnEventDismissed_POSTFIX] eventResultIsValid (after): " + !String.IsNullOrEmpty(eventResult.Stats[i].typeString));
                                }
                            }
                        }
                    }
                    if (foundMoraleResult)
                    {
                        Logger.Debug("[SimGameState_OnEventDismissed_POSTFIX] eventResultSet.Results.Length (before): " + eventResultSet.Results.Length.ToString());
                        Logger.Debug("[SimGameState_OnEventDismissed_POSTFIX] Removing extractedMoraleResult from eventResultSet.Results now");

                        Array.Resize<SimGameEventResult>(ref eventResultSet.Results, eventResultSet.Results.Length - 1);

                        Logger.Debug("[SimGameState_OnEventDismissed_POSTFIX] eventResultSet.Results.Length (after): " + eventResultSet.Results.Length.ToString());
                    }
                }
                // Refresh dependent widgets
                __instance.RoomManager.RefreshLeftNavFromShipScreen();

                Logger.Debug("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                        Logger.Debug("[TooltipManager_SpawnTooltip_PREFIX] tooltipText: " + tooltipText);
                        data = "You will generate <color=#F79B26>" + Fields.ProjectedResolvePerTurn + " Resolve/Turn</color> in combat";
                        Logger.Debug("[TooltipManager_SpawnTooltip_PREFIX] Modified data: " + data);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }



    // BEN: This screen has actually 3 states (ScreenModes): DefaultStatus, QuarterlyReport, NextQuarterProjections. Keep in mind!
    [HarmonyPatch(typeof(SGCaptainsQuartersStatusScreen), "RefreshData")]
    public static class SGCaptainsQuartersStatusScreen_RefreshData_Patch
    {
        public static void Prefix(SGCaptainsQuartersStatusScreen __instance, EconomyScale expenditureLevel, bool showMoraleChange, SimGameState ___simState)
        {
            try
            {
                // This is only true on ScreenMode.NextQuarterProjections when ExpenditureLevel is selected
                if (showMoraleChange)
                {
                    // For (dirty) tooltip correction of moraleValueText when showing a projected value
                    Fields.IsNextQuarterProjections = true;
                    Logger.Debug("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] Fields.IsNextQuarterProjections: " + Fields.IsNextQuarterProjections.ToString());

                    int CurrentExpenditureMoraleValue = ___simState.ExpenditureMoraleValue[___simState.ExpenditureLevel];
                    Logger.Debug("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] CurrentExpenditureMoraleValue: " + CurrentExpenditureMoraleValue);

                    int ProjectedExpenditureMoraleValue = ___simState.ExpenditureMoraleValue[expenditureLevel];
                    Logger.Debug("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] ProjectedExpenditureMoraleValue: " + ProjectedExpenditureMoraleValue);

                    //int ProjectedMorale = ___simState.GetCurrentAbsoluteMorale() + ProjectedExpenditureMoraleValue;
                    int ProjectedMorale = (___simState.GetCurrentBaseMorale() + Fields.EventMoraleModifier) + ProjectedExpenditureMoraleValue;
                    Logger.Debug("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] ProjectedMorale: " + ProjectedMorale);

                    MoraleConstantsDef moraleConstants = ___simState.CombatConstants.MoraleConstants;
                    for (int i = moraleConstants.BaselineAddFromSimGameThresholds.Length - 1; i >= 0; i--)
                    {
                        if (ProjectedMorale >= moraleConstants.BaselineAddFromSimGameThresholds[i])
                        {
                            Fields.ProjectedResolvePerTurn = moraleConstants.BaselineAddFromSimGameValues[i];
                            Logger.Debug("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] Fields.ProjectedResolvePerTurn: " + Fields.ProjectedResolvePerTurn);
                            break;
                        }
                    }
                }
                else
                {
                    Fields.IsNextQuarterProjections = false;
                    Logger.Debug("[SGCaptainsQuartersStatusScreen_RefreshData_PREFIX] Fields.IsNextQuarterProjections: " + Fields.IsNextQuarterProjections.ToString());
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public static void Postfix(SGCaptainsQuartersStatusScreen __instance, EconomyScale expenditureLevel, bool showMoraleChange, SGMoraleBar ___MoralBar, SimGameState ___simState)
        {
            try
            {
                //Logger.Debug("[SGCaptainsQuartersStatusScreen_RefreshData_POSTFIX]");
                if (showMoraleChange)
                {
                    int CurrentMorale = ___simState.Morale;

                    //int ProjectedMorale = ___simState.GetCurrentAbsoluteMorale() + ___simState.ExpenditureMoraleValue[expenditureLevel];
                    int ProjectedMorale = (___simState.GetCurrentBaseMorale() + Fields.EventMoraleModifier) + ___simState.ExpenditureMoraleValue[expenditureLevel];

                    ___MoralBar.ShowMoraleChange(CurrentMorale, ProjectedMorale);
                }
                //Logger.Debug("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                Logger.Debug("[SGCaptainsQuartersStatusScreen_Dismiss_PREFIX] Check before setting SimGameState.Morale: " + ___simState.Morale.ToString());

                int CurrentAbsoluteMorale = ___simState.GetCurrentAbsoluteMorale();
                //___simState.CompanyStats.ModifyStat<int>("Mission", 0, "COMPANY_MonthlyStartingMorale", StatCollection.StatOperation.Set, CurrentAbsoluteMorale, -1, true);
                ___simState.CompanyStats.ModifyStat<int>("Mission", 0, "Morale", StatCollection.StatOperation.Set, CurrentAbsoluteMorale, -1, true);

                Logger.Debug("[SGCaptainsQuartersStatusScreen_Dismiss_PREFIX] Check after setting SimGameState.Morale: " + ___simState.Morale.ToString());



                // Workaround vanilla bug and saving just set ExpenseLevel in custom field too
                StatCollection companyStats = (StatCollection)AccessTools.Field(typeof(SimGameState), "companyStats").GetValue(___simState);
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] SimGameState.companyStats.ExpenseLevel: " + companyStats.GetValue<int>("ExpenseLevel"));
                Fields.SavedExpenseLevel = companyStats.GetValue<int>("ExpenseLevel");
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Fields.SavedExpenseLevel: " + Fields.SavedExpenseLevel);


                // Refresh dependent widgets!
                ___simState.RoomManager.RefreshLeftNavFromShipScreen();

                Logger.Debug("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                Logger.Error(e);
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
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.IsTemporaryMoraleEventActive: " + Fields.IsTemporaryMoraleEventActive);

                Fields.EventMoraleModifier = ___simState.GetAbsoluteMoraleValueOfAllTemporaryResults();
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.EventMoraleModifier: " + Fields.EventMoraleModifier);

                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.Morale: " + ___simState.Morale.ToString());
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check CurrentAbsoluteMorale(uncapped): " + ___simState.GetCurrentAbsoluteMorale(false).ToString());
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.ExpenditureLevel: " + ___simState.ExpenditureLevel.ToString());

                StatCollection companyStats = (StatCollection)AccessTools.Field(typeof(SimGameState), "companyStats").GetValue(___simState);
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.companyStats.ExpenseLevel: " + companyStats.GetValue<int>("ExpenseLevel"));
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check Fields.SavedExpenseLevel: " + Fields.SavedExpenseLevel);



                // Test
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Fields.ShouldFixExpenseLevel: " + Fields.ShouldFixExpenseLevel);
                if (Fields.ShouldFixExpenseLevel == true)
                {
                    ___simState.FixExpenditureLevel();
                    Fields.ShouldFixExpenseLevel = false;
                }

                // Set correct morale NOW
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Calling SimGameState.SetMorale NOW");
                int CurrentAbsoluteMorale = ___simState.GetCurrentAbsoluteMorale();
                ___simState.SetMorale(CurrentAbsoluteMorale);
                Logger.Debug("[SGNavigationWidgetLeft_UpdateData_POSTFIX] Check SimGameState.Morale: " + ___simState.Morale.ToString());

                Logger.Debug("----------------------------------------------------------------------------------------------------");
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
                String level = "";
                String title = ""; ;
                String details = "";

                int moraleLevelFromMoraleValue = ___simState.GetMoraleLevelFromMoraleValue(value);
                level = ___simState.GetDescriptorForMoraleLevel(moraleLevelFromMoraleValue);
                title = "MORALE: " + level;

                //Logger.Debug("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] IsTemporaryMoraleEventActive: " + ___simState.IsTemporaryMoraleEventActive().ToString());

                if (EventMoraleModifier > 0)
                {
                    color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.green;
                    Logger.Debug("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] Green");
                    title += " (+" + EventMoraleModifier.ToString() + " from events)";
                }
                else if (EventMoraleModifier < 0)
                {
                    color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.red;
                    Logger.Debug("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] Red");
                    title += " (" + EventMoraleModifier.ToString() + " from events)";
                }
                // Zero from two or more temp results
                else if (Fields.IsTemporaryMoraleEventActive)
                {
                    color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.blue;
                    Logger.Debug("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] Blue");
                    title += " (+ - " + EventMoraleModifier.ToString() + " from events)";
                }
                // Zero from no active temp results
                else
                {
                    color = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.white;
                    Logger.Debug("[SGMoraleBar_RefreshTextForMoraleValue_POSTFIX] White");
                }
                //moraleValueText.color = color;
                moraleValueText.faceColor = color;
                moraleTitleText.SetText("{0}", new object[]
                {
                    title
                });

                Logger.Debug("----------------------------------------------------------------------------------------------------");



                // Tooltip
                Color red = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.red;
                Color green = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.green;
                Color blue = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.blue;
                Color white = LazySingletonBehavior<UIManager>.Instance.UIColorRefs.white;

                details += $"<b><color=#{ColorUtility.ToHtmlStringRGBA(blue)}>BASE:</color> {___simState.Constants.Story.StartingMorale}</b>\n\n";
                details += "The neutral baseline for a professional company.";
                details += "\n\n";

                details += $"<b><color=#{ColorUtility.ToHtmlStringRGBA(blue)}>DROPSHIP:</color> {___simState.GetDropshipMoraleModifier()}</b>\n\n";
                details += "Many upgrades of your dropship will make life on a spaceship more comfortable.";
                details += "\n\n";

                int expenditureValue = ___simState.ExpenditureMoraleValue[___simState.ExpenditureLevel];
                Color expenditureValueColor = expenditureValue >= 0 ? white : red;
                details += $"<b><color=#{ColorUtility.ToHtmlStringRGBA(blue)}>EXPENDITURES:</color> <color=#{ColorUtility.ToHtmlStringRGBA(expenditureValueColor)}>{expenditureValue}</color></b>\n\n";
                details += "The available money for 'Mech maintenance and your Mechwarrior's salaries will strongly influence morale.";
                details += "\n\n";

                if (DynamicCompanyMorale.Settings.RespectPilotTags)
                {
                    int crewValue = ___simState.GetCrewMoraleModifier();
                    Color crewValueColor = crewValue >= 0 ? white : red;
                    details += $"<b><color=#{ColorUtility.ToHtmlStringRGBA(blue)}>CREW:</color> <color=#{ColorUtility.ToHtmlStringRGBA(crewValueColor)}>{crewValue}</color></b>\n\n";
                    details += "Some attributes of the mechwarriors you hire will have an impact on the overall morale of your crew.";
                    details += "\n\n";
                }

                int eventValue = ___simState.GetAbsoluteMoraleValueOfAllTemporaryResults();
                Color eventValueColor = eventValue >= 0 ? white : red;
                details += $"<b><color=#{ColorUtility.ToHtmlStringRGBA(blue)}>EVENTS:</color> <color=#{ColorUtility.ToHtmlStringRGBA(eventValueColor)}>{eventValue}</color></b>\n\n";
                details += "Certain events and their outcomes will <b>temporarily</b> rise or lower your company's morale.";
                details += "\n\n";



                GameObject moraleTitleTextGO = moraleTitleText.gameObject;
                HBSTooltip Tooltip = moraleTitleTextGO.AddComponent<HBSTooltip>();
                HBSTooltipStateData StateData = new HBSTooltipStateData();
                BaseDescriptionDef TooltipContent = new BaseDescriptionDef("", title, details, "");

                StateData.SetObject(TooltipContent);
                Tooltip.SetDefaultStateData(StateData);
            }
            catch (Exception e)
            {
                Logger.Error(e);
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
            Logger.Debug("[Custom.FixExpenditureLevel] ExpectedExpenseLevel: " + ExpectedExpenseLevel.ToString());

            if (companyStats.ContainsStatistic("ExpenseLevel"))
            {
                int CurrentExpenseLevel = companyStats.GetValue<int>("ExpenseLevel");
                Logger.Debug("[Custom.FixExpenditureLevel] CurrentExpenseLevel: " + CurrentExpenseLevel.ToString());
                if (CurrentExpenseLevel != ExpectedExpenseLevel)
                {
                    Logger.Debug("[Custom.FixExpenditureLevel] APPLY FIX");
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
            Logger.Debug("[Custom.GetCurrentAbsoluteMorale]" + CurrentAbsoluteMorale.ToString());

            if (CapAtLimits)
            {
                Logger.Debug("[Custom.GetCurrentAbsoluteMorale] CapAtLimits: " + CapAtLimits.ToString());
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
            int CurrentBaseMorale = simGameState.Constants.Story.StartingMorale;

            int DropshipMoraleModifier = simGameState.GetDropshipMoraleModifier();
            Logger.Info($"[Custom.GetCurrentBaseMorale] DropshipMoraleModifier: {DropshipMoraleModifier}");
            CurrentBaseMorale += DropshipMoraleModifier;


            if (DynamicCompanyMorale.Settings.RespectPilotTags) {
                int PilotsMoraleModifier = simGameState.GetCrewMoraleModifier();
                Logger.Info($"[Custom.GetCurrentBaseMorale] PilotsMoraleModifier: {PilotsMoraleModifier}");
                CurrentBaseMorale += PilotsMoraleModifier;
            }


            Logger.Debug("[Custom.GetCurrentBaseMorale]" + CurrentBaseMorale.ToString());

            return CurrentBaseMorale;
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
                                //Logger.Debug("[Custom.IsTemporaryMoraleEventActive] True");
                                IsTemporaryMoraleEventActive = true;
                            }
                        }
                    }
                }
            }
            return IsTemporaryMoraleEventActive;
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
                            Logger.Debug("[Custom.GetAbsoluteMoraleValueOfAllTemporaryResults] Check Stats: " + simGameStat.name + " (" + simGameStat.value + ") for " + (temporarySimGameResult.ResultDuration - temporarySimGameResult.DaysElapsed) + " days");
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



        public static int GetDropshipMoraleModifier(this SimGameState simGameState)
        {
            int result = 0;

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
                                result += modifier;
                            }
                        }
                    }
                }
            }
            return result;
        }



        public static int GetCrewMoraleModifier(this SimGameState simGameState)
        {
            int result = 0;

            foreach (Pilot pilot in simGameState.PilotRoster)
            {
                foreach (string tag in DynamicCompanyMorale.Settings.PositivePilotTags)
                {
                    if (pilot.pilotDef.PilotTags.Contains(tag))
                    {
                        Logger.Info($"[Custom.GetCrewMoraleModifier] {pilot.Callsign}: {tag}");
                        result++;
                    }
                }

                foreach (string tag in DynamicCompanyMorale.Settings.NegativePilotTags)
                {
                    if (pilot.pilotDef.PilotTags.Contains(tag))
                    {
                        Logger.Info($"[Custom.GetCrewMoraleModifier] {pilot.Callsign}: {tag}");
                        result--;
                    }
                }
            }

            return result;
        }
    }
}
