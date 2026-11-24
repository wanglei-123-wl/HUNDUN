using System;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;
namespace UnityEngine { public static class Debug { public static void LogWarning(string s) {Console.WriteLine(s);} } }
namespace Game.Client.BattleSync
{
    public interface IBattleSyncClock { long NowMilliseconds { get; } }
    public interface IBattleSyncTransport { bool IsConnected { get; } long ConnectionEpoch { get; } bool SendJson(string s); }
    public interface IBattleSyncJsonCodec
    {
        bool TrySerialize<T>(T value,out string json,out string error) where T:class;
        bool TryDeserialize<T>(string json,out T value,out string error) where T:class;
    }
    public interface IPlayerBattleSyncModule { BattleSyncApplyResult ApplyPlayerDynamicState(DecodedPlayerState s,long tick,long time); }
    public interface IMonsterBattleSyncModule { BattleSyncApplyResult ApplyMonsterDynamicState(DecodedMonsterState s,long tick,long time); }
}
internal sealed class PatrolProtocolTests : IBattleSyncClock,IBattleSyncTransport,IBattleSyncJsonCodec,IPlayerBattleSyncModule,IMonsterBattleSyncModule
{
    public long NowMilliseconds => 1000;
    public bool IsConnected => true;
    public long ConnectionEpoch => 1;
    public bool SendJson(string s) => true;
    public bool TrySerialize<T>(T v,out string json,out string error) where T:class { json="{}";error="";return true; }
    public bool TryDeserialize<T>(string json,out T v,out string error) where T:class { v=null;error="";return false; }
    public int Calls;
    public DecodedMonsterState Last;
    public BattleSyncApplyResult ApplyPlayerDynamicState(DecodedPlayerState s,long tick,long time) => BattleSyncApplyResult.Applied;
    public BattleSyncApplyResult ApplyMonsterDynamicState(DecodedMonsterState s,long tick,long time) { Calls++;Last=s;return BattleSyncApplyResult.Applied; }
    static void Check(bool ok,string reason) { if(!ok)throw new Exception(reason); }
    static string Frame(long sequence,int ai,string extras="",string monsters=null) =>
        "{\"msgId\":\"state_frame\",\"type\":\"state_frame\",\"sceneId\":\"world_001\",\"sceneSessionId\":\"session\",\"serverTick\":"+(100+sequence)+
        ",\"serverTime\":10000,\"frameSequence\":"+sequence+",\"players\":[],\"monsters\":["+
        (monsters??("[501,500,200,500,0,"+ai+",85,100,1,3]"))+"]"+extras+"}";
    static void Main()
    {
        var consumer=new PatrolProtocolTests();var registry=new BattleEntityRegistry();
        registry.BeginSession(1,"world_001");
        registry.RegisterOrUpdate(new BattleEntityRegistration(1,"world_001",BattleEntityKind.Monster,"m",501,3,100,1),0);
        var repair=new SnapshotRepairService(consumer,consumer,consumer);repair.BeginSession(1,"world_001",1);
        var processor=new StateFrameProcessor(consumer,new CompactStateDecoder(),registry,consumer,consumer,repair,consumer);
        foreach(int version in new[]{0,1,2})
        {
            processor.BeginSession(1,"world_001","session",version);
            Check(processor.Process(1,Frame(1,8))==BattleSyncApplyResult.InvalidMessage,"Patrol requires v3");
            Check(processor.Process(1,Frame(1,9))==BattleSyncApplyResult.InvalidMessage,"Hover requires v3");
        }
        processor.BeginSession(1,"world_001","session",3);
        string path=",\"monsterPaths\":[[501,3,101,10000,125,500,200,500,550,500]]";
        var first=processor.Process(1,Frame(1,8,path));
        Check(first==BattleSyncApplyResult.Applied&&consumer.Last.AiState==8
            &&consumer.Last.HasPathUpdate&&consumer.Last.Path!=null,"Patrol full state/path");
        Check(processor.Process(1,Frame(2,0,",\"monsterDeltas\":[[501,3,1,4,90,2]]",""))==BattleSyncApplyResult.Applied
            &&!consumer.Last.HasAiState&&!consumer.Last.HasPosition&&consumer.Last.HasHp,"HP-only delta keeps behavior absent");
        Check(processor.Process(1,Frame(3,0,",\"monsterDeltas\":[[501,3,2,2,9,85]],\"monsterPaths\":[[501,3]]",""))==BattleSyncApplyResult.Applied
            &&consumer.Last.AiState==9&&consumer.Last.HasPathUpdate&&consumer.Last.Path==null&&consumer.Last.Y==2,"Hover delta stops path at airborne height");
        int calls=consumer.Calls;
        Check(processor.Process(1,Frame(2,8))==BattleSyncApplyResult.IgnoredStale&&consumer.Calls==calls,"Old frame cannot resume patrol");
        Check(processor.Process(1,Frame(4,10))==BattleSyncApplyResult.InvalidMessage&&consumer.Calls==calls,"Unknown state rejected atomically");
        Check(processor.Process(1,Frame(4,0,",\"monsterDeltas\":[[501,3,3,2,10,85]]",""))==BattleSyncApplyResult.InvalidMessage,"Unknown delta rejected");
        Check(processor.Process(1,Frame(4,7))==BattleSyncApplyResult.Applied&&consumer.Last.AiState==7,"Death remains code 7");
        processor.ForgetMonster("m");
        Check(processor.Process(1,Frame(5,0,",\"monsterDeltas\":[[501,3,4,2,8,85]]",""))==BattleSyncApplyResult.IgnoredStale
            &&repair.HasPendingRepair,"Missing baseline requests snapshot");
        Check(processor.Process(1,Frame(6,9).Replace("\"session\"","\"old\""))==BattleSyncApplyResult.IgnoredWrongScene,"Old scene session filtered");
        processor.EndSession(1);
        Check(!processor.HasMonsterBaseline("m")&&processor.Process(1,Frame(6,9))==BattleSyncApplyResult.IgnoredWrongScene,"Disconnect clears baseline");
        registry.BeginSession(2,"world_001");
        registry.RegisterOrUpdate(new BattleEntityRegistration(2,"world_001",BattleEntityKind.Monster,"m",501,4,200,2),0);
        processor.BeginSession(2,"world_001","session",3);
        Check(processor.Process(1,Frame(7,8))==BattleSyncApplyResult.IgnoredWrongScene,"Old connection epoch filtered");
        Check(processor.Process(2,Frame(101,9).Replace(",1,3]",",1,4]"))==BattleSyncApplyResult.Applied
            &&consumer.Last.AiState==9&&consumer.Last.Y==2,"Reconnect observes current hover without takeoff replay");
        processor.BeginSession(2,"world_001","session",2);
        Check(processor.Process(2,Frame(102,0,",\"monsterDeltas\":[[501,4,101,2,9,85]]",""))==BattleSyncApplyResult.InvalidMessage,"v2 rejects behavior delta before mutation");
        Console.WriteLine("PASS: patrol v3/full/delta/path/hover/HP-only/death/stale/repair/reconnect");
    }
}
