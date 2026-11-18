using System;
using System.Collections.Generic;
using Game.Inventory.Model.V2;

namespace Game.Inventory.Integration.V2
{
    public sealed class InventoryBagLocalController
    {
        private readonly IInventoryBagLayoutStorage storage;

        public InventoryBagLayout Layout { get; private set; }
        public event Action<InventoryBagLayout> LayoutChanged;

        public InventoryBagLocalController(IInventoryBagLayoutStorage storage)
        {
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public void ApplySnapshot(InventoryOwnershipSnapshot snapshot)
        {
            ApplyPreparedLayout(PrepareSnapshot(snapshot), true);
        }

        public InventoryBagLayout PrepareSnapshot(InventoryOwnershipSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            IReadOnlyList<string> previous = Layout != null
                && Layout.CharacterDbId == snapshot.CharacterDbId
                    ? Layout.Slots
                    : storage.Load(snapshot.CharacterDbId);
            return InventoryBagLayout.Reconcile(snapshot, previous);
        }

        public void ApplyPreparedLayout(InventoryBagLayout next, bool persist)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            Layout = next;
            LayoutChanged?.Invoke(next);
            if (persist) PersistLayout();
        }

        public void RestoreLayout(InventoryBagLayout previous)
        {
            Layout = previous;
            if (previous != null) LayoutChanged?.Invoke(previous);
        }

        public void PersistLayout()
        {
            if (Layout != null) storage.Save(Layout.CharacterDbId, Layout.Slots);
        }

        public bool TryDrop(int sourceIndex, int targetIndex, string sourceIdentity)
        {
            if (Layout == null || string.IsNullOrEmpty(sourceIdentity)
                || !string.Equals(Layout.GetIdentity(sourceIndex),
                    sourceIdentity, StringComparison.Ordinal)
                || !Layout.TryMove(sourceIndex, targetIndex))
                return false;
            storage.Save(Layout.CharacterDbId, Layout.Slots);
            LayoutChanged?.Invoke(Layout);
            return true;
        }

        public void Reset()
        {
            Layout = null;
        }
    }
}
