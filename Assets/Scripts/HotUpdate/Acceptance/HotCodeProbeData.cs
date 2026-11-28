#if UNITY_EDITOR || QIDAO_HOTUPDATE_PROBE
using UnityEngine;
namespace QiDao.GameLogic
{
    public sealed class HotCodeProbeData : ScriptableObject
    {
        public int marker = 73;
        public HotCodeNestedData nested = new HotCodeNestedData();
        public string ReadFromGameCode() => HotCodeProbe.Version + "-" + marker;
    }
    [System.Serializable]
    public sealed class HotCodeNestedData
    {
        public string text = "nested-code-data";
        public System.Collections.Generic.List<int> values = new System.Collections.Generic.List<int> { 17, 29 };
    }
}
#endif
