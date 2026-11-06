using Mosaic.Core;
using Mosaic.Ui;
using UnityEditor;

namespace Mosaic.Editor
{
    [CustomPropertyDrawer(typeof(EntityRef))]
    public sealed class EntityRefDrawer : ConfigRefDrawer<EntityConfig>
    {
    }
}
