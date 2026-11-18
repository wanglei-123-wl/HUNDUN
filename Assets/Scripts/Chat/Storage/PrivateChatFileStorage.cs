using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Game.Chat.Model;
using Game.Chat.Protocol;
using UnityEngine;

namespace Game.Chat.Storage
{
    public sealed class PrivateChatFileStorage : IPrivateChatMessageStorage
    {
        [Serializable]
        private sealed class SavedMessage
        {
            public string scope;
            public long characterDbId;
            public long partnerCharacterDbId;
            public WorldChatMessage message;
        }

        private readonly string directory;
        private readonly string scope;
        private readonly long characterDbId;

        public PrivateChatFileStorage(string root, string host, int port,
            string accountId, long characterDbId)
        {
            if (string.IsNullOrWhiteSpace(root)
                || string.IsNullOrWhiteSpace(host) || port <= 0
                || string.IsNullOrWhiteSpace(accountId) || characterDbId <= 0)
                throw new ArgumentException("Private chat storage identity is incomplete.");

            this.characterDbId = characterDbId;
            string identity = host.Trim().ToLowerInvariant() + "\n"
                + port.ToString(CultureInfo.InvariantCulture) + "\n"
                + accountId.Length.ToString(CultureInfo.InvariantCulture) + ":"
                + accountId + "\n"
                + characterDbId.ToString(CultureInfo.InvariantCulture);
            scope = Hash(identity);
            directory = Path.Combine(root, "PrivateChat", "v1", scope);
        }

        public bool TryLoad(out List<WorldChatMessage> messages)
        {
            messages = new List<WorldChatMessage>();
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory,
                    Guid.NewGuid().ToString("N") + ".probe");
                using (var stream = new FileStream(probe, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                    stream.Flush(true);

                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (string partnerPath in Directory.GetDirectories(directory))
                {
                    if (!long.TryParse(Path.GetFileName(partnerPath),
                            NumberStyles.None, CultureInfo.InvariantCulture,
                            out long partnerId) || partnerId <= 0)
                    {
                        messages.Clear();
                        return false;
                    }

                    foreach (string path in Directory.GetFiles(partnerPath, "*.json"))
                    {
                        if (!TryRead(path, partnerId, out WorldChatMessage message)
                            || !ids.Add(message.messageId))
                        {
                            messages.Clear();
                            return false;
                        }
                        messages.Add(message);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Chat] Private chat load failed: " + ex.GetType().Name);
                messages.Clear();
                return false;
            }
        }

        public bool TryWrite(long partnerCharacterDbId, WorldChatMessage message)
        {
            if (!Valid(message, partnerCharacterDbId)) return false;

            string partnerPath = Path.Combine(directory,
                partnerCharacterDbId.ToString(CultureInfo.InvariantCulture));
            string path = Path.Combine(partnerPath, Hash(message.messageId) + ".json");
            string temporaryPath = path + ".tmp";
            try
            {
                Directory.CreateDirectory(partnerPath);
                if (File.Exists(path))
                    return TryRead(path, partnerCharacterDbId,
                        out WorldChatMessage existing)
                        && SameMessage(existing, message);

                string json = JsonUtility.ToJson(new SavedMessage
                {
                    scope = scope,
                    characterDbId = characterDbId,
                    partnerCharacterDbId = partnerCharacterDbId,
                    message = message
                });
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (var stream = new FileStream(temporaryPath, FileMode.Create,
                    FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporaryPath, path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Chat] Private chat write failed: " + ex.GetType().Name);
                return File.Exists(path)
                    && TryRead(path, partnerCharacterDbId,
                        out WorldChatMessage existing)
                    && SameMessage(existing, message);
            }
        }

        private bool TryRead(string path, long partnerId,
            out WorldChatMessage message)
        {
            message = null;
            try
            {
                SavedMessage saved = JsonUtility.FromJson<SavedMessage>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (saved == null || saved.scope != scope
                    || saved.characterDbId != characterDbId
                    || saved.partnerCharacterDbId != partnerId
                    || !Valid(saved.message, partnerId)
                    || Path.GetFileName(path) != Hash(saved.message.messageId) + ".json")
                    return false;

                message = saved.message;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool Valid(WorldChatMessage message, long partnerId)
        {
            return partnerId > 0 && message != null
                && message.msgId == "chat_message"
                && message.type == "chat_message"
                && message.channel == "private"
                && !string.IsNullOrWhiteSpace(message.messageId)
                && message.senderCharacterDbId > 0
                && message.targetCharacterDbId > 0
                && ((message.senderCharacterDbId == characterDbId
                    && message.targetCharacterDbId == partnerId)
                    || (message.targetCharacterDbId == characterDbId
                        && message.senderCharacterDbId == partnerId));
        }

        private static bool SameMessage(WorldChatMessage first,
            WorldChatMessage second)
        {
            return first.messageId == second.messageId
                && first.senderCharacterDbId == second.senderCharacterDbId
                && first.targetCharacterDbId == second.targetCharacterDbId
                && first.content == second.content
                && first.serverTime == second.serverTime;
        }

        private static string Hash(string value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(value));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                    result.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }
}
