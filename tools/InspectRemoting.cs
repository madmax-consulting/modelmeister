using System;
using System.IO;
using System.Linq;
using System.Reflection;

var asm = typeof(inRiver.Remoting.RemoteManager).Assembly;

using var fs = new StreamWriter(@"C:\Projects\Private\mm-redux\new\tools\remoting-types.txt");

foreach (var type in asm.GetTypes().Where(t => t.IsPublic).OrderBy(t => t.FullName))
{
    fs.WriteLine($"=== {type.FullName} ({(type.IsInterface ? "interface" : type.IsEnum ? "enum" : "class")}) ===");
    if (type.IsEnum)
    {
        foreach (var v in Enum.GetNames(type)) fs.WriteLine($"  {v}");
        continue;
    }
    foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.Name))
        fs.WriteLine($"  PROP {p.PropertyType.Name} {p.Name}");
    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(m => m.Name))
    {
        if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")) continue;
        try
        {
            fs.WriteLine($"  METH {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
        }
        catch { }
    }
}
fs.Flush();
Console.WriteLine("done");
