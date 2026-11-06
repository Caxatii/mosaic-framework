using System.Collections.Generic;
using Mosaic.Core;
using UnityEngine;

namespace Mosaic.Ui
{
    [CreateAssetMenu(menuName = "Mosaic/Entity")]
    public sealed class EntityConfig : NumberedConfig
    {
        public GameObject Prefab;

        [SerializeReference] public List<IEntityPart> Parts = new List<IEntityPart>();

        public EntityRef Ref => new EntityRef(Id);

        public EntityDefinition ToDefinition() => new EntityDefinition(Id, name, Parts.ToArray());
    }
}
