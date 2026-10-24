using System.Reflection;
using EFT;
static class Events { public static List<string> Calls=new(); }
class Program {
    static readonly MethodInfo Prefix=typeof(DefinitiveWeaponVariants.Client.Patches.CoreSlotAddModPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo Postfix=typeof(DefinitiveWeaponVariants.Client.Patches.CoreSlotAddModPatch).GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static);
    static bool Invoke(Player.FirearmController.AddModOperation op, bool uiFixes=false) {
        object[] args={op,null};
        bool runOriginal=(bool)Prefix.Invoke(null,args);
        // HarmonyX invokes all prefixes, even after false. This is UI-Fixes'
        // actual first guard; entering its model path for a core must fail here.
        if(uiFixes && !op._onModChanged && op._slot?.ID=="mod_core")throw new Exception("UI-Fixes would create a core model");
        Postfix.Invoke(null,new[]{args[1]});
        return runOriginal;
    }
    static void Main() {
        var slot=new Slot{ID="mod_core",Item=new object()};
        var op=new Player.FirearmController.AddModOperation {
            _slot=slot, FirearmsAnimator=new(), Weapon=new(){Folded=true}, Player=new(), Controller=new()
        };
        int callbacks=0;
        op._callback=new Comfort.Common.Callback(()=>{
            if(op.State!=Player.EOperationState.Finished || !op._onModChanged)throw new Exception("callback before completion");
            if(!op.Controller.IsIdle)throw new Exception("callback before idle");
            callbacks++;Events.Calls.Add("success");
            // A callback may synchronously reset/reuse the operation. The handler
            // must perform no work through these fields after calling success.
            op.Controller=null;op.Player=null;op.Weapon=null;op.FirearmsAnimator=null;op._callback=null;
        });
        var prefix=typeof(DefinitiveWeaponVariants.Client.Patches.CoreSlotAddModPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
        var before=prefix.GetCustomAttributesData().Single(a=>a.AttributeType.FullName=="HarmonyLib.HarmonyBefore");
        var owners=(IEnumerable<CustomAttributeTypedArgument>)before.ConstructorArguments.Single().Value;
        if(!owners.Any(a=>(string)a.Value=="FixAddModFirearmOperationPatch"))throw new Exception("UI-Fixes ordering missing");
        var item=slot.Item;
        var controller=op.Controller;
        if(Invoke(op,true)) throw new Exception("core ran original");
        string[] expected={"setup:False","fold:True","finished","size:7","grip","modified","initiate","idle","success"};
        if(!Events.Calls.SequenceEqual(expected))throw new Exception(string.Join(",",Events.Calls));
        if(callbacks!=1 || !ReferenceEquals(slot.Item,item) || !controller.IsIdle)throw new Exception("incorrect completion");
        if(Invoke(op,true) || callbacks!=1 || Events.Calls.Count!=expected.Length)throw new Exception("duplicate completion");
        foreach(var id in new[]{"mod_scope","mod_magazine","mod_core_extra","MOD_CORE","",null}) {
            var normal=new Player.FirearmController.AddModOperation{_slot=new(){ID=id}};
            if(!Invoke(normal,true) || normal._onModChanged)throw new Exception("normal insertion changed");
        }
        if(!Invoke(new Player.FirearmController.AddModOperation()))throw new Exception("null slot intercepted");
        // Callback can reset the event guard for the next operation only after
        // UI-Fixes has returned from its prefix.
        var reused=new Player.FirearmController.AddModOperation{_slot=new(){ID="mod_core"},FirearmsAnimator=new(),Weapon=new(),Player=new(),Controller=new()};
        reused._callback=new Comfort.Common.Callback(()=>reused._onModChanged=false);
        Invoke(reused,true);
        int standaloneCallbacks=0;
        var standalone=new Player.FirearmController.AddModOperation{_slot=new(){ID="mod_core"},FirearmsAnimator=new(),Weapon=new(),Player=new(),Controller=new()};
        standalone._callback=new Comfort.Common.Callback(()=>standaloneCallbacks++);
        if(Invoke(standalone) || !standalone.Controller.IsIdle || standaloneCallbacks!=1)throw new Exception("standalone insertion failed");
        Console.WriteLine("PASS: production prefix executes full core branch with engine stand-ins, success once, retained item, fold/size/grip/stat updates and return to idle.");
        Console.WriteLine("PASS: repeated event does not repeat callback; ordinary attachment paths are untouched.");
        Console.WriteLine("PASS: ordering attribute names the verified UI-Fixes Harmony owner.");
        Console.WriteLine("PASS: later UI-Fixes prefix sees the guard before a callback can reset it.");
        NoticeChecks.Run();
    }
}
namespace SPT.Reflection.Patching {
    abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    class PatchPrefixAttribute:Attribute {}
    class PatchPostfixAttribute:Attribute {}
}
namespace Comfort.Common { class Callback(Action run) { public void Succeed()=>run(); } }
class Slot { public string ID; public object Item; }
class FirearmsAnimator {
    public void SetupMod(bool value)=>Events.Calls.Add("setup:"+value);
    public void Fold(bool value)=>Events.Calls.Add("fold:"+value);
}
class Weapon {
    public bool Folded;
    public (int X,int Y) CalculateCellSize()=>(7,2);
}
class PlayerAnimator {
    public const int WEAPON_SIZE_MODIFIER_PARAM_HASH=123;
    public void SetFloat(int hash,float value) { if(hash!=123)throw new Exception("wrong size parameter");Events.Calls.Add("size:"+value); }
}
class GripPose { public enum EGripType{Common} }
namespace EFT {
    class Player {
        public enum EOperationState { Started,Finished }
        public PlayerAnimator BodyAnimatorCommon=new();
        public void UpdateFirstPersonGrip(GripPose.EGripType grip,object hierarchy) { if(hierarchy==null)throw new Exception("missing hierarchy");Events.Calls.Add("grip"); }
        public class FirearmController {
            public object HandsHierarchy=new();
            public bool IsIdle;
            public void WeaponModified()=>Events.Calls.Add("modified");
            public T InitiateOperation<T>() where T:Idling,new() { Events.Calls.Add("initiate"); return new T{Controller=this}; }
            public class Idling {
                public FirearmController Controller;
                public void Start(object argument) { Controller.IsIdle=true;Events.Calls.Add("idle"); }
            }
            public class AddModOperation {
                public Slot _slot;
                public bool _onModChanged;
                public Comfort.Common.Callback _callback;
                public FirearmsAnimator FirearmsAnimator;
                public Weapon Weapon;
                public Player Player;
                public FirearmController Controller;
                EOperationState state;
                public EOperationState State { get=>state;set{state=value;Events.Calls.Add("finished");} }
                public void OnModChanged()=>throw new Exception("original invoked");
            }
        }
    }
}
