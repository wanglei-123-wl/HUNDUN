using System.Globalization;
using UnityEngine;

namespace Game.Cultivation.Presentation
{
    internal readonly struct CultivationProgressDisplay
    {
        public bool IsValid { get; }
        public string Current { get; }
        public string Required { get; }
        public string Progress { get; }
        public string Total { get; }
        public float FillAmount { get; }

        private CultivationProgressDisplay(bool isValid, string current,
            string required, string total, float fillAmount)
        {
            IsValid = isValid;
            Current = current;
            Required = required;
            Progress = current + "/" + required;
            Total = total;
            FillAmount = fillAmount;
        }

        public static CultivationProgressDisplay FromSnapshot(
            ProgressionSnapshotResult snapshot, long characterDbId,
            bool isWorldReady)
        {
            if (!isWorldReady || snapshot == null || !snapshot.success
                || snapshot.characterDbId != characterDbId
                || snapshot.jingjieExp < 0
                || snapshot.requiredJingjieExp < 0)
                return new CultivationProgressDisplay(false, "--", "--", "--", 0f);

            string current = snapshot.jingjieExp.ToString(
                CultureInfo.InvariantCulture);
            string required = snapshot.hasNextJingjie
                ? snapshot.requiredJingjieExp.ToString(CultureInfo.InvariantCulture)
                : "--";
            string total = snapshot.totalJingjieExp >= 0
                ? snapshot.totalJingjieExp.ToString(CultureInfo.InvariantCulture)
                : "--";
            float fill = !snapshot.hasNextJingjie ? 0f
                : snapshot.requiredJingjieExp == 0 ? 1f
                : Mathf.Clamp01((float)((double)snapshot.jingjieExp
                    / snapshot.requiredJingjieExp));
            return new CultivationProgressDisplay(true, current, required,
                total, fill);
        }
    }
}
