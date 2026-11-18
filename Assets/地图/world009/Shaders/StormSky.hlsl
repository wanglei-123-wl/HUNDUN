#ifndef WORLD009_STORM_PANORAMA
#define WORLD009_STORM_PANORAMA
TEXTURE2D(_StormSkyTex);
SAMPLER(sampler_StormSkyTex);

half3 StormPanorama(float3 direction, float drift) {
    float yaw=atan2(direction.x,direction.z)-.28;
    yaw=atan2(sin(yaw),cos(yaw));
    float elevation=atan2(direction.y,length(direction.xz));
    float v=elevation<=.45?max(.034,.083+elevation*1.46):
        .740+.233*(1-exp(-4.15*(elevation-.45)));
    float front=abs(yaw);
    float rearYaw=atan2(sin(yaw+3.141593),cos(yaw+3.141593));
    float u=clamp(.49+yaw*.286+drift,.017,.983);
    float rearU=clamp(.51-rearYaw*.277-drift,.017,.983);
    half3 north=SAMPLE_TEXTURE2D(_StormSkyTex,sampler_StormSkyTex,float2(u,v)).rgb;
    half3 south=SAMPLE_TEXTURE2D(_StormSkyTex,sampler_StormSkyTex,float2(rearU,v*.93+.035)).rgb;
    half3 cloud=lerp(north,south*half3(.57,.64,.74),smoothstep(1.10,2.04,front));
    cloud=lerp(cloud,half3(.022,.040,.051),.17*(1-smoothstep(-.055,.15,elevation)));
    return lerp(cloud,half3(.009,.018,.027),.49*smoothstep(.78,1.41,elevation));
}
#endif
