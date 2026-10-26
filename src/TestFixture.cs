using Splatform;
using System;
using System.Linq;
using UnityEngine;

namespace ValheimBridge
{
    public sealed partial class Bridge
    {
        GameObject SpawnTest(string prefab, Vector3 point, float yaw=0)
        {
            var template=ZNetScene.instance.GetPrefab(prefab);if(!template)throw new Exception("Missing test prefab "+prefab);
            var go=Instantiate(template,point,Quaternion.Euler(0,yaw,0));
            var piece=go.GetComponent<Piece>();if(piece)piece.SetCreator(Player.m_localPlayer.GetPlayerID(),PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            return go;
        }
        bool TestOperation(Command c, Player p)
        {
            if(!c.action.StartsWith("test_")) return false;
            if(!IsTestWorld()) throw new Exception("Test fixtures are forbidden outside isolated local world");
            switch(c.action)
            {
                case "test_travel":
                    var point=new Vector3(c.x,0,c.z);point.y=ZoneSystem.instance.GetGroundHeight(point)+1;
                    p.TeleportTo(point,Quaternion.identity,true);return true;
                case "test_fixture":
                    var center=p.transform.position;center.y=ZoneSystem.instance.GetGroundHeight(center);
                    // Fixture provisioning only; chores still use normal resources and processing.
                    foreach(var pair in new[]{"Cultivator:1","CarrotSeeds:8","TurnipSeeds:8","OnionSeeds:8","Carrot:6","Turnip:6","Onion:6"})
                    { var a=pair.Split(':');p.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(a[0]),int.Parse(a[1])); }
                    var kiln=SpawnTest("charcoal_kiln",center+new Vector3(8,0,0));
                    var smelter=SpawnTest("smelter",center+new Vector3(8,0,8));
                    var chest=SpawnTest("piece_chest_wood",center+new Vector3(-3,0,0)).GetComponent<Container>();
                    chest.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Wood"),10);
                    chest.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("IronOre"),2);
                    SpawnTest("piece_chest_wood",center+new Vector3(-3,0,4));
                    SpawnTest("bed",center+new Vector3(0,0,12));
                    return true;
                case "test_shelter":
                    var bedPos=p.transform.position+new Vector3(0,0,12);bedPos.y=ZoneSystem.instance.GetGroundHeight(bedPos)+.1f;
                    center=bedPos-new Vector3(0,0,12);
                    var flatten=new TerrainOp.Settings {m_level=true,m_levelRadius=5,m_square=true,m_paintCleared=false};
                    Call(Heightmap.FindHeightmap(bedPos).GetAndCreateTerrainCompiler(),"DoOperation",bedPos,Vector3.zero,flatten);
                    for(int x=-2;x<=2;x+=2) for(int z=-2;z<=2;z+=2) SpawnTest("wood_floor",bedPos+new Vector3(x,0,z));
                    var bed=SpawnTest("bed",bedPos).GetComponent<Bed>();
                    for(int x=-2;x<=2;x+=2) for(int z=-2;z<=2;z+=2) SpawnTest("wood_roof_45",bedPos+new Vector3(x,2,z));
                    for(int a=-2;a<=2;a+=2) { SpawnTest("woodwall",bedPos+new Vector3(a,0,-3));SpawnTest("woodwall",bedPos+new Vector3(a,0,3));SpawnTest("woodwall",bedPos+new Vector3(-3,0,a),90);SpawnTest("woodwall",bedPos+new Vector3(3,0,a),90); }
                    SpawnTest("fire_pit",bedPos+new Vector3(3.5f,0,0));
                    Call(bed,"SetOwner",p.GetPlayerID(),Game.instance.GetPlayerProfile().GetName());
                    Game.instance.GetPlayerProfile().SetCustomSpawnPoint(bed.GetSpawnPoint());
                    // Apply cultivated soil using the game's terrain operation system.
                    var settings=new TerrainOp.Settings {m_paintType=TerrainModifier.PaintType.Cultivate,m_paintRadius=6,m_paintCleared=true,m_square=true};
                    Call(Heightmap.FindHeightmap(center).GetAndCreateTerrainCompiler(),"DoOperation",center,Vector3.zero,settings);
                    return true;
                case "test_spawn":
                    var spawnPos=new Vector3(c.x,0,c.z);spawnPos.y=ZoneSystem.instance.GetGroundHeight(spawnPos);
                    if(Utils.DistanceXZ(spawnPos,p.transform.position)>35) throw new Exception("Test fixture out of range");
                    SpawnTest(c.prefab,spawnPos);return true;
                case "test_grow":
                    var plant=Target<Plant>(c.target);plant.UpdateHealth(20);if(!plant.Grow())throw new Exception("Test plant not healthy");return true;
                case "test_night":
                    EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.85f;return true;
                case "test_day": EnvMan.instance.m_debugTimeOfDay=false;return true;
                case "test_damage": p.Damage(new HitData {m_damage={m_damage=5f}});return true;
                case "test_raid":
                    var ev=RandEventSystem.instance.m_events.First(t=>t.m_name=="army_eikthyr").Clone();
                    ev.m_spawn.Clear();ev.m_pos=p.transform.position;
                    Call(RandEventSystem.instance,"SetRandomEvent",ev,p.transform.position);return true;
                default:throw new Exception("Unknown test operation");
            }
        }
    }
}
