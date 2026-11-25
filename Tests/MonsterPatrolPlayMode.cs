// Copy into Assets/Editor for a batch run; it never saves a scene or edits a prefab.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;
using Game.Monsters.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MonsterPatrolPlayMode
{
    private const string Flag="MonsterPatrolPlayMode.Pending";
    private static int checks;
    private static MonsterSnapshotManager manager;
    private static CombatStateStore store;
    private static MonsterCatalog catalog;
    private static void Check(bool ok,string text)
    {
        if(!ok)throw new Exception(text);
        checks++; Debug.Log("PATROL_CHECK "+text);
    }
    private static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
    private static void Set(object o,string name,object value)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Flag,true);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    private static void Resume()
    {
        EditorApplication.playModeStateChanged+=s=>
        {
            if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Flag,false))
                EditorApplication.delayCall+=Test;
        };
    }
    private static void Test()
    {
        try
        {
            SessionState.SetBool(Flag,false);
            catalog=AssetDatabase.LoadAssetAtPath<MonsterCatalog>("Assets/Data/Monsters/怪物总表.asset");
            Check(catalog!=null&&catalog.MonsterAssets.Count>0,"load actual registered catalog");
            foreach(var asset in catalog.MonsterAssets) ValidateAnimation(asset);
            var host=new GameObject("PatrolIsolatedTest");
            store=host.AddComponent<CombatStateStore>();store.BeginScene("world_001");
            manager=host.AddComponent<MonsterSnapshotManager>();
            manager.SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode.CoordinatorOwned);
            Set(manager,"monsterCatalog",catalog);Set(manager,"combatStateStore",store);
            manager.BeginSession(new BattleSyncModuleSessionContext(1,"world_001",1,1,0));
            MonsterDefinitionAsset ground=null,fly=null;
            foreach(var a in catalog.MonsterAssets)
                if(a.Prefab.GetComponentInChildren<FlyingMonsterAnimatorDriver>(true)!=null)fly=a;
                else if(ground==null)ground=a;
            Check(ground!=null&&fly!=null,"catalog has ground and flying models");
            Enter(ground,"ground","Patrol",0,1); Enter(fly,"fly","Hover",2,1);
            var g=View("ground");var f=View("fly");
            Check(f.transform.position.y==2&&Driver(f).GetType()==typeof(FlyingMonsterAnimatorDriver),"late observer starts in hover at server height");
            Dynamic("ground",8,1,true);
            var interpolation=g.GetComponent<MonsterTransformInterpolator>();
            Check(Field(interpolation,"movementPath")!=null,"ground patrol accepts server path");
            var animator=g.GetComponentInChildren<Animator>(true);animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Update(.2f);int stateHash=animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            Dynamic("ground",8,2,false,behavior:false);
            Check(Field(interpolation,"movementPath")!=null&&animator.GetCurrentAnimatorStateInfo(0).fullPathHash==stateHash,"HP-only update preserves path and animation");
            Dynamic("ground",1,3,false);
            Check(Field(interpolation,"movementPath")==null,"idle cancels patrol path");
            Dynamic("ground",8,4,true);Dynamic("ground",4,5,false);
            Check(Field(interpolation,"movementPath")==null,"attack cancels patrol path without playing duplicate action");
            Dynamic("ground",8,5,true);Dynamic("ground",3,5,false);
            Check(Field(interpolation,"movementPath")==null,"chase cannot keep the previous patrol destination");
            Dynamic("ground",5,5,true);Dynamic("ground",8,5,true);
            Check(Field(interpolation,"movementPath")!=null,"return switches back to a fresh patrol path");
            Dynamic("fly",8,6,false);Dynamic("fly",9,7,false);
            var fa=f.GetComponentInChildren<Animator>(true);fa.Update(.2f);
            Check(fa.GetInteger("LocomotionMode")==2&&fa.GetFloat("MoveSpeed")==0&&f.transform.position.y==2,"fly to hover keeps flight loop and height");
            Dynamic("ground",7,8,false);
            Check(!g.GetComponent<Collider>().enabled,"death disables collision");
            Dynamic("ground",8,9,true);
            Check(Field(interpolation,"movementPath")==null&&!g.GetComponent<Collider>().enabled,"late patrol cannot restart dead path or collider");
            Enter(ground,"ground","Patrol",0,2);
            Check(View("ground")!=g&&View("ground").GetComponent<Collider>().enabled,"new generation respawns with fresh animation and collision");
            Check(manager.ApplyMonsterEnter(Message(ground,"ground","Dead",0,1))==BattleSyncApplyResult.IgnoredStale,"old generation cannot kill respawn");
            manager.EndSession(1);
            Check(((IDictionary)Field(manager,"monsters")).Count==0,"disconnect removes views and paths");
            manager.BeginSession(new BattleSyncModuleSessionContext(2,"world_001",1,2,0));Enter(fly,"fly","Hover",2,2);
            Check(View("fly").transform.position.y==2,"reconnect reconstructs current hover");
            manager.EndSession(2);manager.BeginSession(new BattleSyncModuleSessionContext(3,"world_005",1,3,0));
            Check(manager.ApplyMonsterEnter(Message(fly,"fly","Patrol",2,2))==BattleSyncApplyResult.IgnoredWrongScene,"scene change rejects old monster");
            string output=Environment.GetEnvironmentVariable("PATROL_EVIDENCE")??"E:/gameserver/artifacts/unity-monster-patrol-20261124";
            File.WriteAllText(Path.Combine(output,"unity-play-result.json"),"{\"passed\":true,\"checks\":"+checks+",\"registeredModels\":"+catalog.MonsterAssets.Count+"}");
            Debug.Log("MONSTER_PATROL_UNITY_PLAY_PASS checks="+checks);EditorApplication.Exit(0);
        }
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static IMonsterAnimatorDriver Driver(MonsterNetworkView v)
    {
        foreach(var c in v.GetComponentsInChildren<MonoBehaviour>(true))if(c is IMonsterAnimatorDriver d)return d;
        throw new Exception("Missing driver");
    }
    private static void ValidateAnimation(MonsterDefinitionAsset asset)
    {
        var go=UnityEngine.Object.Instantiate(asset.Prefab);var v=go.GetComponent<MonsterNetworkView>();
        var a=go.GetComponentInChildren<Animator>(true);
        Check(a!=null&&a.runtimeAnimatorController!=null,asset.Id+" real animator/controller");
        Check(!a.applyRootMotion,asset.Id+" network root motion disabled");
        a.cullingMode=AnimatorCullingMode.AlwaysAnimate;v.RebindAnimator();
        v.Initialize(new MonsterViewState{entityId=asset.Id,stateSequence=1,hp=100,maxHp=100,rotation=Quaternion.identity});
        v.BootstrapState("Patrol");
        if (Driver(v) is GorosaurusMonsterAnimatorDriver gorosaurus)
        {
            var motion = go.GetComponent<MonsterTransformInterpolator>();
            typeof(MonsterTransformInterpolator).GetProperty("CurrentVelocity").SetValue(motion, Vector3.forward);
            typeof(GorosaurusMonsterAnimatorDriver).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(gorosaurus, null);
            a.Update(.1f);
        }
        a.Update(.2f);int walk=a.GetCurrentAnimatorStateInfo(0).fullPathHash;
        var clips=a.GetCurrentAnimatorClipInfo(0);Check(clips.Length>0&&clips[0].clip!=null,asset.Id+" patrol plays real motion clip");
        float t=a.GetCurrentAnimatorStateInfo(0).normalizedTime;
        v.ApplyState(new MonsterViewState{entityId=asset.Id,stateSequence=2,hp=90,maxHp=100,preserveTransform=true,preservePresentation=true});
        a.Update(.1f);Check(a.GetCurrentAnimatorStateInfo(0).fullPathHash==walk&&a.GetCurrentAnimatorStateInfo(0).normalizedTime>t,asset.Id+" HP update does not restart clip");
        v.BootstrapState(Driver(v) is FlyingMonsterAnimatorDriver?"Hover":"Idle");
        if (Driver(v) is GorosaurusMonsterAnimatorDriver) a.Update(.1f);
        a.Update(.2f);
        if(Driver(v) is FlyingMonsterAnimatorDriver)Check(a.GetCurrentAnimatorStateInfo(0).fullPathHash==walk&&a.GetInteger("LocomotionMode")==2,asset.Id+" hover stays airborne");
        else Check(a.GetCurrentAnimatorStateInfo(0).fullPathHash!=walk,asset.Id+" patrol stops to idle");
        v.BootstrapState("Dead");a.Update(.2f);a.Update(.2f);int dead=a.GetCurrentAnimatorStateInfo(0).fullPathHash;
        v.BootstrapState("Patrol");a.Update(.2f);Check(a.GetCurrentAnimatorStateInfo(0).fullPathHash==dead,asset.Id+" death remains terminal");
        UnityEngine.Object.DestroyImmediate(go);
    }
    private static MonsterNetworkView View(string id)
    {
        object entry=((IDictionary)Field(manager,"monsters"))[id];return (MonsterNetworkView)entry.GetType().GetField("networkView").GetValue(entry);
    }
    private static MonsterEnterMessageDto Message(MonsterDefinitionAsset a,string id,string state,float y,long generation)=>new MonsterEnterMessageDto
    {
        sceneId="world_001",serverTick=100+generation,serverTime=10000,aoiRevision=generation,visibilityRevision=generation,
        monster=new MonsterStateDto{monsterId=a.Id,monsterInstanceId=id,sceneId="world_001",x=5,y=y,z=5,hp=100,maxHp=100,
            state=state,lifeState="Alive",spawnGeneration=generation,combatRevision=1,aoiRevision=generation}
    };
    private static void Enter(MonsterDefinitionAsset a,string id,string state,float y,long gen)=>Check(manager.ApplyMonsterEnter(Message(a,id,state,y,gen))==BattleSyncApplyResult.Applied,"enter "+id+" "+state+" generation="+gen);
    private static void Dynamic(string id,byte ai,long sequence,bool path,bool behavior=true)
    {
        byte flags=(byte)(behavior?85:80);var row=new CompactMonsterStateDto{netId=501,x=500,y=id=="fly"?200:0,z=500,aiState=ai,flags=flags,hp=90,combatRevision=sequence,visibilityRevision=1};
        Check(new CompactStateDecoder().TryDecodeMonster(row,out var state,out _),"decode state "+ai);
        state=state.WithEntityId(id);
        if(!behavior)state=state.WithChangedGroups(MonsterStateDelta.Combat);
        if(path)state=state.WithPath(new MonsterPathDto{startTick=110+sequence,startTime=10000,speed=1.25f,x=5,y=row.y*.01f,z=5,endX=5.5f,endZ=5});
        Check(manager.ApplyMonsterDynamicState(state,110+sequence,10000)==BattleSyncApplyResult.Applied,"apply "+id+" state="+ai);
    }
}
#endif
