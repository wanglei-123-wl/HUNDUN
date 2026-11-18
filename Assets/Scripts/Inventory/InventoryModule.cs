using System;
using Game.Items;

namespace Game.Inventory
{
    public sealed class InventoryModuleDependencies
    {
        public NetworkManager NetworkManager;
        public NetMessageDispatcher MessageDispatcher;
        public InventoryPanelView PanelView;
        public ItemDefinitionTable ItemDefinitions;
        public Func<float> GetLocalTime;
    }

    public sealed class InventoryModule : IDisposable
    {
        private bool initialized;

        public InventoryStateStore StateStore { get; }
        public InventoryFilterService FilterService { get; }
        public InventoryServerAdapter ServerAdapter { get; }
        public InventoryController Controller { get; }

        public bool IsOpen => Controller.IsOpen;

        public InventoryModule(InventoryModuleDependencies dependencies)
        {
            if (dependencies == null)
            {
                throw new ArgumentNullException(nameof(dependencies));
            }

            if (dependencies.NetworkManager == null)
            {
                throw new ArgumentException(
                    "Inventory NetworkManager is missing.",
                    nameof(dependencies));
            }

            if (dependencies.MessageDispatcher == null)
            {
                throw new ArgumentException(
                    "Inventory NetMessageDispatcher is missing.",
                    nameof(dependencies));
            }

            if (dependencies.PanelView == null)
            {
                throw new ArgumentException(
                    "InventoryPanelView is missing.",
                    nameof(dependencies));
            }

            if (dependencies.ItemDefinitions == null)
            {
                throw new ArgumentException(
                    "ItemDefinitionTable is missing.",
                    nameof(dependencies));
            }

            StateStore = new InventoryStateStore();
            FilterService = new InventoryFilterService();
            ServerAdapter = new InventoryServerAdapter(
                dependencies.NetworkManager,
                dependencies.MessageDispatcher,
                dependencies.GetLocalTime);
            Controller = new InventoryController(
                StateStore,
                FilterService,
                ServerAdapter,
                dependencies.PanelView,
                dependencies.ItemDefinitions);
        }

        public void Initialize(bool registerSnapshot = true)
        {
            if (initialized)
            {
                return;
            }

            ServerAdapter.Initialize(registerSnapshot);
            Controller.Initialize();
            initialized = true;
        }

        public void Open()
        {
            EnsureInitialized();
            Controller.Open();
        }

        public void Close()
        {
            if (!initialized)
            {
                return;
            }

            Controller.Close();
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void RequestSnapshot()
        {
            EnsureInitialized();
            Controller.RequestSnapshot();
        }

        public void ResetConnectionState()
        {
            ServerAdapter.ResetConnectionState();
            Controller.ResetConnectionState();
        }

        public void Dispose()
        {
            Controller.Dispose();
            ServerAdapter.Dispose();
            initialized = false;
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }
        }
    }
}
