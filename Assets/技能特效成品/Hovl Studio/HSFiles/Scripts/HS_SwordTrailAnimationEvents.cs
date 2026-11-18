using System.Collections.Generic;
using UnityEngine;

namespace Hovl
{
    /// <summary>
    /// Receives Animation Events and forwards them to every registered sword trail.
    /// When Work Without Animation is enabled, all trails emit continuously while
    /// this component is active.
    /// </summary>
    [DisallowMultipleComponent]
    public class HS_SwordTrailAnimationEvents : MonoBehaviour
    {
        [Tooltip("此组件把动画事件转发给列表里的拖尾。子物体上的拖尾会自动加入。")]
        [SerializeField]
        private List<HS_SwordMeshTrail> swordTrails =
            new List<HS_SwordMeshTrail>();

        [Tooltip("开启后，组件启用期间会持续生成刀光。当前物体没有 Animator 时会自动开启。")]
        [SerializeField]
        private bool workWithoutAnimation;

        private bool hasStarted;

        // Compatibility accessor for projects that previously used one trail.
        public HS_SwordMeshTrail SwordTrail =>
            swordTrails != null && swordTrails.Count > 0 ? swordTrails[0] : null;

        public IReadOnlyList<HS_SwordMeshTrail> SwordTrails => swordTrails;
        public bool WorkWithoutAnimation => workWithoutAnimation;

        private void Reset()
        {
            RefreshSwordTrails();
            workWithoutAnimation = GetComponent<Animator>() == null;
        }

        private void Awake()
        {
            RefreshSwordTrails();

            if (GetComponent<Animator>() == null)
            {
                workWithoutAnimation = true;
            }
        }

        private void Start()
        {
            hasStarted = true;

            if (workWithoutAnimation)
            {
                StartSwordTrail();
            }
        }

        private void OnEnable()
        {
            if (hasStarted && workWithoutAnimation)
            {
                StartSwordTrail();
            }
        }

        private void OnDisable()
        {
            if (hasStarted && workWithoutAnimation)
            {
                StopSwordTrail();
            }
        }

        private void OnValidate()
        {
            RefreshSwordTrails();

            if (GetComponent<Animator>() == null)
            {
                workWithoutAnimation = true;
            }
        }

        /// <summary>
        /// Kept for compatibility with existing code. The trail is added to the
        /// collection instead of replacing the previously assigned trail.
        /// </summary>
        public void SetSwordTrail(HS_SwordMeshTrail newSwordTrail)
        {
            RegisterSwordTrail(newSwordTrail);
        }

        public void RegisterSwordTrail(HS_SwordMeshTrail newSwordTrail)
        {
            if (newSwordTrail == null)
            {
                return;
            }

            RemoveMissingSwordTrails();

            if (!swordTrails.Contains(newSwordTrail))
            {
                swordTrails.Add(newSwordTrail);
            }

            if (hasStarted && workWithoutAnimation &&
                Application.isPlaying && isActiveAndEnabled)
            {
                newSwordTrail.StartTrail();
            }
        }

        public void UnregisterSwordTrail(HS_SwordMeshTrail swordTrail)
        {
            if (swordTrail != null)
            {
                swordTrails.Remove(swordTrail);
            }

            RemoveMissingSwordTrails();
        }

        public void SetWorkWithoutAnimation(bool enabled)
        {
            if (workWithoutAnimation == enabled)
            {
                return;
            }

            workWithoutAnimation = enabled;

            if (!Application.isPlaying || !hasStarted || !isActiveAndEnabled)
            {
                return;
            }

            if (workWithoutAnimation)
            {
                StartSwordTrail();
            }
            else
            {
                StopSwordTrail();
            }
        }

        [ContextMenu("刷新刀光拖尾列表")]
        public void RefreshSwordTrails()
        {
            RemoveMissingSwordTrails();

            HS_SwordMeshTrail[] foundTrails =
                GetComponentsInChildren<HS_SwordMeshTrail>(true);

            for (int i = 0; i < foundTrails.Length; i++)
            {
                HS_SwordMeshTrail foundTrail = foundTrails[i];

                if (foundTrail != null && !swordTrails.Contains(foundTrail))
                {
                    swordTrails.Add(foundTrail);
                }
            }
        }

        private void RemoveMissingSwordTrails()
        {
            if (swordTrails == null)
            {
                swordTrails = new List<HS_SwordMeshTrail>();
                return;
            }

            for (int i = swordTrails.Count - 1; i >= 0; i--)
            {
                if (swordTrails[i] == null)
                {
                    swordTrails.RemoveAt(i);
                }
            }
        }

        // Animation Event
        public void StartSwordTrail()
        {
            RemoveMissingSwordTrails();

            for (int i = 0; i < swordTrails.Count; i++)
            {
                swordTrails[i].StartTrail();
            }
        }

        // Animation Event
        public void StopSwordTrail()
        {
            RemoveMissingSwordTrails();

            for (int i = 0; i < swordTrails.Count; i++)
            {
                swordTrails[i].StopTrail();
            }
        }

        // Animation Event
        public void ClearSwordTrail()
        {
            RemoveMissingSwordTrails();

            for (int i = 0; i < swordTrails.Count; i++)
            {
                swordTrails[i].ClearTrail();
            }
        }
    }
}