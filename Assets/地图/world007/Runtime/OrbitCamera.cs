using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace World007 {
    public class OrbitCamera:MonoBehaviour {
        public Vector3 target,home;
        public Vector2 extents;
        public MeshRenderer[] ceiling;
        float distance,yaw,pitch;

        void Start() {
            var offset=transform.position-target;
            distance=offset.magnitude;
            yaw=Mathf.Atan2(offset.x,offset.z)*Mathf.Rad2Deg;
            pitch=Mathf.Asin(offset.y/Mathf.Max(1,distance))*Mathf.Rad2Deg;
        }

        void LateUpdate() {
            Vector2 mouse=Vector2.zero,pan=Vector2.zero;
            float zoom=0;
            bool orbit=false,reset=false,fast=false;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            if(Mouse.current!=null) {
                mouse=Mouse.current.delta.ReadValue()*.2f;
                orbit=Mouse.current.rightButton.isPressed;
                zoom=Mouse.current.scroll.ReadValue().y/120f;
            }
            if(Keyboard.current!=null) {
                var k=Keyboard.current;
                pan=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
                reset=k.homeKey.wasPressedThisFrame;
                fast=k.leftShiftKey.isPressed;
            }
#else
            mouse=new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y"));
            orbit=Input.GetMouseButton(1);
            zoom=Input.mouseScrollDelta.y;
            pan=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),
                            (Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
            reset=Input.GetKeyDown(KeyCode.Home);
            fast=Input.GetKey(KeyCode.LeftShift);
#endif
            if(!orbit&&Mathf.Abs(zoom)<.001f&&pan==Vector2.zero&&!reset)return;
            if(orbit) {
                yaw+=mouse.x*2.3f;
                pitch=Mathf.Clamp(pitch-mouse.y*2.3f,-8,87);
            }
            distance=Mathf.Clamp(distance*Mathf.Exp(-zoom*.12f),2.5f,Mathf.Max(extents.x,extents.y)*2.8f);
            if(pan!=Vector2.zero) {
                target+=Quaternion.Euler(0,yaw,0)*new Vector3(pan.x,0,pan.y)*Time.deltaTime*(fast?115:38);
                target.x=Mathf.Clamp(target.x,-extents.x+2,extents.x-2);
                target.z=Mathf.Clamp(target.z,-extents.y+2,extents.y-2);
                if(Physics.Raycast(new Vector3(target.x,420,target.z),Vector3.down,out var ground,520))
                    target.y=ground.point.y+2;
            }
            if(reset) {
                target=home;
                distance=Mathf.Max(extents.x,extents.y)*2.05f;
                pitch=76;
                yaw=174;
            }
            transform.position=target+Quaternion.Euler(-pitch,yaw,0)*new Vector3(0,0,distance);
            transform.LookAt(target);
            foreach(var roof in ceiling)if(roof)roof.enabled=transform.position.y<32;
        }
    }
}
