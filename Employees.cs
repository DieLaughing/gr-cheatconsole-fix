using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace GRCheatConsoleFix;

// Employee cheats. Members are resolved through Employee's save method (see SaveMap), so the
// names below are the game's own un-obfuscated save-file keys.
internal static class Employees
{
    // Needs run 0 (fine) .. 1 (desperate); e.g. shower > 0.75 turns on the stink particles.
    private static readonly string[] Needs = { "hunger", "thirst", "toilet", "shower", "tiredness" };

    private static Dictionary<string, MemberInfo> _map;
    private static Type _employeeType;

    private static bool EnsureMap()
    {
        if (_map != null)
            return _map.Count > 0;

        _employeeType = AccessTools.TypeByName("Employee");
        var saveType = AccessTools.TypeByName("EmployeeSaveData");
        if (_employeeType == null || saveType == null)
        {
            Plugin.Log.LogWarning("Employees: Employee/EmployeeSaveData type not found.");
            _map = new Dictionary<string, MemberInfo>();
            return false;
        }

        _map = SaveMap.Build(_employeeType, saveType);
        Plugin.Log.LogInfo("Employees: resolved " + _map.Count + " save fields (" +
                           string.Join(", ", _map.Select(kv => kv.Key + "=" + kv.Value.Name)) + ").");
        return _map.Count > 0;
    }

    public static List<object> GetAll()
    {
        var result = new List<object>();
        var managerType = AccessTools.TypeByName("EmployeeManager");
        if (managerType == null || !EnsureMap())
            return result;

        // Every List<Employee>/HashSet<Employee> the manager holds (hired staff and friends).
        var seen = new HashSet<object>();
        var collections = managerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => ElementType(f.FieldType) is Type t && _employeeType.IsAssignableFrom(t))
            .ToList();
        foreach (var manager in Plugin.FindLiveInstances(managerType))
        {
            foreach (var field in collections)
            {
                if (field.GetValue(manager) is IEnumerable items)
                {
                    foreach (var e in items)
                    {
                        if (e != null && seen.Add(e))
                            result.Add(e);
                    }
                }
            }
        }
        return result;
    }

    private static Type ElementType(Type t)
    {
        if (!t.IsGenericType || !typeof(IEnumerable).IsAssignableFrom(t))
            return null;
        var args = t.GetGenericArguments();
        return args.Length == 1 ? args[0] : null;
    }

    // Sets each named save field on every employee; returns (employees, writes).
    public static (int employees, int writes) Apply(params (string key, object value)[] values)
    {
        var employees = GetAll();
        int writes = 0;
        foreach (var e in employees)
        {
            foreach (var (key, value) in values)
            {
                if (!_map.TryGetValue(key, out var member))
                    continue;
                try
                {
                    if (SaveMap.TrySet(e, member, value))
                        writes++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Employees: {key} write failed: {(ex.InnerException ?? ex).Message}");
                }
            }
        }
        return (employees.Count, writes);
    }

    public static float? Read(object employee, string key) =>
        EnsureMap() && _map.TryGetValue(key, out var m) && SaveMap.TryGet(employee, m) is object v
            ? Convert.ToSingle(v)
            : (float?)null;

    public static bool IsFree(object employee) => Read(employee, "salary") is float w && w <= 0.001f;

    // ---- Keep free wages free ----

    // The employee panel's wage slider is two-way bound to EmployeeUIData.Salary with its low
    // end at MinSalary (= desiredSalary * 0.7), so showing a $0 employee clamps the wage back up.
    // Let the slider go to 0 for employees who were made free.
    public static void MinSalaryPostfix(object __instance, ref float __result)
    {
        try
        {
            if (AccessTools.Property(__instance.GetType(), "Employee")?.GetValue(__instance, null) is object e && IsFree(e))
                __result = 0f;
        }
        catch
        {
        }
    }

    // Loading a save clamps each wage into the job's min..max range. Keep saved $0 wages at $0.
    public static void LoadPostfix(object __instance, object[] __args)
    {
        try
        {
            if (__args.Length == 1 && __args[0] != null && EnsureMap() &&
                AccessTools.Field(__args[0].GetType(), "salary")?.GetValue(__args[0]) is float saved && saved <= 0.001f)
            {
                SaveMap.TrySet(__instance, _map["salary"], 0f);
            }
        }
        catch
        {
        }
    }

    // Every Employee method taking just an EmployeeSaveData (the loader, plus any decoys).
    public static IEnumerable<MethodInfo> LoadMethods()
    {
        var employee = AccessTools.TypeByName("Employee");
        var save = AccessTools.TypeByName("EmployeeSaveData");
        if (employee == null || save == null)
            return Enumerable.Empty<MethodInfo>();
        return employee.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsAbstract && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == save);
    }

    public static void RefreshSatisfaction()
    {
        var managerType = AccessTools.TypeByName("EmployeeManager");
        var update = managerType == null ? null : AccessTools.Method(managerType, "UpdateSatisfactionOfEmployees");
        if (update == null)
            return;
        foreach (var manager in Plugin.FindLiveInstances(managerType))
        {
            try { update.Invoke(manager, null); }
            catch (Exception ex) { Plugin.Log.LogDebug("UpdateSatisfactionOfEmployees: " + (ex.InnerException ?? ex).Message); }
        }
    }

    public static (string key, object value)[] NeedsZero() => Needs.Select(n => (n, (object)0f)).ToArray();

    // ---- Actions (same effects the original mods intended) ----

    // Only the wage goes to 0. The game rates pay as salary / desiredSalary - 1, so zeroing the
    // desired salary as well (as the original console did) produces NaN; keep it at least 1.
    public static string MakeFree()
    {
        var (n, w) = Apply(("salary", 0f));
        EnsureDesiredSalary();
        return $"Employee free: {n} employee(s), {w} wage(s) zeroed.";
    }

    private static void EnsureDesiredSalary()
    {
        if (!_map.TryGetValue("desiredSalary", out var desired))
            return;
        foreach (var e in GetAll())
        {
            if ((Read(e, "desiredSalary") ?? 1f) < 1f)
                SaveMap.TrySet(e, desired, 10f);
        }
    }

    public static string ZeroNeeds()
    {
        var (n, w) = Apply(NeedsZero());
        return $"Employee needs refilled: {n} employee(s), {w} prop(s) set to 0.";
    }

    // EmployeeOnlyPatch "skills only": raise the satisfaction ceiling to max.
    public static string MaxStats()
    {
        var (n, w) = Apply(("satisfactionLimit", 1f));
        RefreshSatisfaction();
        return $"Employee stats maxed: {n} employee(s), {w} write(s).";
    }

    // EmployeeOnlyPatch "burst"/"freeze": everything at once.
    public static string ApplyAll()
    {
        var values = new List<(string, object)>
        {
            ("satisfactionLimit", 1f), ("satisfaction", 1f), ("reliability", 1f), ("xp", 999999),
            ("salary", 0f)
        };
        values.AddRange(NeedsZero());
        var (n, w) = Apply(values.ToArray());
        EnsureDesiredSalary();
        RefreshSatisfaction();
        Apply(("satisfaction", 1f));
        return $"Employee free+max+no fatigue: {n} employee(s), {w} write(s).";
    }
}
