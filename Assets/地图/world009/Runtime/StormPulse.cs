using UnityEngine;

namespace World009 {
    public sealed class StormPulse:MonoBehaviour {
        public MeshRenderer[] bolts;
        public Light flashLight;
        MaterialPropertyBlock properties;
        Material sky;

        void OnEnable() {
            properties=new MaterialPropertyBlock();
            sky=RenderSettings.skybox;
            SetFlash(0);
        }

        void LateUpdate() {
            float clock=Mathf.Repeat(Time.time+3f,9.7f);
            float a=Mathf.Exp(-Mathf.Pow((clock-.21f)*8f,2));
            float b=.72f*Mathf.Exp(-Mathf.Pow((clock-.48f)*11f,2));
            SetFlash(Mathf.Clamp01(a+b));
        }

        public void SetFlash(float flash) {
            if(properties==null)properties=new MaterialPropertyBlock();
            if(!sky)sky=RenderSettings.skybox;
            foreach(var bolt in bolts) {
                if(!bolt)continue;
                bolt.GetPropertyBlock(properties);
                properties.SetFloat("_Flash",flash);
                bolt.SetPropertyBlock(properties);
            }
            if(sky&&sky.name.StartsWith("world009"))sky.SetFloat("_StormFlash",.12f+.61f*flash);
            if(flashLight)flashLight.intensity=35f+168f*flash;
        }
    }
}
