using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ValheimBridge
{
    public sealed class VectorJson : JsonConverter
    {
        public override bool CanConvert(Type t) { return t == typeof(Vector3); }
        public override void WriteJson(JsonWriter w, object value, JsonSerializer s) { var v=(Vector3)value; w.WriteStartObject(); w.WritePropertyName("x"); w.WriteValue(v.x); w.WritePropertyName("y"); w.WriteValue(v.y); w.WritePropertyName("z"); w.WriteValue(v.z); w.WriteEndObject(); }
        public override object ReadJson(JsonReader r, Type t, object existing, JsonSerializer s) { throw new NotSupportedException(); }
    }
    [Serializable] public class Command { public string id; public string action; public string item; public string prefab; public string world; public int amount; public bool deposit; public float x; public float z; public float seconds; public int target; }
    [Serializable] public class Thing { public string prefab; public string item; public int amount; public int id; public string kind; public string name; public Vector3 position; public float distance; }
    [Serializable] public class Stack { public string name; public string item; public int amount; }
    [Serializable] public class State { public string time; public string scene; public bool focused; public bool ready; public Vector3 position; public float health; public Stack[] inventory; public Thing[] nearby; public string lastResult; public object[] objects; public object[] plants; public object[] drops; public bool night; public bool sleepDue; public bool sleeping; public bool controlled; public string handoff; public string world; public string mode; public object[] crops; public object[] soil; public Vector3[] route; }
    [DefaultExecutionOrder(30000)]
    public sealed partial class Bridge : MonoBehaviour
    {
        string root, saves, lastResult = "booting";
        bool started, handoff;
        string mode, handoffReason;
        float leaseUntil;
        Player controlledPlayer;
        ZNetPeer serverPeer;
        long testWorldUid;
        float nextPoll, nextState, moveUntil, lastHealth, readySince;
        Vector3 move;
        Vector3[] route = new Vector3[0];
        public static void Create() { var go = new GameObject("Codex local test bridge"); DontDestroyOnLoad(go); go.AddComponent<Bridge>(); }
        void Awake()
        {
            root = Environment.GetEnvironmentVariable("VALHEIM_BRIDGE_DIR");
            saves = Environment.GetEnvironmentVariable("VALHEIM_BRIDGE_SAVES");
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(saves)) { enabled = false; return; }
            mode = Environment.GetEnvironmentVariable("VALHEIM_BRIDGE_MODE") ?? "test";
            Directory.CreateDirectory(root);
            if (mode == "test") { Directory.CreateDirectory(saves); Utils.SetSaveDataPath(saves); }
            Application.runInBackground = true;
            Doorstop.Entrypoint.Log("Bridge ready; isolated saves: " + saves);
        }
        void Update()
        {
            Application.runInBackground = true;
            if (Time.realtimeSinceStartup >= nextPoll)
            {
                nextPoll = Time.realtimeSinceStartup + .1f;
                string path = Path.Combine(root, "command.json");
                if (File.Exists(path))
                {
                    try { var command = JsonUtility.FromJson<Command>(File.ReadAllText(path)); File.Delete(path); try { Execute(command); } catch(Exception ex) {lastResult=command.id+": ERROR "+ex.GetBaseException().Message;moveUntil=0;} }
                    catch (Exception ex) { lastResult = "ERROR " + ex; moveUntil = 0; }
                }
            }
            if (mode == "test" && Environment.GetEnvironmentVariable("VALHEIM_BRIDGE_FIXTURE_SAFE") == "1" && Player.m_localPlayer) Player.m_localPlayer.SetGhostMode(true);
            CheckSafety();
            if (Time.realtimeSinceStartup >= nextState) { nextState = Time.realtimeSinceStartup + .25f; WriteState(); }
        }
        void LateUpdate() { Drive(); }
        void FixedUpdate() { Drive(); }
        bool IsTestWorld() { return mode == "test" && started && ZNet.instance && ZNet.instance.IsServer() && ZNet.instance.GetWorld() != null && ZNet.instance.GetWorld().m_uid == testWorldUid && ZNet.instance.GetWorld().m_name == "Codex Local Test" && ZNet.instance.GetConnectedPeers().Count == 0; }
        bool AllowedWorld() { return IsTestWorld() || (mode == "server" && started && ZNet.instance && !ZNet.instance.IsServer() && serverPeer != null && ZNet.instance.GetServerPeer() == serverPeer && Player.m_localPlayer == controlledPlayer); }
        bool Controlled() { return AllowedWorld() && !handoff && Time.realtimeSinceStartup < leaseUntil; }
        void Release(string reason, bool danger = false)
        {
            moveUntil = 0; leaseUntil = 0;
            if(InventoryGui.instance) InventoryGui.instance.Hide();
            var p = Player.m_localPlayer;
            if (p) { p.SetMoveDir(Vector3.zero); var pc = p.GetComponent<PlayerController>(); if (pc) pc.enabled = true; }
            if (danger) { handoff = true; handoffReason = reason; NotifyTakeover(reason); }
            lastResult = "HANDOFF: " + reason;
        }
        void CheckSafety()
        {
            var p = Player.m_localPlayer;
            if (!p) { if (controlledPlayer) Release("player disconnected", true); return; }
            if (!p.InCutscene() && readySince == 0) readySince = Time.realtimeSinceStartup;
            float health = p.GetHealth();
            if (Controlled())
            {
                string danger = DangerReason(p);
                if (danger != null) Release(danger, true);
                else if (readySince > 0 && Time.realtimeSinceStartup > readySince + 10 && lastHealth > 0 && health < lastHealth - .1f) Release("health decreased", true);
            }
            else if (leaseUntil > 0) Release("controller heartbeat expired or world changed");
            lastHealth = health;
            if (Input.GetKeyDown(KeyCode.F8)) Release("manual F8 takeover", true);
        }
        void Drive()
        {
            var p = Player.m_localPlayer;
            if (!p || !Controlled()) return;
            var controller = p.GetComponent<PlayerController>();
            if (controller) controller.enabled = false;
            if (Time.realtimeSinceStartup < moveUntil && !p.IsDead() && !p.InBed()) { p.SetLookDir(move); p.SetMoveDir(move); p.SetRun(false); }
            else p.SetMoveDir(Vector3.zero);
        }
        void Execute(Command c)
        {
            if (c.action == "start")
            {
                if (mode != "test" || started || Player.m_localPlayer || !FejdStartup.instance) throw new Exception("Start requires title screen");
                var profile = new PlayerProfile("codexlocaltest", FileHelpers.FileSource.Local);
                if (!profile.Load()) { profile.SetName("Codex Test"); profile.m_firstSpawn = false; profile.Save(); }
                Game.SetProfile("codexlocaltest", FileHelpers.FileSource.Local);
                var world = World.GetCreateWorld("Codex Local Test", FileHelpers.FileSource.Local);
                testWorldUid = world.m_uid;
                ZNet.SetServer(true, false, false, world.m_name, "", world); ZNet.ResetServerHost();
                typeof(FejdStartup).GetMethod("LoadMainScene", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(FejdStartup.instance, null);
                started = true; lastResult = c.id + ": opened isolated test character and local world"; return;
            }
            if (c.action == "release") { Release("requested takeover"); lastResult = c.id + ": released"; return; }
            if (c.action == "arm")
            {
                var current = Player.m_localPlayer;
                if (!current || (current.InCutscene() && !current.InBed()) || Time.realtimeSinceStartup < readySince + 10) throw new Exception("Wait for stable loaded player");
                if (mode == "server")
                {
                    if (!ZNet.instance || ZNet.instance.IsServer() || ZNet.instance.GetServerPeer() == null) throw new Exception("Join the server first");
                    if (string.IsNullOrEmpty(c.world) || c.world != ZNet.instance.GetWorldName()) throw new Exception("Explicit matching world name required");
                    serverPeer = ZNet.instance.GetServerPeer(); controlledPlayer = current; started = true;
                }
                if (!AllowedWorld()) throw new Exception("World is not authorized");
                string danger = DangerReason(current); if (danger != null) throw new Exception(danger);
                handoff = false; handoffReason = null; controlledPlayer = current; lastHealth = current.GetHealth(); leaseUntil = Time.realtimeSinceStartup + 8;
                lastResult = c.id + ": armed"; return;
            }
            if (c.action == "status") { lastResult = c.id + ": status"; return; }
            var p = Player.m_localPlayer;
            if (!Controlled() || !p || (p.InCutscene() && !p.InBed())) throw new Exception("Isolated test player is not ready");
            if(p.InBed() && c.action!="heartbeat" && c.action!="save" && c.action!="test_day" && c.action!="test_night") throw new Exception("Wait until waking before chores");
            leaseUntil = Time.realtimeSinceStartup + 8;
            switch (c.action)
            {
                case "heartbeat": break;
                case "move":
                    move = new Vector3(c.x,0,c.z).normalized; moveUntil = Time.realtimeSinceStartup + Mathf.Clamp(c.seconds,0,3); break;
                case "stop": moveUntil = 0; p.SetMoveDir(Vector3.zero); break;
                case "save": Game.instance.SavePlayerProfile(true); if (ZNet.instance.IsServer()) ZNet.instance.Save(false,true,true); break;
                case "pick":
                    var pick = FindObjectsByType<Pickable>(FindObjectsSortMode.None).FirstOrDefault(t => t.GetInstanceID() == c.target);
                    if (!pick || Vector3.Distance(p.transform.position,pick.transform.position) > 2.5f || !pick.CanBePicked()) throw new Exception("Pickable unavailable or out of reach");
                    if (!pick.Interact(p,false,false)) throw new Exception("Game rejected interaction"); break;
                default: if (!ExecuteOperation(c,p)) throw new Exception("Unknown command"); break;
            }
            lastResult = c.id + ": " + c.action + " accepted";
        }
        void WriteState()
        {
            try
            {
                var p = Player.m_localPlayer;
                var s = new State { time = DateTime.UtcNow.ToString("O"), scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, focused = Application.isFocused, ready = p && !p.InCutscene(), lastResult = lastResult };
                if (p)
                {
                    s.position = p.transform.position; s.health = p.GetHealth();
                    s.inventory = p.GetInventory().GetAllItems().Select(i => new Stack { name = i.m_shared.m_name, item = i.m_dropPrefab ? i.m_dropPrefab.name : "", amount = i.m_stack }).ToArray();
                    s.nearby = FindObjectsByType<Pickable>(FindObjectsSortMode.None).Where(t => t.CanBePicked() && Vector3.Distance(p.transform.position,t.transform.position) < 35).Select(t => new Thing { prefab=t.name.Replace("(Clone)",""),item=t.m_itemPrefab?t.m_itemPrefab.name:"",amount=t.m_amount,id=t.GetInstanceID(),kind="pickable",name=t.GetHoverName(),position=t.transform.position,distance=Vector3.Distance(p.transform.position,t.transform.position) }).OrderBy(t=>t.distance).Take(40).ToArray();
                }
                EnrichState(s,p);
                string path = Path.Combine(root,"state.json"); File.WriteAllText(path + ".tmp",JsonConvert.SerializeObject(s,Formatting.Indented,new VectorJson())); if (File.Exists(path)) File.Delete(path); File.Move(path + ".tmp",path);
            }
            catch (Exception ex) { Doorstop.Entrypoint.Log("State: " + ex.Message); }
        }
    }
}
