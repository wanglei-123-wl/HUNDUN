namespace Game.Inventory.Model.V2
{
    public static class InventoryWorldDropRules
    {
        public static bool ShouldOffer(bool startedInBag, bool hasIdentity,
            bool insideBag, bool overUi, bool handledByDrop)
        {
            return startedInBag && hasIdentity && !insideBag
                && !overUi && !handledByDrop;
        }
    }
}
