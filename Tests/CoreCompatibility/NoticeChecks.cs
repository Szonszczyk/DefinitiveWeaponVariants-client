using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using DefinitiveWeaponVariants.Client.Compatibility;
using EFT.Communications;

static class NoticeChecks
{
    static void Check(bool condition) { if(!condition) throw new Exception("Notice assertion failed"); }
    static (ConfigFile own, ConfigFile other, ManualLogSource log, UiFixesCompatibilityNotice notice) Setup(string setting="Never")
    {
        Chainloader.PluginInfos.Clear(); NotificationManager.Messages.Clear();
        Singleton<NotificationManager>.Instantiated=true;
        var own=new ConfigFile(); var other=new ConfigFile(); var log=new ManualLogSource();
        other.Bind("C. Gameplay", "Modify Weapons In Raid", setting, "");
        Chainloader.PluginInfos.Add("com.tyfon.uifixes",new(){Instance=new(){Config=other}});
        return (own,other,log,new UiFixesCompatibilityNotice(own,log));
    }
    static void Step(System.Collections.IEnumerator run) { for(int i=0;i<3;i++)run.MoveNext(); }
    public static void Run()
    {
        foreach(var setting in new[]{"Always","WithTool"})
        {
            var t=Setup(setting); using var notice=t.notice; Step(notice.Run());
            Check(t.log.Messages.Count==0 && NotificationManager.Messages.Count==0);
            Check(t.other["C. Gameplay","Modify Weapons In Raid"].GetSerializedValue()==setting);
        }
        Console.WriteLine("PASS: Always and With Multitool accepted without changing UI-Fixes settings.");
        {
            var t=Setup(); var run=t.notice.Run(); Step(run); Step(run);
            Check(t.log.Messages.Count==1 && NotificationManager.Messages.Count==1);
            Check(t.other.SubscriberCount==0 && t.own.Enabled.SubscriberCount==0);
        }
        Console.WriteLine("PASS: Never warns once and unsubscribes.");
        {
            var t=Setup(); using var notice=t.notice; t.own.Enabled.Value=false; Step(notice.Run());
            Check(t.log.Messages.Count==0 && NotificationManager.Messages.Count==0);
        }
        Console.WriteLine("PASS: DWV toggle silences both log and popup.");
        {
            var t=Setup(); using var notice=t.notice; Singleton<NotificationManager>.Instantiated=false;
            var run=notice.Run(); Step(run); Check(t.log.Messages.Count==0);
            t.other.Change("Always"); Singleton<NotificationManager>.Instantiated=true; Step(run);
            Check(t.log.Messages.Count==0 && NotificationManager.Messages.Count==0);
            t.other.Change("Never"); Step(run); Check(NotificationManager.Messages.Count==1);
        }
        Console.WriteLine("PASS: pending warning cancels on correction and later config changes are observed.");
        {
            var t=Setup(); using var notice=t.notice; Singleton<NotificationManager>.Instantiated=false;
            var run=notice.Run(); Step(run); t.own.Enabled.Value=false;
            Singleton<NotificationManager>.Instantiated=true; Step(run); Check(NotificationManager.Messages.Count==0);
        }
        {
            var t=Setup(); using var notice=t.notice; Chainloader.PluginInfos.Clear(); Step(notice.Run());
            Check(t.log.Messages.Count==0 && NotificationManager.Messages.Count==0 && t.other.SubscriberCount==0);
        }
        {
            var t=Setup(); var run=t.notice.Run(); run.MoveNext(); t.notice.Dispose(); Step(run);
            Check(t.other.SubscriberCount==0 && NotificationManager.Messages.Count==0);
        }
        Console.WriteLine("PASS: no UI-Fixes and disposal before initialization exit cleanly.");
    }
}

// Engine/config stand-ins let the production coroutine and insertion prefix run
// outside Unity. Actual game signatures are checked by the main project build.
namespace BepInEx.Configuration
{
    class ConfigDefinition(string section,string key) { public string Section=section,Key=key; }
    class ConfigEntryBase { public string Serialized; public string GetSerializedValue()=>Serialized; }
    class ConfigEntry<T>:ConfigEntryBase
    {
        T _value;
        public event EventHandler SettingChanged;
        public int SubscriberCount=>SettingChanged?.GetInvocationList().Length??0;
        public T Value {get=>_value;set{_value=value;SettingChanged?.Invoke(this,EventArgs.Empty);}}
    }
    class ConfigFile
    {
        readonly Dictionary<string,ConfigEntryBase> _entries=new();
        public ConfigEntry<bool> Enabled;
        public event EventHandler SettingChanged;
        public int SubscriberCount=>SettingChanged?.GetInvocationList().Length??0;
        public ConfigEntry<T> Bind<T>(string section,string key,T value,string description)
        {
            var entry=new ConfigEntry<T>{Value=value,Serialized=value?.ToString()};
            _entries.Add(section+"/"+key,entry);
            if(entry is ConfigEntry<bool> enabled)Enabled=enabled;
            return entry;
        }
        public bool ContainsKey(ConfigDefinition d)=>_entries.ContainsKey(d.Section+"/"+d.Key);
        public ConfigEntryBase this[ConfigDefinition d]=>this[d.Section,d.Key];
        public ConfigEntryBase this[string section,string key]=>_entries[section+"/"+key];
        public void Change(string value){this["C. Gameplay","Modify Weapons In Raid"].Serialized=value;SettingChanged?.Invoke(this,EventArgs.Empty);}
    }
}
namespace BepInEx.Logging { class ManualLogSource {public List<string> Messages=new();public void LogWarning(object message)=>Messages.Add(message.ToString());} }
namespace BepInEx.Bootstrap
{
    class Instance {public ConfigFile Config;}
    class PluginInfo {public Instance Instance;}
    static class Chainloader {public static Dictionary<string,PluginInfo> PluginInfos=new();}
}
namespace Comfort.Common {static class Singleton<T>{public static bool Instantiated;}}
namespace EFT.Communications {class NotificationManager {public static List<string> Messages=new();public static void DisplayWarningNotification(string message)=>Messages.Add(message);}}
namespace UnityEngine {class WaitForSecondsRealtime {public WaitForSecondsRealtime(float seconds) { _=seconds; }}}
