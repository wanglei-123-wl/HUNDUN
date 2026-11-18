using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace Game.Client.BattleSync
{
    /// <summary>
    /// Writes a small, correlation-friendly trace for the reliable battle
    /// message path. The logger is deliberately independent from the
    /// protocol and does not change message handling decisions.
    /// </summary>
    public static class BattleSyncDiagnosticsLogger
    {
        private static readonly object SyncRoot = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private static StreamWriter writer;
        private static bool enabled;
        private static bool initializationFailed;
        private static string logPath = string.Empty;
        private static long lastQueueMetricsAtMs = -1;
        private static DispatchEnvelope currentEnvelope;
        private static bool hasCurrentEnvelope;

        public static bool Enabled
        {
            get
            {
                lock (SyncRoot)
                {
                    return enabled && writer != null;
                }
            }
        }

        public static string LogPath
        {
            get
            {
                lock (SyncRoot)
                {
                    return logPath;
                }
            }
        }

        public static long NowMilliseconds => Clock.ElapsedMilliseconds;

        public static long CurrentDispatchReceivedAtMs
        {
            get
            {
                lock (SyncRoot)
                {
                    return hasCurrentEnvelope ? currentEnvelope.ReceivedAtMs : -1;
                }
            }
        }

        public static long CurrentDispatchSequence
        {
            get
            {
                lock (SyncRoot)
                {
                    return hasCurrentEnvelope ? currentEnvelope.Sequence : 0;
                }
            }
        }

        public static void Configure(bool shouldEnable)
        {
            lock (SyncRoot)
            {
                if (!shouldEnable)
                {
                    enabled = false;
                    CloseWriterLocked();
                    return;
                }

                enabled = true;
                if (writer != null || initializationFailed)
                {
                    return;
                }

                try
                {
                    string root = Path.Combine(
                        Application.persistentDataPath,
                        "GameClientLogs",
                        "BattleSync");
                    Directory.CreateDirectory(root);

                    string stamp = DateTime.Now.ToString(
                        "yyyyMMdd_HHmmss",
                        CultureInfo.InvariantCulture);
                    string fileName = "BattleSync_"
                        + stamp
                        + "_"
                        + Guid.NewGuid().ToString("N")
                        + ".log";
                    logPath = Path.Combine(root, fileName);

                    FileStream stream = new FileStream(
                        logPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite);
                    writer = new StreamWriter(
                        stream,
                        new UTF8Encoding(false));
                    writer.AutoFlush = true;

                    WriteLocked(
                        "logger_started",
                        "path=" + logPath,
                        "persistentDataPath=" + Application.persistentDataPath);
                }
                catch
                {
                    initializationFailed = true;
                    enabled = false;
                    CloseWriterLocked();
                }
            }
        }

        public static void Close()
        {
            lock (SyncRoot)
            {
                enabled = false;
                CloseWriterLocked();
                hasCurrentEnvelope = false;
                lastQueueMetricsAtMs = -1;
            }
        }

        public static void MarkMessageDequeued(
            long connectionEpoch,
            long sequence,
            long receivedAtMs,
            int byteCount,
            int queueDepth)
        {
            lock (SyncRoot)
            {
                if (!CanWriteLocked())
                {
                    return;
                }

                currentEnvelope = new DispatchEnvelope(
                    connectionEpoch,
                    sequence,
                    receivedAtMs,
                    byteCount,
                    queueDepth);
                hasCurrentEnvelope = true;
            }
        }

        public static void LogDispatch(
            string stage,
            string messageId,
            string type,
            long reliableMessageId,
            bool handlerFound,
            bool deferred,
            int deferredCount)
        {
            lock (SyncRoot)
            {
                if (!CanWriteLocked()
                    || (reliableMessageId <= 0
                        && stage != "parse_failed"
                        && stage != "missing_msg_id"))
                {
                    return;
                }

                WriteLocked(
                    "message_dispatch",
                    "stage=" + stage,
                    "epoch=" + (hasCurrentEnvelope
                        ? currentEnvelope.ConnectionEpoch.ToString(CultureInfo.InvariantCulture)
                        : "0"),
                    "sequence=" + (hasCurrentEnvelope
                        ? currentEnvelope.Sequence.ToString(CultureInfo.InvariantCulture)
                        : "0"),
                    "messageId=" + messageId,
                    "type=" + type,
                    "reliableMessageId=" + reliableMessageId,
                    "handlerFound=" + handlerFound,
                    "deferred=" + deferred,
                    "deferredCount=" + deferredCount,
                    "bytes=" + (hasCurrentEnvelope
                        ? currentEnvelope.ByteCount.ToString(CultureInfo.InvariantCulture)
                        : "-1"),
                    "queueDepth=" + (hasCurrentEnvelope
                        ? currentEnvelope.QueueDepth.ToString(CultureInfo.InvariantCulture)
                        : "-1"),
                    "queueDelayMs=" + (hasCurrentEnvelope
                        ? Math.Max(0, NowMilliseconds - currentEnvelope.ReceivedAtMs).ToString(CultureInfo.InvariantCulture)
                        : "-1"));
            }
        }

        public static void LogValidationFailure(
            string messageId,
            long reliableMessageId,
            string sceneId,
            string detail)
        {
            Write(
                "validation_failed",
                "messageId=" + messageId,
                "reliableMessageId=" + reliableMessageId,
                "sceneId=" + sceneId,
                "wireSequence=" + CurrentDispatchSequence,
                "detail=" + detail);
        }

        public static void LogReliableProcess(
            ReliableMessageContext context,
            ReliableMessageProcessResult result,
            string phase,
            long processedAtMs)
        {
            if (result == null)
            {
                return;
            }

            long wireReceivedAtMs = CurrentDispatchReceivedAtMs;
            long wireSequence = CurrentDispatchSequence;
            long queueDelayMs = wireReceivedAtMs >= 0
                ? Math.Max(0, processedAtMs - wireReceivedAtMs)
                : -1;

            Write(
                "reliable_processed",
                "epoch=" + context.ConnectionEpoch,
                "sceneId=" + context.SceneId,
                "messageId=" + context.MessageId,
                "reliableMessageId=" + context.ReliableMessageId,
                "identity=" + context.MessageIdentity,
                "serverTime=" + context.ServerTime,
                "serverTick=" + context.ServerTick,
                "phase=" + phase,
                "status=" + result.Status,
                "businessInvoked=" + result.BusinessInvoked,
                "businessResult=" + result.BusinessResult,
                "ackStatus=" + result.AckStatus,
                "requiresSnapshotRepair=" + result.RequiresSnapshotRepair,
                "wireSequence=" + wireSequence,
                "queueDelayMs=" + queueDelayMs,
                "detail=" + result.Detail);
        }

        public static void LogAckRequest(
            ReliableAckRequest request,
            ReliableAckRequestResult result)
        {
            if (result == null)
            {
                return;
            }

            Write(
                "ack_requested",
                "epoch=" + request.ConnectionEpoch,
                "sceneId=" + request.SceneId,
                "sourceMessageId=" + request.SourceMessageId,
                "reliableMessageId=" + request.ReliableMessageId,
                "identity=" + request.MessageIdentity,
                "duplicateDelivery=" + request.IsDuplicateDelivery,
                "status=" + result.Status,
                "wasSent=" + result.WasSent,
                "pending=" + result.IsPending,
                "detail=" + result.Detail);
        }

        public static void LogAckAttempt(
            PendingReliableAck pending,
            bool succeeded,
            string stage,
            string detail)
        {
            if (pending == null)
            {
                return;
            }

            Write(
                "ack_attempt",
                "epoch=" + pending.ConnectionEpoch,
                "sceneId=" + pending.SceneId,
                "sourceMessageId=" + pending.SourceMessageId,
                "reliableMessageId=" + pending.ReliableMessageId,
                "identity=" + pending.MessageIdentity,
                "stage=" + stage,
                "attempt=" + pending.AttemptCount,
                "succeeded=" + succeeded,
                "firstQueuedAtMs=" + pending.FirstQueuedAtMs,
                "lastRequestedAtMs=" + pending.LastRequestedAtMs,
                "nextAttemptAtMs=" + pending.NextAttemptAtMs,
                "detail=" + detail);
        }

        public static void LogConnection(
            string stage,
            long connectionEpoch,
            string host,
            int port,
            string reason)
        {
            Write(
                "connection",
                "stage=" + stage,
                "epoch=" + connectionEpoch,
                "host=" + host,
                "port=" + port,
                "reason=" + reason);
        }

        public static void LogSendFailure(
            long connectionEpoch,
            int byteCount,
            string reason)
        {
            Write(
                "send_failed",
                "epoch=" + connectionEpoch,
                "bytes=" + byteCount,
                "reason=" + reason);
        }

        public static void LogDispatchException(
            string messageId,
            long reliableMessageId,
            string detail)
        {
            Write(
                "dispatch_exception",
                "messageId=" + messageId,
                "reliableMessageId=" + reliableMessageId,
                "detail=" + detail);
        }

        public static void LogQueueMetrics(
            long connectionEpoch,
            int queueDepth,
            int processed,
            long updateElapsedMs,
            int maxActionsPerFrame,
            float maxDispatchTimeMs,
            bool budgetReached)
        {
            long nowMs = NowMilliseconds;
            lock (SyncRoot)
            {
                if (!CanWriteLocked()
                    || (!budgetReached && queueDepth <= 0 && processed <= 0)
                    || nowMs - lastQueueMetricsAtMs < 1000)
                {
                    return;
                }

                lastQueueMetricsAtMs = nowMs;
                WriteLocked(
                    "queue_metrics",
                    "epoch=" + connectionEpoch,
                    "queueDepth=" + queueDepth,
                    "processed=" + processed,
                    "updateElapsedMs=" + updateElapsedMs,
                    "maxActionsPerFrame=" + maxActionsPerFrame,
                    "maxDispatchTimeMs=" + maxDispatchTimeMs.ToString("0.###", CultureInfo.InvariantCulture),
                    "budgetReached=" + budgetReached);
            }
        }

        private static void Write(string eventName, params string[] fields)
        {
            lock (SyncRoot)
            {
                if (!CanWriteLocked())
                {
                    return;
                }

                WriteLocked(eventName, fields);
            }
        }

        private static bool CanWriteLocked()
        {
            return enabled && writer != null;
        }

        private static void WriteLocked(string eventName, params string[] fields)
        {
            StringBuilder line = new StringBuilder();
            line.Append(DateTime.Now.ToString(
                "yyyy-MM-ddTHH:mm:ss.fff",
                CultureInfo.InvariantCulture));
            line.Append(" event=");
            line.Append(Sanitize(eventName, 64));
            line.Append(" elapsedMs=");
            line.Append(Clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));

            if (fields != null)
            {
                for (int index = 0; index < fields.Length; index++)
                {
                    if (string.IsNullOrWhiteSpace(fields[index]))
                    {
                        continue;
                    }

                    line.Append(' ');
                    line.Append(Sanitize(fields[index], 512));
                }
            }

            writer.WriteLine(line.ToString());
        }

        private static string Sanitize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "<empty>";
            }

            string sanitized = value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('|', '/');
            return sanitized.Length <= maxLength
                ? sanitized
                : sanitized.Substring(0, maxLength);
        }

        private static void CloseWriterLocked()
        {
            if (writer == null)
            {
                return;
            }

            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch
            {
            }

            writer = null;
        }

        private readonly struct DispatchEnvelope
        {
            public readonly long ConnectionEpoch;
            public readonly long Sequence;
            public readonly long ReceivedAtMs;
            public readonly int ByteCount;
            public readonly int QueueDepth;

            public DispatchEnvelope(
                long connectionEpoch,
                long sequence,
                long receivedAtMs,
                int byteCount,
                int queueDepth)
            {
                ConnectionEpoch = connectionEpoch;
                Sequence = sequence;
                ReceivedAtMs = receivedAtMs;
                ByteCount = byteCount;
                QueueDepth = queueDepth;
            }
        }
    }
}
