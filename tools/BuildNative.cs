using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
class BuildNative {
 static string bin;
 static int Main(string[] args){if(args.Length!=4){Console.Error.WriteLine("customDir outputDll ninjaInstallBin sourceOverrideDir required");return 2;}bin=args[2];AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string p=Path.Combine(bin,new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};try{return Run(args);}catch(Exception e){Console.WriteLine(e.ToString());return 3;}}
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static int Run(string[] args){
 string baseDir=args[0],output=args[1],fw=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Microsoft.NET","Framework64","v4.0.30319");
 var xml=XDocument.Load(Path.Combine(baseDir,"NinjaTrader.Custom.csproj"));
 var paths=xml.Descendants("Compile").Select(x=>Path.Combine(baseDir,Uri.UnescapeDataString((string)x.Attribute("Include")))).ToList();
 foreach(string name in new[]{"MNQProtectionPanel.cs","ProtectionMath.cs"}){var p=Path.Combine(baseDir,"Indicators",name);if(!paths.Contains(p))paths.Add(p);}
 string overrideDir=args[3];
 var replacements=new[]{"MNQProtectionPanel.cs","ProtectionMath.cs","Sim101ApiBridge.cs"}.ToDictionary(n=>n,n=>Path.Combine(overrideDir,n));
 paths=paths.Select(p=>replacements.ContainsKey(Path.GetFileName(p))?replacements[Path.GetFileName(p)]:p).ToList();
 foreach(var replacement in replacements.Values)if(!paths.Contains(replacement))paths.Add(replacement);
 var trees=paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),new CSharpParseOptions(LanguageVersion.Preview),p,Encoding.UTF8)).ToList();
 var refs=new List<MetadataReference>();
 foreach(var x in xml.Descendants("Reference")) {string hint=(string)x.Element("HintPath"),name=(string)x.Attribute("Include");string p=hint??Path.Combine(fw,name+".dll");if(!File.Exists(p))p=Path.Combine(fw,"WPF",name+".dll");refs.Add(MetadataReference.CreateFromFile(p));}
 foreach(string n in new[]{"UIAutomationClient.dll","UIAutomationTypes.dll"})refs.Add(MetadataReference.CreateFromFile(Path.Combine(fw,"WPF",n)));
 // Retain the installed main assembly's resources verbatim; satellite assemblies remain untouched.
 var resources=new List<ResourceDescription>();var old=Assembly.LoadFrom(Path.Combine(baseDir,"NinjaTrader.Custom.dll"));
 foreach(string name in old.GetManifestResourceNames()){byte[] bytes;using(var stream=old.GetManifestResourceStream(name)){using(var ms=new MemoryStream()){stream.CopyTo(ms);bytes=ms.ToArray();}}byte[] captured=bytes;resources.Add(new ResourceDescription(name,()=>new MemoryStream(captured,false),true));}
 var compilation=CSharpCompilation.Create("NinjaTrader.Custom",trees,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64));
 using(var stream=File.Create(output)){var result=compilation.Emit(stream,manifestResources:resources);foreach(var d in result.Diagnostics.Where(x=>x.Severity==DiagnosticSeverity.Error))Console.WriteLine(d.ToString());Console.WriteLine("Sources="+trees.Count+" Resources="+resources.Count+" Success="+result.Success);return result.Success?0:1;}
 }
}
