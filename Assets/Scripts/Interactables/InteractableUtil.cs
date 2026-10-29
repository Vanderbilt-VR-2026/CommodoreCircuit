using UnityEngine;

namespace CommodoreCircuit.Interactables
{
    public static class InteractableUtil
    {
        /// <summary>Depth-first search for a descendant by exact name.</summary>
        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var hit = FindDeep(child, name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Remembers a part's rest pose so animation layers on top of it.</summary>
        public struct Rest
        {
            public Transform t;
            public Vector3 pos;
            public Quaternion rot;

            public Rest(Transform t)
            {
                this.t = t;
                pos = t != null ? t.localPosition : Vector3.zero;
                rot = t != null ? t.localRotation : Quaternion.identity;
            }

            /// <summary>Apply an offset in the PARENT's space (axes match the model root).</summary>
            public void Pose(Vector3 eulerOffset, Vector3 posOffset = default)
            {
                if (t == null) return;
                t.localRotation = Quaternion.Euler(eulerOffset) * rot;
                t.localPosition = pos + posOffset;
            }
        }
    }
}
