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

        public static void LogMovementEventSent(long connectionEpoch, MovementEventRequest message)
        {
            if (!Enabled || message?.info == null) return;
            MovementEventInfo info = message.info;
            WriteMovementRecord("movement_event_sent",
                "connectionEpoch=" + connectionEpoch,
                "sceneSessionId=" + message.sceneSessionId,
                "movementEpoch=" + message.movementEpoch,
                "sequence=" + message.sequence,
                "kind=" + message.@event,
                "clientTimeMs=" + info.clientTimeMs,
                "position=" + FormatVector(info.Position),
                "velocity=" + FormatVector(info.Velocity),
                "yaw=" + info.yaw.ToString("R", CultureInfo.InvariantCulture),
                "flags=" + (int)info.flags,
                "fallTimeMs=" + info.fallTimeMs);
        }

        public static void LogMovementControlReply(long connectionEpoch, string sceneId, string mapId,
            MovementControlEvaluation result, bool correction)
        {
            if (!Enabled || result == null) return;
            WriteMovementRecord(correction ? "movement_correction_received" : "movement_control_result_received",
                "connectionEpoch=" + connectionEpoch,
                "sceneId=" + sceneId,
                "mapId=" + mapId,
                "errorCode=" + result.errorCode,
                "success=" + result.success,
                "needsControlAck=" + result.needsControlAck,
                "acceptedSequence=" + result.acceptedSequence,
                "revision=" + result.revision,
                "movementEpoch=" + (result.control?.epoch ?? 0),
                "reason=" + result.control?.reason,
                "mode=" + result.control?.mode,
                "serverPosition=" + (result.control == null ? "unavailable" : FormatVector(result.control.Position)));
        }

        public static void LogMovementControl(long connectionEpoch, string sceneId, string mapId,
            MovementEventControl control, Vector3 localBefore, Vector3 localAfter,
            Vector3 localVelocityBefore, CharacterController controller, bool accepted)
        {
            if (!Enabled || control == null) return;
            WriteMovementRecord("movement_control_received",
                "connectionEpoch=" + connectionEpoch,
                "sceneId=" + sceneId,
                "mapId=" + mapId,
                "sceneSessionId=" + control.sceneSessionId,
                "movementEpoch=" + control.epoch,
                "nextSequence=" + control.nextSequence,
                "serverTime=" + control.serverTime,
                "reason=" + control.reason,
                "mode=" + control.mode,
                "accepted=" + accepted,
                "localBefore=" + FormatVector(localBefore),
                "localAfter=" + FormatVector(localAfter),
                "serverPosition=" + FormatVector(control.Position),
                "positionChange=" + Vector3.Distance(localBefore, localAfter).ToString("R", CultureInfo.InvariantCulture),
                "localVelocityBefore=" + FormatVector(localVelocityBefore),
                "serverRadius=" + control.radius.ToString("R", CultureInfo.InvariantCulture),
                "serverHeight=" + control.height.ToString("R", CultureInfo.InvariantCulture),
                "serverWalkSpeed=" + control.walkSpeed.ToString("R", CultureInfo.InvariantCulture),
                "serverRunSpeed=" + control.runSpeed.ToString("R", CultureInfo.InvariantCulture),
                "serverGravity=" + control.gravity.ToString("R", CultureInfo.InvariantCulture),
                "grounded=" + control.grounded,
                "controllerRadius=" + FormatControllerValue(controller, controller == null ? 0f : controller.radius),
                "controllerHeight=" + FormatControllerValue(controller, controller == null ? 0f : controller.height),
                "controllerSkin=" + FormatControllerValue(controller, controller == null ? 0f : controller.skinWidth),
                "controllerStep=" + FormatControllerValue(controller, controller == null ? 0f : controller.stepOffset),
                "controllerSlope=" + FormatControllerValue(controller, controller == null ? 0f : controller.slopeLimit));
        }

        public static void LogLocalTraversalBlocked(long connectionEpoch, long movementEpoch,
            string sceneId, string mapId, string packageFingerprint, Vector3 from, Vector3 requested, Vector3 accepted,
            float radius, float height, bool grounded)
        {
            if (!Enabled) return;
            WriteMovementRecord("local_traversal_blocked",
                "connectionEpoch=" + connectionEpoch,
                "movementEpoch=" + movementEpoch,
                "sceneId=" + sceneId,
                "mapId=" + mapId,
                "packageSha256=" + packageFingerprint,
                "from=" + FormatVector(from),
                "requested=" + FormatVector(requested),
                "accepted=" + FormatVector(accepted),
                "radius=" + radius.ToString("R", CultureInfo.InvariantCulture),
                "height=" + height.ToString("R", CultureInfo.InvariantCulture),
                "grounded=" + grounded);
        }

        private static string FormatVector(Vector3 value) => string.Format(CultureInfo.InvariantCulture,
            "{0:R},{1:R},{2:R}", value.x, value.y, value.z);

        private static string FormatControllerValue(CharacterController controller, float value) =>
            controller == null ? "unavailable" : value.ToString("R", CultureInfo.InvariantCulture);

        private static void WriteMovementRecord(string eventName, params string[] fields)
        {
            try
            {
                Write(eventName, fields);
            }
            catch (IOException) { Close(); }
            catch (UnauthorizedAccessException) { Close(); }
            catch (ObjectDisposedException) { Close(); }
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
