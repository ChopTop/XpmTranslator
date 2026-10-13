using Mono.Cecil;
using Mono.Cecil.Cil;

namespace XpmTranslator.Patches;

/// <summary>
/// .NET assembly patches with Mono.Cecil.
/// The map replaces exact string literals (ldstr / string constants). Optional keys:
///   "__sanitize__":      "Ns.Type::Method:paramIndex;..." string parameters filtered at method entry
///   "__sanitize_text__": replacement used when such a parameter is unreadable (only '?', punctuation, digits)
/// The sanitizer exists for the OTA change log: Xiaomi's native wrapper converts it through the ANSI code
/// page, so on non-Chinese Windows every Chinese character arrives as '?'.
/// </summary>
public static class AssemblyPatch
{
    public static byte[] Patch(byte[] input, Dictionary<string, string> map, string searchDir, out int replaced, out List<string> unmatched)
    {
        map = new Dictionary<string, string>(map);
        var sanitize = map.Remove("__sanitize__", out var sl) ? sl.Split(';', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        map.Remove("__sanitize_text__", out var sanitizeText);

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(searchDir);
        using var inStream = new MemoryStream(input);
        using var asm = AssemblyDefinition.ReadAssembly(inStream, new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
        var module = asm.MainModule;
        var hits = new HashSet<string>();
        replaced = 0;
        foreach (var type in module.GetTypes())
        {
            foreach (var f in type.Fields)
                if (f.HasConstant && f.Constant is string cs && map.TryGetValue(cs, out var cr))
                {
                    f.Constant = cr; hits.Add(cs); replaced++;
                }
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var ins in m.Body.Instructions)
                    if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string s && map.TryGetValue(s, out var r))
                    {
                        ins.Operand = r; hits.Add(s); replaced++;
                    }
            }
        }

        foreach (var spec in sanitize)
        {
            int colon = spec.LastIndexOf(':'), sep = spec.IndexOf("::", StringComparison.Ordinal);
            var typeName = spec[..sep];
            var methodName = spec[(sep + 2)..colon];
            int index = int.Parse(spec[(colon + 1)..]);
            var type = module.GetType(typeName) ?? throw new InvalidOperationException("type not found: " + typeName);
            var method = type.Methods.SingleOrDefault(x => x.Name == methodName) ?? throw new InvalidOperationException("method not found: " + spec);
            var param = method.Parameters[index];
            if (param.ParameterType.FullName != "System.String") throw new InvalidOperationException(spec + ": not a string parameter");
            var filter = type.Methods.SingleOrDefault(x => x.Name == "__SanitizeUnreadable") ?? AddFilter(module, type, sanitizeText ?? "");
            var il = method.Body.GetILProcessor();
            var first = method.Body.Instructions[0];
            if (first.OpCode == OpCodes.Ldarg && first.Next?.Operand == filter) continue; // already patched
            il.InsertBefore(first, il.Create(OpCodes.Ldarg, param));
            il.InsertBefore(first, il.Create(OpCodes.Call, filter));
            il.InsertBefore(first, il.Create(OpCodes.Starg, param));
            replaced++;
        }

        unmatched = map.Keys.Where(k => !hits.Contains(k)).ToList();
        using var outStream = new MemoryStream();
        asm.Write(outStream);
        return outStream.ToArray();
    }

    // static string __SanitizeUnreadable(string s):
    //   null/empty -> s; any char outside "?,.;:!-() \t\r\n0-9，。" -> s; otherwise -> replacement
    static MethodDefinition AddFilter(ModuleDefinition module, TypeDefinition type, string replacement)
    {
        var ts = module.TypeSystem;   // System.Runtime of the target, not of this tool
        var m = new MethodDefinition("__SanitizeUnreadable", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, ts.String);
        m.Parameters.Add(new ParameterDefinition("s", ParameterAttributes.None, ts.String));
        var getLength = new MethodReference("get_Length", ts.Int32, ts.String) { HasThis = true };
        var getChars = new MethodReference("get_Chars", ts.Char, ts.String) { HasThis = true };
        getChars.Parameters.Add(new ParameterDefinition(ts.Int32));
        var indexOf = new MethodReference("IndexOf", ts.Int32, ts.String) { HasThis = true };
        indexOf.Parameters.Add(new ParameterDefinition(ts.Char));
        var i = new VariableDefinition(ts.Int32);
        m.Body.Variables.Add(i);
        m.Body.InitLocals = true;
        var il = m.Body.GetILProcessor();
        var retS = il.Create(OpCodes.Ldarg_0);
        var loopCheck = il.Create(OpCodes.Ldloc, i);
        var loopBody = il.Create(OpCodes.Ldstr, "?,.;:!-() \t\r\n0123456789，。");
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Brfalse, retS));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, getLength));
        il.Append(il.Create(OpCodes.Brfalse, retS));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stloc, i));
        il.Append(il.Create(OpCodes.Br, loopCheck));
        il.Append(loopBody);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldloc, i));
        il.Append(il.Create(OpCodes.Callvirt, getChars));
        il.Append(il.Create(OpCodes.Callvirt, indexOf));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Blt, retS));
        il.Append(il.Create(OpCodes.Ldloc, i));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Stloc, i));
        il.Append(loopCheck);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, getLength));
        il.Append(il.Create(OpCodes.Blt, loopBody));
        il.Append(il.Create(OpCodes.Ldstr, replacement));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(retS);
        il.Append(il.Create(OpCodes.Ret));
        type.Methods.Add(m);
        return m;
    }
}
