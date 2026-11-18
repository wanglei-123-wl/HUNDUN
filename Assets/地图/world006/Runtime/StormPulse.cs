using UnityEngine;

namespace World006 {
    public sealed class StormPulse:MonoBehaviour {
        public MeshRenderer[] bolts;
        public Light flashLight;
        MaterialPropertyBlock properties;
        Material sky;

        void OnEnable() {
            properties=new MaterialPropertyBlock();
            sky=RenderSettings.skybox;
        }

        void LateUpdate() {
            float clock=Mathf.Repeat(Time.time+3f,9.7f);
            float a=Mathf.Exp(-Mathf.Pow((clock-.21f)*8f,2));
            float b=.72f*Mathf.Exp(-Mathf.Pow((clock-.48f)*11f,2));
            float flash=Mathf.Clamp01(a+b);
            foreach(var bolt in bolts) {
                if(!bolt)continue;
                bolt.GetPropertyBlock(properties);
                properties.SetFloat("_Flash",flash);
                bolt.SetPropertyBlock(properties);
            }
            if(sky&&sky.name.StartsWith("world006"))sky.SetFloat("_StormFlash",flash);
            if(flashLight)flashLight.intensity=31f+230f*flash;
        }
    }
}
