using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ValheimBridge
{
    public sealed partial class Bridge
    {
        static object Call(object obj, string method, params object[] args)
        {
            var methods = obj.GetType().GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            var m = methods.First(t=>t.Name==method && t.GetParameters().Length==args.Length);
            return m.Invoke(obj,args);
        }
        static T Target<T>(int id) where T : Component
        {
            var t = FindObjectsByType<T>(FindObjectsSortMode.None).FirstOrDefault(v=>v.GetInstanceID()==id);
            if (!t) throw new Exception("Target is no longer loaded");
            return t;
        }
        static void Reach(Player p, Component t, float radius = 3f)
        {
            // Machines are large; measure against their interaction switch where possible.
            if (Vector3.Distance(p.transform.position,t.transform.position)>radius) throw new Exception("Walk closer to target");
            if (!PrivateArea.CheckAccess(t.transform.position)) throw new Exception("Ward access denied");
        }
        static ItemDrop.ItemData Item(Inventory inv, string name)
        {
            var item = inv.GetAllItems().FirstOrDefault(i=>i.m_dropPrefab && i.m_dropPrefab.name==name);
            if (item == null) throw new Exception("Missing item: "+name);
            return item;
        }
        void NotifyTakeover(string reason)
        {
            if (MessageHud.instance) Player.m_localPlayer.Message(MessageHud.MessageType.Center,"TAKE OVER: "+reason);
            try { System.Diagnostics.Process.Start("/usr/bin/afplay","/System/Library/Sounds/Sosumi.aiff"); } catch(Exception ex) { Doorstop.Entrypoint.Log("Takeover sound: "+ex.Message); }
            try { System.Diagnostics.Process.Start("/usr/bin/osascript", "-e \"display notification \\\"Valheim needs you: automation stopped. Check the game and Codex.\\\" with title \\\"Valheim takeover\\\"\""); } catch(Exception ex) { Doorstop.Entrypoint.Log("Takeover notification: "+ex.Message); }
        }
        string DangerReason(Player p)
        {
            if (p.IsDead()) return "character died";
            var events = RandEventSystem.instance;
            if (events)
            {
                var ev = events.GetActiveEvent() ?? events.GetCurrentRandomEvent();
                if (ev != null && (events.GetActiveEvent()!=null || Utils.DistanceXZ(ev.m_pos,p.transform.position)<ev.m_eventRange+40)) return "RAID: "+ev.m_name+" "+Localization.instance.Localize(ev.m_startMessage);
            }
            foreach (var c in Character.GetAllCharacters())
            {
                if (c==p || c.IsDead() || Vector3.Distance(c.transform.position,p.transform.position)>18) continue;
                var ai=c.GetBaseAI();
                if (ai is MonsterAI && BaseAI.IsEnemy(c,p) && (ai.IsAlerted() || ai.GetTargetCreature()==p)) return "hostile creature approaching: "+c.GetHoverName();
            }
            return null;
        }
        bool ExecuteOperation(Command c, Player p)
        {
            string danger=DangerReason(p); if(danger!=null) { Release(danger,true); throw new Exception(danger); }
            switch(c.action)
            {
                case "route":
                    var end=new Vector3(c.x,p.transform.position.y,c.z);end.y=ZoneSystem.instance.GetGroundHeight(end);
                    if(Utils.DistanceXZ(end,p.transform.position)>60) throw new Exception("Route exceeds local chore radius");
                    var path=new System.Collections.Generic.List<Vector3>();
                    bool found=Pathfinding.instance && Pathfinding.instance.GetPath(p.transform.position,end,path,Pathfinding.AgentType.HumanoidNoSwim,true);
                    route=found?path.ToArray():new Vector3[0];return true;
                case "door":
                    var door=Target<Door>(c.target);Reach(p,door,3);
                    if(door.m_keyItem || door.m_canNotBeClosed) throw new Exception("Only ordinary unlocked base doors are automated");
                    if(!door.Interact(p,false,false)) throw new Exception("Door is busy");return true;
                case "open":
                    var chest=Target<Container>(c.target); Reach(p,chest);
                    if (chest.IsInUse()) { var opened=typeof(InventoryGui).GetField("m_currentContainer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(InventoryGui.instance) as Container; if(opened==chest && chest.IsOwner()) return true; throw new Exception("Chest is already in use"); }
                    chest.Interact(p,false,false); return true;
                case "close": if(InventoryGui.instance) InventoryGui.instance.Hide(); return true;
                case "transfer": Transfer(c,p); return true;
                case "fuel": case "ore":
                    var machine=Target<Smelter>(c.target);
                    var sw=c.action=="fuel" ? machine.m_addWoodSwitch : machine.m_addOreSwitch;
                    if(!sw) throw new Exception("Machine has no such input"); Reach(p,sw,3.5f);
                    var material=Item(p.GetInventory(),c.item);
                    if(!(bool)Call(machine,c.action=="fuel"?"OnAddFuel":"OnAddOre",sw,p,material)) throw new Exception("Machine rejected item (full or wrong material)");
                    return true;
                case "plant": PlantCrop(c,p); return true;
                case "sleep":
                    var bed=Target<Bed>(c.target); Reach(p,bed);
                    if(!bed.IsCurrent()) throw new Exception("Choose your existing claimed bed");
                    if(!EnvMan.CanSleep()) throw new Exception("It is not sleep time");
                    bed.Interact(p,false,false);
                    if(!p.InBed()) throw new Exception("Bed refused sleep: shelter="+Call(bed,"CheckExposure",p)+", fire="+Call(bed,"CheckFire",p)+", dry="+Call(bed,"CheckWet",p)+", safe="+Call(bed,"CheckEnemies",p));
                    return true;
                case "pickup":
                    var drop=Target<ItemDrop>(c.target); Reach(p,drop,2f);
                    if(!p.GetInventory().CanAddItem(drop.m_itemData) || p.GetInventory().GetTotalWeight()+drop.m_itemData.GetWeight()>p.GetMaxCarryWeight()) throw new Exception("Not enough inventory capacity");
                    p.Pickup(drop.gameObject,false,false); return true;
                default: return TestOperation(c,p);
            }
        }
        void Transfer(Command c, Player p)
        {
            var chest=Target<Container>(c.target); Reach(p,chest);
            if(!(bool)Call(chest,"CheckAccess",p.GetPlayerID())) throw new Exception("Chest access denied");
            var current=typeof(InventoryGui).GetField("m_currentContainer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(InventoryGui.instance) as Container;
            if(!chest.IsOwner() || current!=chest) throw new Exception("Open chest and wait for ownership before transferring");
            var from=c.deposit?p.GetInventory():chest.GetInventory(); var to=c.deposit?chest.GetInventory():p.GetInventory();
            var item=Item(from,c.item); int count=Mathf.Clamp(c.amount,1,item.m_stack);
            if(!to.CanAddItem(item,count)) throw new Exception("Destination inventory is full");
            var clone=item.Clone(); clone.m_stack=count; clone.m_equipped=false;
            if(!c.deposit && to.GetTotalWeight()+clone.GetWeight()>p.GetMaxCarryWeight()) throw new Exception("Carry weight limit");
            if(p.IsItemEquiped(item)) throw new Exception("Do not transfer equipped items");
            if(!to.AddItem(clone)) throw new Exception("Transfer failed");
            from.RemoveItem(item,count);
            Call(chest,"Save");
        }
        void PlantCrop(Command c, Player p)
        {
            var tool=Item(p.GetInventory(),"Cultivator");
            if(tool.m_durability<=0) throw new Exception("Cultivator needs repair");
            var prefab=ZNetScene.instance.GetPrefab(c.prefab); var piece=prefab?prefab.GetComponent<Piece>():null;
            if(!piece || !prefab.GetComponent<Plant>() || !tool.m_shared.m_buildPieces.m_pieces.Contains(prefab)) throw new Exception("Only cultivator plants are allowed");
            if(!p.HaveRequirements(piece,Player.RequirementMode.CanBuild)) throw new Exception("Missing seeds or crop requirements");
            var pos=new Vector3(c.x,p.transform.position.y,c.z); pos.y=ZoneSystem.instance.GetGroundHeight(pos);
            if(Vector3.Distance(p.transform.position,pos)>3.5f) throw new Exception("Walk closer to planting point");
            var plant=prefab.GetComponent<Plant>();
            if(FindObjectsByType<Plant>(FindObjectsSortMode.None).Where(v=>v.GetComponent<ZNetView>() && v.GetComponent<ZNetView>().IsValid()).Any(v=>Utils.DistanceXZ(v.transform.position,pos)<Mathf.Max(v.m_growRadius,plant.m_growRadius)+.15f)) throw new Exception("Plant needs more space");
            if(!p.IsItemEquiped(tool) && !p.EquipItem(tool,false)) throw new Exception("Cannot equip cultivator");
            Call(p,"UpdateAvailablePiecesList");
            if(!p.SetSelectedPiece(piece)) throw new Exception("Plant recipe unavailable");
            Call(p,"SetupPlacementGhost");
            float stamina=(float)Call(p,"GetBuildStamina"); if(!p.HaveStamina(stamina)) throw new Exception("Wait for stamina");
            var camera=GameCamera.instance.transform; var oldPos=camera.position;var oldRot=camera.rotation;
            try
            {
                camera.position=pos+Vector3.up*2;camera.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
                Call(p,"UpdatePlacementGhost",false);
                var ghost=typeof(Player).GetField("m_placementGhost",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(p) as GameObject;
                if(!ghost || !ghost.activeInHierarchy) throw new Exception("Game placement validation rejected plant: no valid ghost");
                Physics.SyncTransforms();
                var preview=ghost.GetComponent<Plant>();preview.UpdateHealth(20);
                if(preview.GetStatus()!=Plant.Status.Healthy) throw new Exception("Game placement validation rejected plant growth: "+preview.GetStatus());
                if(!p.TryPlacePiece(piece)) throw new Exception("Game placement validation rejected plant: "+typeof(Player).GetField("m_placementStatus",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(p));
                if(!ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) p.ConsumeResources(piece.m_resources,0);
                p.UseStamina(stamina);
                if(tool.m_shared.m_useDurability) tool.m_durability-=(float)Call(p,"GetPlaceDurability",tool)*Game.m_durabilityRate;
            }
            finally { camera.position=oldPos;camera.rotation=oldRot; }
        }
        void EnrichState(State s, Player p)
        {
            s.route=route;
            s.mode=mode; s.world=ZNet.instance?ZNet.instance.GetWorldName():null; s.controlled=Controlled(); s.handoff=handoffReason; s.night=EnvMan.IsNight();s.sleepDue=EnvMan.instance && EnvMan.CanSleep() && (EnvMan.instance.GetDayFraction()>=.75f || EnvMan.instance.GetDayFraction()<.15f);s.sleeping=p&&p.InBed();
            if(!p || !ZNetScene.instance) return;
            Func<Component,bool> near=t=>t.GetComponent<ZNetView>() && t.GetComponent<ZNetView>().IsValid() && Vector3.Distance(t.transform.position,p.transform.position)<45;
            s.objects=FindObjectsByType<Container>(FindObjectsSortMode.None).Where(t=>near(t)).Select(t=>(object)new { id=t.GetInstanceID(),kind="chest",name=t.name,position=t.transform.position,owned=t.IsOwner(),inUse=t.IsInUse(),inventory=t.GetInventory().GetAllItems().Select(i=>new {item=i.m_dropPrefab?i.m_dropPrefab.name:"",amount=i.m_stack}) }).Concat(
                FindObjectsByType<Smelter>(FindObjectsSortMode.None).Where(t=>near(t)).Select(t=>(object)new {id=t.GetInstanceID(),kind="machine",name=t.name,position=t.transform.position,fuel=Call(t,"GetFuel"),queue=Call(t,"GetQueueSize"),fuelPort=t.m_addWoodSwitch?t.m_addWoodSwitch.transform.position:t.transform.position,orePort=t.m_addOreSwitch?t.m_addOreSwitch.transform.position:t.transform.position,maxFuel=t.m_maxFuel,maxOre=t.m_maxOre,fuelPerProduct=t.m_fuelPerProduct,secondsPerProduct=t.m_secPerProduct,conversions=t.m_conversion.Select(v=>new{input=v.m_from.name,output=v.m_to.name})})).Concat(
                FindObjectsByType<Bed>(FindObjectsSortMode.None).Where(t=>near(t)).Select(t=>(object)new{id=t.GetInstanceID(),kind="bed",name=t.name,position=t.transform.position,mine=t.IsCurrent()})).Concat(FindObjectsByType<Door>(FindObjectsSortMode.None).Where(t=>near(t)).Select(t=>(object)new{id=t.GetInstanceID(),kind="door",name=t.name,position=t.transform.position,open=t.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_state)!=0})).ToArray();
            s.plants=FindObjectsByType<Plant>(FindObjectsSortMode.None).Where(t=>near(t)).Select(t=>(object)new{id=t.GetInstanceID(),name=t.name,position=t.transform.position,health=t.GetHoverText()}).ToArray();
            s.drops=FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(t=>near(t)).Select(t=>(object)new{id=t.GetInstanceID(),item=t.m_itemData.m_dropPrefab?t.m_itemData.m_dropPrefab.name:t.name,amount=t.m_itemData.m_stack,position=t.transform.position}).ToArray();
            var soil=new System.Collections.Generic.List<object>();
            for(float x=-12;x<=12;x+=1.25f) for(float z=-12;z<=12;z+=1.25f)
            {
                var point=p.transform.position+new Vector3(x,0,z);var h=Heightmap.FindHeightmap(point);
                if(h && h.IsCultivated(point)) {point.y=ZoneSystem.instance.GetGroundHeight(point);soil.Add(point);}
            }
            s.soil=soil.ToArray();
            var cultivator=ObjectDB.instance.GetItemPrefab("Cultivator").GetComponent<ItemDrop>();
            s.crops=cultivator.m_itemData.m_shared.m_buildPieces.m_pieces.Where(t=>t.GetComponent<Plant>()).Select(t=>(object)new{prefab=t.name,radius=t.GetComponent<Plant>().m_growRadius,resources=t.GetComponent<Piece>().m_resources.Select(r=>new{item=r.m_resItem.name,amount=r.m_amount})}).ToArray();
        }
    }
}
