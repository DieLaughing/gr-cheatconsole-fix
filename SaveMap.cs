using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace GRCheatConsoleFix;

// Learns which obfuscated member holds which value by reading the IL of the game's own
// "build save data" method. That method copies members into a save-data class whose field
// names are NOT obfuscated (salary, hunger, tiredness, ...), e.g.
//
//     return new EmployeeSaveData { salary = GBGKEANDJJL, hunger = DCCJFABFGHB, ... };
//
// compiles to "dup; ldarg.0; call get_GBGKEANDJJL; stfld EmployeeSaveData::salary", so pairing
// each stfld into the save class with the last member read from the owner gives the mapping.
internal static class SaveMap
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null))
        .ToDictionary(o => o.Value);

    // Returns saveFieldName -> member on ownerType (PropertyInfo or FieldInfo).
    public static Dictionary<string, MemberInfo> Build(Type ownerType, Type saveType)
    {
        var best = new Dictionary<string, MemberInfo>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly;

        // The obfuscator can add decoy methods, so try every candidate and keep the richest map.
        foreach (var method in ownerType.GetMethods(flags))
        {
            if (method.ReturnType != saveType || method.GetParameters().Length != 0 || method.IsAbstract)
                continue;
            try
            {
                var map = Scan(method, ownerType, saveType);
                if (map.Count > best.Count)
                    best = map;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"SaveMap: skipped {ownerType.Name}.{method.Name}: {ex.Message}");
            }
        }
        return best;
    }

    private static Dictionary<string, MemberInfo> Scan(MethodInfo method, Type ownerType, Type saveType)
    {
        var map = new Dictionary<string, MemberInfo>();
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null)
            return map;

        var module = method.Module;
        MemberInfo last = null;
        int pos = 0;
        while (pos < il.Length)
        {
            OpCode op;
            if (il[pos] == 0xFE)
            {
                op = OpCodesByValue[(short)(0xFE00 | il[pos + 1])];
                pos += 2;
            }
            else
            {
                op = OpCodesByValue[il[pos]];
                pos += 1;
            }

            int token = 0;
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    pos += 1;
                    break;
                case OperandType.InlineVar:
                    pos += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    pos += 8;
                    break;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, pos);
                    pos += 4 + 4 * count;
                    break;
                default:
                    token = BitConverter.ToInt32(il, pos);
                    pos += 4;
                    break;
            }

            if (op == OpCodes.Dup)
            {
                last = null;
            }
            else if (op == OpCodes.Call || op == OpCodes.Callvirt)
            {
                // Getter names are obfuscated too (not always get_X), so match the accessor itself.
                if (TryResolveMethod(module, token) is MethodInfo m && m.GetParameters().Length == 0 &&
                    m.DeclaringType != null && m.DeclaringType.IsAssignableFrom(ownerType))
                {
                    last = PropertyForGetter(m);
                }
            }
            else if (op == OpCodes.Ldfld)
            {
                if (TryResolveField(module, token) is FieldInfo f && f.DeclaringType != null &&
                    f.DeclaringType.IsAssignableFrom(ownerType))
                    last = f;
            }
            else if (op == OpCodes.Stfld)
            {
                if (TryResolveField(module, token) is FieldInfo f && f.DeclaringType == saveType && last != null)
                {
                    if (!map.ContainsKey(f.Name))
                        map[f.Name] = last;
                    last = null;
                }
            }
        }
        return map;
    }

    private static PropertyInfo PropertyForGetter(MethodInfo getter)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly;
        foreach (var p in getter.DeclaringType.GetProperties(flags))
        {
            if (p.GetGetMethod(true) == getter)
                return p;
        }
        return null;
    }

    private static MethodBase TryResolveMethod(Module module, int token)
    {
        try { return module.ResolveMethod(token); }
        catch { return null; }
    }

    private static FieldInfo TryResolveField(Module module, int token)
    {
        try { return module.ResolveField(token); }
        catch { return null; }
    }

    public static bool TrySet(object target, MemberInfo member, object value)
    {
        switch (member)
        {
            case PropertyInfo p when p.GetSetMethod(true) != null:
                p.SetValue(target, Convert.ChangeType(value, p.PropertyType), null);
                return true;
            case FieldInfo f when !f.IsInitOnly && !f.IsLiteral:
                f.SetValue(target, Convert.ChangeType(value, f.FieldType));
                return true;
            default:
                return false;
        }
    }

    public static object TryGet(object target, MemberInfo member)
    {
        try
        {
            return member switch
            {
                PropertyInfo p => p.GetValue(target, null),
                FieldInfo f => f.GetValue(target),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}
