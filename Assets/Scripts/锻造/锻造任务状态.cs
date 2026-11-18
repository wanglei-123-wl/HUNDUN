using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Game.Forging.Protocol
{
    public enum 锻造任务接收结果
    {
        无快照,
        无效,
        过期,
        未变化,
        已更新
    }

    public sealed class 锻造任务状态
    {
        public 锻造快照 Snapshot { get; private set; }
        public long LastServerTime { get; private set; }

        public void Reset()
        {
            Snapshot = null;
            LastServerTime = 0;
        }

        public 锻造任务接收结果 Apply(string json, 锻造快照 value,
            long characterDbId, long serverTime)
        {
            if (value == null)
                return 锻造任务接收结果.无快照;
            if (value.characterDbId != characterDbId || characterDbId <= 0
                || value.revision < 0 || serverTime <= 0)
                return 锻造任务接收结果.无效;
            try
            {
                JObject root = JObject.Parse(json);
                JObject snapshot = root["snapshot"] as JObject;
                if (snapshot == null
                    || !snapshot.TryGetValue("currentTask", out JToken taskToken))
                    return 锻造任务接收结果.无效;
                if (snapshot.TryGetValue("learnedRecipeIds",
                    out JToken learnedToken))
                {
                    if (!(learnedToken is JArray learned)
                        || value.learnedRecipeIds == null
                        || learned.Count != value.learnedRecipeIds.Length)
                        return 锻造任务接收结果.无效;
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JToken entry in learned)
                        if (entry.Type != JTokenType.String
                            || string.IsNullOrWhiteSpace(entry.Value<string>())
                            || !ids.Add(entry.Value<string>()))
                            return 锻造任务接收结果.无效;
                }
                else
                    value.learnedRecipeIds = Snapshot?.learnedRecipeIds;
                if (taskToken.Type != JTokenType.Null)
                {
                    if (!(taskToken is JObject taskJson)
                        || !taskJson.TryGetValue("succeeded", out JToken succeeded)
                        || !IsValidTask(value.currentTask, succeeded))
                        return 锻造任务接收结果.无效;
                }
                else if (value.currentTask != null)
                    return 锻造任务接收结果.无效;
            }
            catch (Exception exception) when (exception is Newtonsoft.Json.JsonException
                || exception is ArgumentException)
            {
                return 锻造任务接收结果.无效;
            }

            if (Snapshot != null && value.revision < Snapshot.revision)
                return 锻造任务接收结果.过期;
            if (Snapshot != null && value.revision == Snapshot.revision)
            {
                if (serverTime < LastServerTime)
                    return 锻造任务接收结果.过期;
                if ((Snapshot.currentTask?.taskId ?? string.Empty)
                    != (value.currentTask?.taskId ?? string.Empty)
                    || (Snapshot.currentTask?.status ?? string.Empty)
                    != (value.currentTask?.status ?? string.Empty)
                    || Snapshot.currentTask?.succeeded
                    != value.currentTask?.succeeded)
                    return 锻造任务接收结果.无效;
                if (!SameLearned(Snapshot.learnedRecipeIds,
                    value.learnedRecipeIds))
                {
                    if (serverTime == LastServerTime)
                        return 锻造任务接收结果.无效;
                    Snapshot = value;
                    LastServerTime = serverTime;
                    return 锻造任务接收结果.已更新;
                }
                LastServerTime = serverTime;
                return 锻造任务接收结果.未变化;
            }
            Snapshot = value;
            LastServerTime = serverTime;
            return 锻造任务接收结果.已更新;
        }

        private static bool SameLearned(string[] left, string[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
                if (!string.Equals(left[index], right[index],
                    StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static bool IsValidTask(锻造任务 task, JToken succeeded)
        {
            if (task == null || !Guid.TryParseExact(task.taskId, "D", out _)
                || string.IsNullOrWhiteSpace(task.recipeId)
                || task.startedAtUnixMilliseconds <= 0
                || task.endsAtUnixMilliseconds < task.startedAtUnixMilliseconds)
                return false;
            if (task.status == "active")
                return succeeded.Type == JTokenType.Null
                    && task.succeeded == null && task.outputCount == 0;
            if (task.status != "ready"
                || (succeeded.Type != JTokenType.Boolean)
                || task.succeeded == null)
                return false;
            return task.succeeded.Value
                ? task.outputCount > 0
                    && !string.IsNullOrWhiteSpace(task.outputItemId)
                : task.outputCount == 0;
        }
    }
}
