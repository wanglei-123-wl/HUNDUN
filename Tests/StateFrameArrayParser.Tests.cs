using System;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;

internal static class StateFrameArrayParserTests
{
    private const string Player = "[101,19820,3000,9510,16565,10,0,0,1,0,15,729,5]";
    private const string Monster = "[501,21050,2967,7600,32768,3,85,80,3,3]";
    private const string Sample = "{\"msgId\":\"state_frame\",\"type\":\"state_frame\","
        + "\"sceneId\":\"world_001\",\"serverTick\":12350,\"serverTime\":1784713000100,"
        + "\"frameSequence\":9001,\"players\":[" + Player + "],\"monsters\":[" + Monster + "]}";

    private static void Main()
    {
        Check(StateFrameArrayParser.TryParse(Sample, out StateFrameMessageDto frame,
            out StateFrameParseError error), "Sample rejected: " + error.FieldPath + " " + error.Reason);
        Check(frame.players.Length == 1 && frame.monsters.Length == 1
            && frame.frameSequence == 9001 && frame.serverTime == 1784713000100,
            "Sample header or lists differ.");
        Check(frame.players[0].netId == 101 && frame.players[0].yaw == 16565
            && frame.players[0].vx == 10 && frame.players[0].flags == 15
            && frame.players[0].inputSeq == 729 && frame.players[0].visibilityRevision == 5,
            "Player indexes differ.");
        Check(frame.monsters[0].netId == 501 && frame.monsters[0].y == 2967
            && frame.monsters[0].aiState == 3 && frame.monsters[0].flags == 85
            && frame.monsters[0].hp == 80 && frame.monsters[0].combatRevision == 3,
            "Monster indexes differ.");
        CompactStateDecoder decoder = new CompactStateDecoder();
        Check(decoder.TryDecodePlayer(frame.players[0], out DecodedPlayerState player, out _)
            && Math.Abs(player.X - 198.20f) < 0.001f && Math.Abs(player.Y - 30f) < 0.001f
            && Math.Abs(player.VelocityX - 0.10f) < 0.001f, "Player quantization changed.");
        Check(decoder.TryDecodeMonster(frame.monsters[0], out DecodedMonsterState monster, out _)
            && Math.Abs(monster.Y - 29.67f) < 0.001f && monster.HasHp
            && monster.HasCombatRevision, "Monster quantization or flags changed.");

        Accept(Sample.Replace("\"players\":[" + Player + "]", "\"players\":[]"));
        Accept(Sample.Replace("\"monsters\":[" + Monster + "]", "\"monsters\":[]"));
        Accept(Sample.Replace("\"players\":[" + Player + "]", "\"players\":[]")
            .Replace("\"monsters\":[" + Monster + "]", "\"monsters\":[]"));
        string stoppedPlayer = Player.Replace(",10,0,0,1,0,15,", ",0,0,0,0,0,15,");
        Check(StateFrameArrayParser.TryParse(Sample.Replace(Player, stoppedPlayer),
            out frame, out error) && frame.players[0].vx == 0
            && frame.players[0].move == 0 && frame.players[0].flags == 15,
            "A stopped player's zero values were not retained.");

        const string large = "9007199254740993";
        string extremePlayer = "[2147483647,-2147483648,2147483647,-2147483648,"
            + "65535,-32768,32767,0,255,0,65535," + large + ",9223372036854775807]";
        string extremeMonster = "[2147483647,-2147483648,2147483647,-2147483648,"
            + "65535,7,85,2147483647," + large + ",9223372036854775807]";
        Check(StateFrameArrayParser.TryParse(
            Sample.Replace(Player, extremePlayer).Replace(Monster, extremeMonster)
                .Replace("12350", large).Replace("9001", "9223372036854775807"),
            out frame, out error) && frame.serverTick == 9007199254740993L
            && frame.frameSequence == long.MaxValue
            && frame.players[0].inputSeq == 9007199254740993L
            && frame.players[0].visibilityRevision == long.MaxValue
            && frame.monsters[0].combatRevision == 9007199254740993L,
            "Large integers lost precision or boundaries were rejected: " + error.Reason);

        Reject(Sample.Replace("\"players\":[" + Player + "],", ""), "players");
        Reject(Sample.Replace(",\"monsters\":[" + Monster + "]", ""), "monsters");
        Reject(Sample.Replace("\"players\":[" + Player + "]", "\"players\":null"), "players");
        Reject(Sample.Replace("\"monsters\":[" + Monster + "]", "\"monsters\":null"), "monsters");
        Reject(Sample.Replace(Player, "{}"), "players[0]");
        Reject(Sample.Replace(Player, Player.TrimEnd(']') + ",0]"), "players[0]");
        Reject(Sample.Replace(Player, Player.Substring(0, Player.LastIndexOf(',')) + "]"), "players[0]");
        Reject(Sample.Replace(Monster, "{}"), "monsters[0]");
        Reject(Sample.Replace(Monster, Monster.TrimEnd(']') + ",0]"), "monsters[0]");
        Reject(Sample.Replace(Monster, Monster.Substring(0, Monster.LastIndexOf(',')) + "]"), "monsters[0]");
        Reject(Sample.Replace("\"monsters\":[" + Monster + "]", "\"monsters\":[" + Monster
            + ",[1,2]]"), "monsters[1]");
        Reject(Sample.Replace(Player, Player.Replace(",729,", ",\"729\",")), "players[0][11]");
        Reject(Sample.Replace(Player, Player.Replace(",729,", ",729.0,")), "players[0][11]");
        Reject(Sample.Replace(Player, Player.Replace(",729,", ",true,")), "players[0][11]");
        Reject(Sample.Replace(Player, Player.Replace(",10,0,0,", ",32768,0,0,")), "players[0][5]");
        Reject(Sample.Replace(Player, Player.Replace(",19820,3000,", ",2147483648,3000,")),
            "players[0][1]");
        Reject(Sample.Replace(Player, Player.Replace(",15,729,", ",65536,729,")), "players[0][10]");
        Reject(Sample.Replace(Player, Player.Replace("[101,", "[0,")), "players[0][0]");
        Reject(Sample.Replace(Player, Player.Replace(",729,5]", ",-1,5]")), "players[0][11]");
        Reject(Sample.Replace(Player, Player.Replace(",729,5]", ",9223372036854775808,5]")),
            "players[0][11]");
        Reject(Sample.Replace(Monster, Monster.Replace(",3,85,", ",256,85,")), "monsters[0][5]");
        Reject(Sample.Replace(Monster, Monster.Replace(",3,85,", ",8,85,")), "monsters[0][5]");
        Reject(Sample.Replace(Monster, Monster.Replace(",80,3,3]", ",-1,3,3]")),
            "monsters[0][7]");
        Reject(Sample.Replace("\"serverTime\":1784713000100",
            "\"serverTime\":9007199254740992.0"), "serverTime");
        Reject(Sample.Replace("\"serverTick\":12350", "\"serverTick\":0"), "serverTick");
        Reject(Sample.Replace("\"type\":\"state_frame\"", "\"type\":false"), "type");
        Reject(Sample.Replace("\"msgId\":\"state_frame\"", "\"msgId\":\"player_state_frame\""), "msgId");
        Reject(Sample.Replace("\"frameSequence\":9001", "\"frameSequence\":9223372036854775808"),
            "frameSequence");
        Reject(Sample.Replace("\"players\":[" + Player + "]",
            "\"players\":[" + Player + "],\"players\":[]"), "$");
        Reject(Sample + "{}", "$");

        Console.WriteLine("State frame array parser tests passed.");
    }

    private static void Accept(string json)
    {
        Check(StateFrameArrayParser.TryParse(json, out _, out StateFrameParseError error),
            "Valid frame rejected: " + error.FieldPath + " " + error.Reason);
    }

    private static void Reject(string json, string path)
    {
        Check(!StateFrameArrayParser.TryParse(json, out StateFrameMessageDto frame,
            out StateFrameParseError error) && frame == null && error.FieldPath == path,
            "Invalid frame accepted or wrong error path: " + path + " / " + error.FieldPath);
    }

    private static void Check(bool valid, string reason)
    {
        if (!valid) throw new Exception(reason);
    }
}
