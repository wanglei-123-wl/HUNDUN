using System;
using System.Collections.Generic;

namespace Game.Inventory.Model.V2
{
    public sealed class InventoryBagLayout
    {
        private readonly string[] slots;
        private readonly IReadOnlyList<string> visibleSlots;

        public long CharacterDbId { get; }
        public int Capacity => slots.Length;
        public IReadOnlyList<string> Slots => visibleSlots;

        private InventoryBagLayout(long characterDbId, string[] slots)
        {
            CharacterDbId = characterDbId;
            this.slots = slots;
            visibleSlots = Array.AsReadOnly(slots);
        }

        public static InventoryBagLayout Reconcile(
            InventoryOwnershipSnapshot snapshot, IReadOnlyList<string> previousSlots)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var remaining = new HashSet<string>(StringComparer.Ordinal);
            var authoritativeOrder = new List<string>();
            foreach (InventoryStackState stack in snapshot.Stacks)
                AddIdentity(stack.Identity);
            foreach (InventoryEquipmentState equipment in snapshot.Equipment)
                if (equipment.SlotCode == 0) AddIdentity(equipment.Identity);

            var slots = new string[snapshot.Capacity];
            if (previousSlots != null)
            {
                for (int index = 0; index < slots.Length && index < previousSlots.Count; index++)
                {
                    string identity = previousSlots[index];
                    if (identity != null && remaining.Remove(identity))
                        slots[index] = identity;
                }
            }

            int emptyIndex = 0;
            foreach (string identity in authoritativeOrder)
            {
                if (!remaining.Contains(identity)) continue;
                while (emptyIndex < slots.Length && slots[emptyIndex] != null) emptyIndex++;
                if (emptyIndex == slots.Length)
                    throw new InvalidOperationException("Inventory ownership exceeds bag capacity.");
                slots[emptyIndex++] = identity;
            }

            return new InventoryBagLayout(snapshot.CharacterDbId, slots);

            void AddIdentity(string identity)
            {
                if (!remaining.Add(identity))
                    throw new InvalidOperationException("Duplicate bag identity.");
                authoritativeOrder.Add(identity);
            }
        }

        public string GetIdentity(int index)
        {
            return index >= 0 && index < slots.Length ? slots[index] : null;
        }

        public bool TryMove(int sourceIndex, int targetIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= slots.Length
                || targetIndex < 0 || targetIndex >= slots.Length
                || sourceIndex == targetIndex || slots[sourceIndex] == null)
                return false;

            string target = slots[targetIndex];
            slots[targetIndex] = slots[sourceIndex];
            slots[sourceIndex] = target;
            return true;
        }
    }
}
