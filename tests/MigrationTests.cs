using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using PreLuna.BuildSupport;
namespace BepInEx { public static class Paths { public static string ConfigPath=""; } }
namespace BepInEx.Logging { public static class Logger { public static Log CreateLogSource(string s)=>new(); } public class Log { public void LogError(object o){} } }
namespace BepInEx.Unity.IL2CPP { public class IL2CPPChainloader { public static IL2CPPChainloader Instance=new(); public Dictionary<string,object> Plugins=new(); } }
static class MigrationTests
{
 static int checks;
 static void Check(bool ok,string s){checks++;if(!ok)throw new Exception(s);}
 static void Bad(Action run,string s){bool failed=false;try{run();}catch{failed=true;}Check(failed,s);}
 static void Main(){
  string root=Path.Combine(Environment.GetEnvironmentVariable("PRELUNA_TEST_ROOT")??Path.GetTempPath(),"PreLunaMigration-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
  string[] ids={"CoopBotsCustom","PrisonerMovementCustom","PrisonerHealthCustom","FlashlightCustom","BioScannerCustom","FoamGunCustom","Captain","HeadCamera","MicroMaster"};
  foreach(string id in ids){
   string dir=Path.Combine(root,id);Directory.CreateDirectory(dir);
   string old="wanglocal."+id.ToLowerInvariant(),next="PreLuna."+id;
   string content="# 中文自定义\r\n[General]\r\nEnabled = false\r\nHotkey = F6\r\nRadius = 20\r\nUnknown = keep=me\r\n";
   File.WriteAllText(Path.Combine(dir,old+".cfg"),content);var bytes=File.ReadAllBytes(Path.Combine(dir,old+".cfg"));
   ModuleMigration.PrepareAt(dir,old,next);
   Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,next+".cfg"))),id+" exact copy");
   Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,old+".cfg"))),id+" original retained");
   ModuleMigration.PrepareAt(dir,old,next);Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,next+".cfg"))),id+" repeat");
   File.WriteAllText(Path.Combine(dir,next+".cfg"),"[New]\nKey = new");ModuleMigration.PrepareAt(dir,old,next);
   Check(File.ReadAllText(Path.Combine(dir,next+".cfg")).Contains("new"),id+" new wins");
  }
  string empty=Path.Combine(root,"empty");Directory.CreateDirectory(empty);
  foreach(string id in ids)ModuleMigration.PrepareAt(empty,"wanglocal."+id.ToLowerInvariant(),"PreLuna."+id);
  foreach(string id in new[]{"captain.commands","captain.boosters","captain.fixes","MaxConsumables"})ModuleMigration.PrepareAt(empty,id=="MaxConsumables"?"WangLocal.MaxConsumables":"wanglocal."+id,"PreLuna."+(id=="MaxConsumables"?id:"Captain."+char.ToUpperInvariant(id[8])+id.Substring(9)));
  Check(Directory.GetFiles(empty).Length==1&&File.ReadAllText(Path.Combine(empty,"PreLuna.FoamGunCustom.cfg")).Contains("ShotInterval = 0.4"),"fresh foam independent baseline default; no empty secondary CFG");
  string partial=Path.Combine(root,"partial-foam");Directory.CreateDirectory(partial);File.WriteAllText(Path.Combine(partial,"wanglocal.foamguncustom.cfg"),"[General]\nEnabled = false\n");ModuleMigration.PrepareAt(partial,"wanglocal.foamguncustom","PreLuna.FoamGunCustom");Check(!File.ReadAllText(Path.Combine(partial,"PreLuna.FoamGunCustom.cfg")).Contains("ShotInterval"),"existing partial foam config unchanged / Bind fallback preserved");
  var fallbacks=new Dictionary<string,string>{{"coopbotscustom","BetterBots.cfg"},{"prisonermovementcustom","Localia.APEX_Prisoners.cfg"},{"flashlightcustom","FlashlightOverhaul.cfg"},{"bioscannercustom","INF.PersistentBioScanner.cfg"},{"foamguncustom","local.betterfoamgun.cfg"}};
  foreach(var pair in fallbacks){string dir=Path.Combine(root,"fallback-"+pair.Key);Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,pair.Value),"[Legacy]\nValue = 12");string next="PreLuna."+ids.Single(i=>i.ToLowerInvariant()==pair.Key);ModuleMigration.PrepareAt(dir,"wanglocal."+pair.Key,next);Check(File.ReadAllText(Path.Combine(dir,next+".cfg")).Contains("12"),"fallback "+pair.Key);}
  File.WriteAllText(Path.Combine(empty,"MindAttic.GTFO.EZ.cfg"),"[Old]\nHP = 9999");ModuleMigration.PrepareAt(empty,"wanglocal.prisonerhealthcustom","PreLuna.PrisonerHealthCustom");Check(!File.Exists(Path.Combine(empty,"PreLuna.PrisonerHealthCustom.cfg")),"retired health config ignored");
  string corrupt=Path.Combine(root,"corrupt");Directory.CreateDirectory(corrupt);string source=Path.Combine(corrupt,"wanglocal.captain.cfg"),dest=Path.Combine(corrupt,"PreLuna.Captain.cfg");
  File.WriteAllText(source,"not a config");Bad(()=>ModuleMigration.PrepareAt(corrupt,"wanglocal.captain","PreLuna.Captain"),"bad CFG rejected");Check(!File.Exists(dest)&&File.ReadAllText(source)=="not a config","bad source preserved");
  File.WriteAllText(source,"[Good]\nKey = 1");File.WriteAllText(dest,"invalid");Bad(()=>ModuleMigration.PrepareAt(corrupt,"wanglocal.captain","PreLuna.Captain"),"bad new CFG rejected");Check(File.ReadAllText(dest)=="invalid","bad new CFG not overwritten");
  string jsonDir=Path.Combine(root,"json");Directory.CreateDirectory(jsonDir);string jsonOld=Path.Combine(jsonDir,"wanglocal.captain.boosters.json"),jsonNew=Path.Combine(jsonDir,"PreLuna.Captain.Boosters.json");
  string fingerprint=new string('A',64);string row="{\"Slot\":2,\"Category\":1,\"SourceId\":4294967295,\"Fingerprint\":\""+fingerprint+"\",\"FutureField\":123}";
  string json="{\"Version\":1,\"Selections\":["+row+"],\"Unknown\":\"保留\"}";File.WriteAllText(jsonOld,json);ModuleMigration.PrepareAt(jsonDir,"wanglocal.captain.boosters","PreLuna.Captain.Boosters");
  Check(File.ReadAllText(jsonNew)==json&&File.ReadAllText(jsonOld)==json,"JSON exact copy / unknown fields retained");
  var saved=JsonSerializer.Deserialize<PreLuna.Captain.Boosters.SelectionFile>(File.ReadAllText(jsonNew));
  var slots=new PreLuna.Captain.Boosters.BoosterSelection(); slots.Import(saved);Check(slots.Export().Selections.Count==1,"real booster selection import");Check(slots.Export().Selections.Single().SourceId==uint.MaxValue,"real booster source ID retained");
  foreach(string bad in new[]{"{","null","[]","{\"Version\":2}","{\"Selections\":null}","{\"Selections\":["+row+","+row+"]}","{\"Selections\":[{\"Slot\":4,\"Category\":0,\"Fingerprint\":\""+fingerprint+"\"}]}","{\"Selections\":[{\"Fingerprint\":\"bad\"}]}"}){
   File.Delete(jsonNew);File.WriteAllText(jsonOld,bad);Bad(()=>ModuleMigration.PrepareAt(jsonDir,"wanglocal.captain.boosters","PreLuna.Captain.Boosters"),"bad JSON rejected");Check(!File.Exists(jsonNew)&&File.ReadAllText(jsonOld)==bad,"bad JSON untouched");
  }
  File.WriteAllText(jsonOld,json);File.WriteAllText(jsonNew,"{}");ModuleMigration.PrepareAt(jsonDir,"wanglocal.captain.boosters","PreLuna.Captain.Boosters");Check(File.ReadAllText(jsonNew)=="{}","new empty selection wins");
  Bad(()=>ModuleMigration.PrepareAt(empty,"wanglocal.captain","../escape"),"unsafe target rejected");
  ModuleMigration.Guard("wanglocal.captain");Check(true,"no conflict allowed");BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.Add("wanglocal.captain",new());Bad(()=>ModuleMigration.Guard("wanglocal.captain"),"legacy loaded blocks PreLuna");
  Check(!Directory.GetFiles(root,"*.tmp",SearchOption.AllDirectories).Any(),"no temporary file remnants");
  Console.WriteLine("PASS: "+checks+" config/save migration assertions; original data retained. Fixtures: "+root);
 }
}
