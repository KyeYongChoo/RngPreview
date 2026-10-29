using HarmonyLib;
using System.Reflection;
using System.Runtime.CompilerServices;
var asm = Assembly.LoadFrom(args[0]);
var h = new Harmony("check");
h.PatchAll(asm);
foreach (var m in h.GetPatchedMethods())
{
    var info = Harmony.GetPatchInfo(m);
    Console.WriteLine($"{m.DeclaringType!.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})  prefixes={info.Prefixes.Count} postfixes={info.Postfixes.Count}");
}
foreach (var t in asm.GetTypes()) RuntimeHelpers.RunClassConstructor(t.TypeHandle);
Console.WriteLine("static initializers OK");
