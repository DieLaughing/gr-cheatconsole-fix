using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GRCheatConsoleFix;

// Companion fix for "Global Rescue Cheat Console Rebuild" 2.5.5.
//
// Global Rescue's code is obfuscated and every game update re-scrambles member names, so the
// console's hardcoded names (e.g. FinanceManager.PGPEPOJPPNB) break on each patch. This plugin
// replaces the broken console (and EmployeeOnlyPatch) actions with versions that locate game
// members by shape instead of by name:
//  - resources: each manager keeps its value in a single private field encoded with a single
//    const multiplier (money = double * 13.5, trophies = int * 123, research = int * 417), and
//    raises an unobfuscated event (onMoneyChanged, ...) that the UI and other managers listen to;
//  - employees: members are learned from the game's own save routine (see SaveMap);
//  - bases / mission cooldowns: the only collection of the right element type on the manager.
[BepInPlugin("justinpints.globalrescue.cheatconsolefix", "GR Cheat Console Fix", "1.2.1")]
[BepInDependency(ConsoleGuid)]
[BepInDependency(BridgeGuid)]
public class Plugin : BaseUnityPlugin
{
    private const string ConsoleGuid = "unknowngamer.globalrescue.cheatconsole.rebuild";
    private const string ConsoleType = "UnknownGamer.GlobalRescueCheatConsole.GlobalRescueCheatConsole";
    private const string BridgeGuid = "ksm97.globalrescue.employeeonlypatch";
    private const string BridgeType = "Ksm97.GlobalRescueEmployeeOnlyPatch.EmployeeOnlyPatch";
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        var harmony = new Harmony("justinpints.globalrescue.cheatconsolefix");

        var console = AccessTools.TypeByName(ConsoleType);
        if (console == null)
        {
            Log.LogError("Cheat Console type not found; nothing patched.");
            return;
        }
        Patch(harmony, console, "SetMoney", nameof(SetMoneyPrefix));
        Patch(harmony, console, "SetResearch", nameof(SetResearchPrefix));
        Patch(harmony, console, "SetReputation", nameof(SetReputationPrefix));
        Patch(harmony, console, "MakeEmployeesFreeFromManager", nameof(MakeEmployeesFreePrefix));
        Patch(harmony, console, "ZeroEmployeeFatigueFromManager", nameof(ZeroEmployeeFatiguePrefix));
        Patch(harmony, console, "GetEmployeeList", nameof(GetEmployeeListPrefix));
        Patch(harmony, console, "EnableAllBaseDepartments", nameof(EnableAllBaseDepartmentsPrefix));
        Patch(harmony, console, "ClearDictionary", nameof(ClearDictionaryPrefix));
        Patch(harmony, console, "FreezeEmployeeStatsTick", nameof(FreezeTickPrefix));
        PatchFreeWages(harmony);

        var employeeManager = AccessTools.TypeByName("EmployeeManager");
        var updateSatisfaction = employeeManager == null ? null : AccessTools.Method(employeeManager, "UpdateSatisfactionOfEmployees");
        if (updateSatisfaction != null)
            harmony.Patch(updateSatisfaction, postfix: new HarmonyMethod(typeof(Employees), nameof(Employees.UpdateSatisfactionPostfix)));
        else
            Log.LogWarning("EmployeeManager.UpdateSatisfactionOfEmployees not found; frozen satisfaction may flicker.");

        var bridge = AccessTools.TypeByName(BridgeType);
        if (bridge == null)
        {
            Log.LogError("EmployeeOnlyPatch type not found; employee burst/freeze and time fixes skipped.");
            return;
        }
        Patch(harmony, bridge, "ApplyEmployees", nameof(ApplyEmployeesPrefix));
        Patch(harmony, bridge, "ApplyEmployeeSkillsOnly", nameof(ApplyEmployeeSkillsOnlyPrefix));
        Patch(harmony, bridge, "GatherEmployees", nameof(GatherEmployeesPrefix));
        Patch(harmony, bridge, "LogFirstEmployeeDiagnostic", nameof(SkipPrefix));
        Patch(harmony, bridge, "ForceCurrentDateTime", nameof(ForceCurrentDateTimePrefix));
        Patch(harmony, bridge, "FreezeClockTickBridge", nameof(FreezeClockTickPrefix));
    }

    private static void PatchFreeWages(Harmony harmony)
    {
        var uiData = AccessTools.TypeByName("EmployeeUIData");
        var minSalary = uiData == null ? null : AccessTools.PropertyGetter(uiData, "MinSalary");
        if (minSalary != null)
            harmony.Patch(minSalary, postfix: new HarmonyMethod(typeof(Employees), nameof(Employees.MinSalaryPostfix)));
        else
            Log.LogWarning("EmployeeUIData.MinSalary not found; the wage slider may undo free wages.");

        int loaders = 0;
        foreach (var load in Employees.LoadMethods())
        {
            harmony.Patch(load, postfix: new HarmonyMethod(typeof(Employees), nameof(Employees.LoadPostfix)));
            loaders++;
        }
        if (loaders == 0)
            Log.LogWarning("Employee save loader not found; free wages reset when a save loads.");
    }

    private static void Patch(Harmony harmony, Type type, string method, string prefix)
    {
        var target = AccessTools.Method(type, method);
        if (target == null)
        {
            Log.LogWarning($"{type.Name}.{method} not found; left unpatched.");
            return;
        }
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(Plugin), prefix));
        Log.LogDebug($"Replaced {type.Name}.{method}.");
    }

    // ---- Console actions ----

    private static bool SetMoneyPrefix(object __instance)
    {
        double value = ParseDouble(ReadConsoleField(__instance, "_money"), 9999999.0);
        SetStatus(__instance, WriteEncoded("FinanceManager", typeof(double), value, "onMoneyChanged", "Money"));
        return false;
    }

    private static bool SetResearchPrefix(object __instance)
    {
        int value = ParseInt(ReadConsoleField(__instance, "_research"), 999999);
        SetStatus(__instance, WriteEncoded("ResearchManager", typeof(int), value, "onResearchPointsChanged", "Research"));
        return false;
    }

    // Reputation in the console is the game's trophy count. LevelManager listens to
    // onTrophiesChanged and recomputes the level from it, so raising the event is enough.
    private static bool SetReputationPrefix(object __instance)
    {
        int value = ParseInt(ReadConsoleField(__instance, "_reputation"), 9999);
        SetStatus(__instance, WriteEncoded("FinanceManager", typeof(int), value, "onTrophiesChanged", "Reputation"));
        return false;
    }

    private static bool MakeEmployeesFreePrefix(object __instance)
    {
        SetStatus(__instance, Report(Employees.MakeFree()));
        return false;
    }

    private static bool ZeroEmployeeFatiguePrefix(object __instance)
    {
        SetStatus(__instance, Report(Employees.ZeroNeeds()));
        return false;
    }

    private static bool GetEmployeeListPrefix(ref List<object> __result)
    {
        __result = Employees.GetAll();
        return false;
    }

    private static bool EnableAllBaseDepartmentsPrefix(object __instance)
    {
        SetStatus(__instance, Report(EnableAllBaseDepartments()));
        return false;
    }

    // The console's "No Call Cooldowns" clears CallManager.<obfuscated>. The real cooldown is
    // MissionManager's "missionId-location" -> last spawn time map, whose keys are excluded when
    // new calls spawn. Other ClearDictionary uses fall through to the original.
    private static bool ClearDictionaryPrefix(object __instance, string typeName)
    {
        if (typeName != "CallManager")
            return true;
        SetStatus(__instance, Report(ClearMissionCooldowns()));
        return false;
    }

    // ---- EmployeeOnlyPatch actions ----

    // verbose = the "Free + Max + No Fatigue" button; otherwise it's the bridge's burst/freeze loop.
    private static bool ApplyEmployeesPrefix(bool verbose)
    {
        if (verbose)
            Report(Employees.ApplyAll());
        else
            Employees.FreezeTick();
        return false;
    }

    // The console's freeze toggle ran free + max + needs as three separate passes every second.
    private static bool FreezeTickPrefix()
    {
        Employees.FreezeTick();
        return false;
    }

    private static bool ApplyEmployeeSkillsOnlyPrefix()
    {
        Report(Employees.MaxStats());
        return false;
    }

    private static bool GatherEmployeesPrefix(ref List<object> __result)
    {
        __result = Employees.GetAll();
        return false;
    }

    private static bool SkipPrefix() => false;

    private static float _nextClockTick;
    private static int _lastClockTarget = -1;

    // The console calls this every frame while Freeze Clock is on, and each call searches the
    // scene, refreshes the skybox and writes a log line. TimeManager.Update is already gated off
    // during the freeze, so re-applying once a second (or when the target time changes) is enough.
    private static bool FreezeClockTickPrefix(int hour, int minute)
    {
        float now = Time.realtimeSinceStartup;
        int target = hour * 60 + minute;
        if (target == _lastClockTarget && now < _nextClockTick)
            return false;
        _lastClockTarget = target;
        _nextClockTick = now + 1f;
        return true;
    }

    // The bridge wrote the clock through an obfuscated static field; use the public setter.
    private static bool ForceCurrentDateTimePrefix(Type timeType, DateTime target)
    {
        var setTime = timeType == null ? null : AccessTools.Method(timeType, "SetTime", new[] { typeof(DateTime) });
        if (setTime == null)
            return true;
        setTime.Invoke(null, new object[] { target });
        return false;
    }

    // ---- Bases / missions ----

    private static string EnableAllBaseDepartments()
    {
        var baseType = AccessTools.TypeByName("Base");
        var managerType = AccessTools.TypeByName("BaseManager");
        if (baseType == null || managerType == null)
            return Fail("Base departments: Base/BaseManager type not found.");

        string[] flags = { "_hasPolice", "_hasFirefighter", "_hasAmbulance", "_hasSwat" };
        var flagFields = flags.Select(f => AccessTools.Field(baseType, f)).Where(f => f != null).ToList();
        var bases = CollectionsOf(managerType, baseType).Distinct().ToList();
        if (bases.Count == 0)
            return Fail("Base departments: no bases found. Load a save first.");

        int set = 0;
        foreach (var b in bases)
        {
            foreach (var f in flagFields)
            {
                f.SetValue(b, true);
                set++;
            }
        }
        return $"Base departments: {bases.Count} base(s), {set} flag(s) set.";
    }

    private static string ClearMissionCooldowns()
    {
        var managerType = AccessTools.TypeByName("MissionManager");
        if (managerType == null)
            return Fail("Call cooldowns: MissionManager type not found.");

        var dicts = managerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(Dictionary<string, DateTime>))
            .ToList();
        if (dicts.Count != 1)
            return Fail($"Call cooldowns: expected 1 cooldown map on MissionManager, found {dicts.Count}.");

        int cleared = 0;
        foreach (var manager in FindLiveInstances(managerType))
        {
            if (dicts[0].GetValue(manager) is IDictionary d)
            {
                cleared += d.Count;
                d.Clear();
            }
        }
        return $"Call cooldowns cleared: {cleared} mission/location entr{(cleared == 1 ? "y" : "ies")}.";
    }

    // Items of every List<T>/HashSet<T> field on live managerType instances where T is elementType.
    private static IEnumerable<object> CollectionsOf(Type managerType, Type elementType)
    {
        var fields = managerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(f.FieldType) &&
                        f.FieldType.GetGenericArguments().Length == 1 &&
                        elementType.IsAssignableFrom(f.FieldType.GetGenericArguments()[0]))
            .ToList();
        foreach (var manager in FindLiveInstances(managerType))
        {
            foreach (var f in fields)
            {
                if (f.GetValue(manager) is IEnumerable items)
                {
                    foreach (var item in items)
                    {
                        if (item != null)
                            yield return item;
                    }
                }
            }
        }
    }

    // ---- Shape-based member resolution ----

    private static string WriteEncoded(string managerName, Type valueType, double value, string eventName, string label)
    {
        var managerType = AccessTools.TypeByName(managerName);
        if (managerType == null)
            return Fail($"{label}: type {managerName} not found.");

        if (!TryFindEncodedStore(managerType, valueType, out var store, out var scale))
            return Fail($"{label}: couldn't identify the encoded {valueType.Name} field on {managerName} (game update changed its layout?).");

        var managers = FindLiveInstances(managerType);
        if (managers.Count == 0)
            return Fail($"{label}: no live {managerName}. Load a save first.");

        object stored;
        object reported;
        if (valueType == typeof(double))
        {
            stored = value * Convert.ToDouble(scale);
            reported = value;
        }
        else
        {
            // Keep value * scale inside int range so the encoded field can't overflow.
            int s = Convert.ToInt32(scale);
            int clamped = (int)Math.Max(0, Math.Min(value, int.MaxValue / Math.Abs(s)));
            stored = clamped * s;
            reported = clamped;
        }

        var evt = managerType.GetField(eventName, All);
        foreach (var manager in managers)
        {
            store.SetValue(manager, stored);
            try
            {
                (evt?.GetValue(manager) as Delegate)?.DynamicInvoke(reported);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"{label}: {eventName} listener threw: {(ex.InnerException ?? ex).Message}");
            }
        }

        string msg = $"{label} set to {reported} ({managerName}.{store.Name}, x{scale}).";
        Log.LogInfo(msg);
        return msg;
    }

    // The value lives in the class's only private, writable instance field of valueType
    // (ignoring auto-property backing fields) and is multiplied by its only const of that type.
    private static bool TryFindEncodedStore(Type type, Type valueType, out FieldInfo store, out object scale)
    {
        var fields = type.GetFields(All).Where(f => f.FieldType == valueType).ToList();
        var consts = fields.Where(f => f.IsLiteral).ToList();
        var stores = fields.Where(f => !f.IsStatic && !f.IsLiteral && !f.IsInitOnly && !f.IsPublic &&
                                       !f.Name.StartsWith("<")).ToList();

        store = null;
        scale = null;
        if (consts.Count == 1 && stores.Count == 1)
        {
            store = stores[0];
            scale = consts[0].GetRawConstantValue();
            return true;
        }

        Log.LogWarning($"{type.Name}: expected 1 const + 1 store of {valueType.Name}, found consts=[" +
                       string.Join(", ", consts.Select(f => f.Name)) + "] stores=[" +
                       string.Join(", ", stores.Select(f => f.Name)) + "]");
        return false;
    }

    internal static List<UnityEngine.Object> FindLiveInstances(Type type)
    {
        return Resources.FindObjectsOfTypeAll(type)
            .Where(o => o is Component c && c != null && c.gameObject.scene.IsValid())
            .ToList();
    }

    // ---- Console plumbing ----

    private static string ReadConsoleField(object console, string field) =>
        AccessTools.Field(console.GetType(), field)?.GetValue(console) as string;

    private static void SetStatus(object console, string status) =>
        AccessTools.Field(console.GetType(), "_status")?.SetValue(console, status);

    private static readonly Dictionary<string, float> LastLogged = new Dictionary<string, float>();

    // Logs at most once per 10s per message, since the freeze toggles re-run actions every second.
    private static string Report(string msg)
    {
        float now = Time.realtimeSinceStartup;
        if (!LastLogged.TryGetValue(msg, out var at) || now - at > 10f)
        {
            LastLogged[msg] = now;
            Log.LogInfo(msg);
        }
        return msg;
    }

    private static string Fail(string msg)
    {
        Log.LogWarning(msg);
        return msg;
    }

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(Clean(s), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int ParseInt(string s, int fallback) =>
        double.TryParse(Clean(s), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, v))
            : fallback;

    private static string Clean(string s) => (s ?? "").Replace(",", "").Replace("_", "").Trim();
}
