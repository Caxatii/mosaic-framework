using UnityEngine;

namespace Mosaic.Ui
{
    public abstract class NumberedConfig : ScriptableObject
    {
        [SerializeField] private int _id;

        public int Id => _id;

#if UNITY_EDITOR
        public void AssignId(int id) => _id = id;
#endif
    }
}
