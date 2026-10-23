using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
static class BrandTransform
{
    static readonly Dictionary<string,string> guids = new(StringComparer.Ordinal) {
        ["wanglocal.coopbotscustom"]="PreLuna.CoopBotsCustom", ["wanglocal.prisonermovementcustom"]="PreLuna.PrisonerMovementCustom",
        ["wanglocal.prisonerhealthcustom"]="PreLuna.PrisonerHealthCustom", ["wanglocal.flashlightcustom"]="PreLuna.FlashlightCustom",
        ["wanglocal.bioscannercustom"]="PreLuna.BioScannerCustom", ["wanglocal.foamguncustom"]="PreLuna.FoamGunCustom",
        ["wanglocal.captain.boosters"]="PreLuna.Captain.Boosters", ["wanglocal.captain.commands"]="PreLuna.Captain.Commands",
        ["wanglocal.captain.fixes"]="PreLuna.Captain.Fixes", ["wanglocal.captain"]="PreLuna.Captain",
        ["wanglocal.headcamera"]="PreLuna.HeadCamera", ["wanglocal.micromaster"]="PreLuna.MicroMaster", ["WangLocal.MaxConsumables"]="PreLuna.MaxConsumables"
    };
    static void Need(bool ok,string why) { if(!ok)throw new InvalidOperationException(why); }
    static string Map(string value)
    {
        if(value==null)return null;
        foreach(var pair in guids.OrderByDescending(p=>p.Key.Length)) value=value.Replace(pair.Key,pair.Value,StringComparison.Ordinal);
        return value.Replace("WangLocal.","PreLuna.",StringComparison.Ordinal).Replace("WangLocal ","PreLuna ",StringComparison.Ordinal);
    }
    static void Type(TypeReference t)
    {
        if(t==null)return;
        if(t is GenericInstanceType g){Type(g.ElementType);foreach(var a in g.GenericArguments)Type(a);return;}
        if(t is TypeSpecification s){Type(s.ElementType);return;}
        if(t is GenericParameter)return;
        t.Namespace=Map(t.Namespace); if(t.DeclaringType!=null)Type(t.DeclaringType);
    }
    static CustomAttributeArgument Argument(CustomAttributeArgument a)
    {
        if(a.Value is string text)return new(a.Type,Map(text));
        if(a.Value is TypeReference type){Type(type);return new(a.Type,type);}
        if(a.Value is CustomAttributeArgument[] array)return new(a.Type,array.Select(Argument).ToArray());
        return a;
    }
    static void Attributes(Mono.Cecil.ICustomAttributeProvider p)
    {
        foreach(var a in p.CustomAttributes){
            if(a.AttributeType.Name=="BepInIncompatibility")continue; // Keep historical identities; do not make self-incompatibilities.
            for(int i=0;i<a.ConstructorArguments.Count;i++)a.ConstructorArguments[i]=Argument(a.ConstructorArguments[i]);
            for(int i=0;i<a.Fields.Count;i++)a.Fields[i]=new(a.Fields[i].Name,Argument(a.Fields[i].Argument));
            for(int i=0;i<a.Properties.Count;i++)a.Properties[i]=new(a.Properties[i].Name,Argument(a.Properties[i].Argument));
        }
    }
    static string Body(MethodDefinition method,int skip,bool mapped)
    {
        if(!method.HasBody)return "NO_BODY";
        var code=method.Body.Instructions;
        string O(object o)=>o switch {
            null=>"", Instruction i=>"@"+(code.IndexOf(i)-skip),Instruction[] a=>string.Join(",",a.Select(i=>code.IndexOf(i)-skip)),
            VariableDefinition v=>"local:"+v.Index,ParameterDefinition p=>"arg:"+p.Index,
            _=>mapped?Map(o.ToString()):o.ToString()
        };
        return string.Join("\n",code.Skip(skip).Select(i=>i.OpCode.Code+" "+O(i.Operand)))
            +"\nLOCALS:"+string.Join(",",method.Body.Variables.Select(v=>mapped?Map(v.VariableType.FullName):v.VariableType.FullName))
            +"\nEH:"+string.Join(";",method.Body.ExceptionHandlers.Select(e=>e.HandlerType+":"+O(e.CatchType)+":"+O(e.TryStart)+":"+O(e.TryEnd)+":"+O(e.HandlerStart)+":"+O(e.HandlerEnd)+":"+O(e.FilterStart)));
    }
    static Dictionary<string,MethodDefinition> MergeSupport(ModuleDefinition target,ModuleDefinition source,string ns)
    {
        var old=source.GetType("PreLuna.BuildSupport.ModuleMigration");
        Need(old!=null&&!old.HasNestedTypes&&!old.HasFields,"Migration helper must remain one standalone static type");
        var type=new TypeDefinition(ns+".Internal","ModuleMigration",old.Attributes,target.ImportReference(old.BaseType));target.Types.Add(type);
        var methods=new Dictionary<string,MethodDefinition>();
        foreach(var m in old.Methods){var n=new MethodDefinition(m.Name,m.Attributes,target.ImportReference(m.ReturnType)){ImplAttributes=m.ImplAttributes};
            foreach(var p in m.Parameters)n.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,target.ImportReference(p.ParameterType)));
            type.Methods.Add(n);methods.Add(m.FullName,n);
        }
        foreach(var m in old.Methods){
            var n=methods[m.FullName];if(!m.HasBody)continue;
            n.Body=new MethodBody(n){InitLocals=m.Body.InitLocals,MaxStackSize=m.Body.MaxStackSize};
            var vars=new Dictionary<VariableDefinition,VariableDefinition>();foreach(var v in m.Body.Variables){var copy=new VariableDefinition(target.ImportReference(v.VariableType));n.Body.Variables.Add(copy);vars.Add(v,copy);}
            var code=new Dictionary<Instruction,Instruction>();foreach(var i in m.Body.Instructions){var copy=Instruction.Create(OpCodes.Nop);copy.OpCode=i.OpCode;n.Body.Instructions.Add(copy);code.Add(i,copy);}
            foreach(var i in m.Body.Instructions)code[i].Operand=i.Operand switch {
                Instruction j=>code[j],Instruction[] jj=>jj.Select(j=>code[j]).ToArray(),VariableDefinition v=>vars[v],ParameterDefinition p=>n.Parameters[p.Index],
                MethodReference call when call.DeclaringType.FullName==old.FullName=>methods[call.FullName],
                MethodReference call=>target.ImportReference(call),FieldReference f=>target.ImportReference(f),TypeReference t=>target.ImportReference(t),_=>i.Operand
            };
            Instruction At(Instruction i)=>i==null?null:code[i];
            foreach(var e in m.Body.ExceptionHandlers)n.Body.ExceptionHandlers.Add(new ExceptionHandler(e.HandlerType){TryStart=At(e.TryStart),TryEnd=At(e.TryEnd),HandlerStart=At(e.HandlerStart),HandlerEnd=At(e.HandlerEnd),FilterStart=At(e.FilterStart),CatchType=e.CatchType==null?null:target.ImportReference(e.CatchType)});
        }
        return methods.Values.ToDictionary(m=>m.Name);
    }
    static void Prefix(MethodDefinition m,params Instruction[] instructions)
    {var il=m.Body.GetILProcessor();var start=m.Body.Instructions[0];foreach(var i in instructions)il.InsertBefore(start,i);m.Body.MaxStackSize=Math.Max(m.Body.MaxStackSize,2);}
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void Main(string[] args)
    {
        // input, output, merged-helper, framework-BepInEx, runtime-root, legacy-guid, new-guid, report
        string input=args[0],output=args[1],oldGuid=args[5],newGuid=args[6];
        using var resolver=new DefaultAssemblyResolver();foreach(var dir in new[]{Path.Combine(args[3],"core"),Path.Combine(args[3],"interop"),args[4],Path.GetDirectoryName(input)})resolver.AddSearchDirectory(dir);
        var reader=new ReaderParameters{AssemblyResolver=resolver};
        using var module=ModuleDefinition.ReadModule(input,reader);using var helper=ModuleDefinition.ReadModule(args[2],reader);
        var plugin=module.GetTypes().Single(t=>t.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInPlugin"));
        var info=plugin.CustomAttributes.Single(a=>a.AttributeType.Name=="BepInPlugin");
        Need((string)info.ConstructorArguments[0].Value==oldGuid||(string)info.ConstructorArguments[0].Value==newGuid,"Wrong baseline identity");
        string version=(string)info.ConstructorArguments[2].Value;
        var original=module.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>Map(m.FullName),m=>Body(m,0,true));
        var attributes=module.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>Map(m.FullName),m=>new{m.Attributes,m.ImplAttributes,Parameters=string.Join(";",m.Parameters.Select(p=>p.Name+":"+p.Attributes))});
        foreach(var reference in module.AssemblyReferences)reference.Name=Map(reference.Name);
        foreach(var type in module.GetTypeReferences())Type(type);
        foreach(var type in module.GetTypes()){
            type.Namespace=Map(type.Namespace);Type(type.BaseType);Attributes(type);
            foreach(var iface in type.Interfaces)Type(iface.InterfaceType);
            foreach(var field in type.Fields){Type(field.FieldType);if(field.HasConstant&&field.Constant is string text)field.Constant=Map(text);Attributes(field);}
            foreach(var property in type.Properties)Attributes(property);
            foreach(var method in type.Methods){Type(method.ReturnType);Attributes(method);Attributes(method.MethodReturnType);foreach(var p in method.Parameters){Type(p.ParameterType);Attributes(p);}
                if(!method.HasBody)continue;foreach(var v in method.Body.Variables)Type(v.VariableType);
                foreach(var e in method.Body.ExceptionHandlers)Type(e.CatchType);
                foreach(var i in method.Body.Instructions){if(i.Operand is string text)i.Operand=Map(text);if(i.Operand is TypeReference t)Type(t);if(i.Operand is MemberReference member)Type(member.DeclaringType);}
            }
        }
        Attributes(module);Attributes(module.Assembly);
        foreach(var resource in module.Resources)resource.Name=Map(resource.Name);
        module.Assembly.Name.Name=newGuid;module.Name=newGuid+".dll";module.Mvid=Guid.NewGuid();
        info.ConstructorArguments[0]=new(module.TypeSystem.String,newGuid);
        var core=resolver.Resolve(new AssemblyNameReference("BepInEx.Core",new Version(6,0,0,0)));
        var ctor=core.MainModule.GetType("BepInEx.BepInIncompatibility").Methods.Single(m=>m.IsConstructor&&m.Parameters.Count==1);
        if(!plugin.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInIncompatibility"&&(string)a.ConstructorArguments[0].Value==oldGuid)){
            var attr=new CustomAttribute(module.ImportReference(ctor));attr.ConstructorArguments.Add(new(module.TypeSystem.String,oldGuid));plugin.CustomAttributes.Add(attr);
        }
        var bootstrap=MergeSupport(module,helper,newGuid);
        var constructor=plugin.Methods.Single(m=>m.IsConstructor&&!m.IsStatic&&m.Parameters.Count==0);
        var load=plugin.Methods.Single(m=>m.Name=="Load"&&m.Parameters.Count==0);
        Prefix(constructor,Instruction.Create(OpCodes.Ldstr,oldGuid),Instruction.Create(OpCodes.Ldstr,newGuid),Instruction.Create(OpCodes.Call,bootstrap["Prepare"]));
        Prefix(load,Instruction.Create(OpCodes.Ldstr,oldGuid),Instruction.Create(OpCodes.Call,bootstrap["Guard"]));
        module.Write(output);
        using var read=ModuleDefinition.ReadModule(output,reader);
        var actual=read.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
        int preserved=0;
        foreach(var pair in original){
            Need(actual.TryGetValue(pair.Key,out var now),"Missing method: "+pair.Key);
            int prefix=now.FullName==constructor.FullName?3:now.FullName==load.FullName?2:0;
            Need(pair.Value==Body(now,prefix,false),"Unexpected gameplay IL/local/EH change: "+pair.Key);
            var flags=attributes[pair.Key];Need(flags.Attributes==now.Attributes&&flags.ImplAttributes==now.ImplAttributes&&flags.Parameters==string.Join(";",now.Parameters.Select(p=>p.Name+":"+p.Attributes)),"Hook signature/argument change: "+pair.Key);preserved++;
        }
        var readPlugin=read.GetTypes().Single(t=>t.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInPlugin"));
        Need((string)readPlugin.CustomAttributes.Single(a=>a.AttributeType.Name=="BepInPlugin").ConstructorArguments[0].Value==newGuid,"GUID not serialized");
        Need(!read.AssemblyReferences.Any(a=>a.Name.StartsWith("WangLocal.",StringComparison.Ordinal)||a.Name=="Migration.helper"),"Old/external migration dependency remains");
        File.WriteAllText(args[7],JsonSerializer.Serialize(new{OldGuid=oldGuid,NewGuid=newGuid,Version=version,InputSHA256=Hash(input),OutputSHA256=Hash(output),PreservedInputMethods=preserved,HookSignaturesPreserved=true,MigrationBeforeBaseConstructor=true,OldPluginMutualExclusion=true,IdentityOverlayChangesGameplay=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS: "+newGuid+" identity/config migration; "+preserved+" methods preserved.");
    }
}
