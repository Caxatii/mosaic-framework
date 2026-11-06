using System.Linq;
using System.Text;

namespace Mosaic.Core
{
    /// <summary>
    /// Text dump of a world and its short hash, to compare a recording with its replay.
    /// </summary>
    public static class WorldFingerprint
    {
        public static string Describe(World world)
        {
            var text = new StringBuilder();
            foreach (var id in world.Entities.OrderBy(id => id.Index))
            {
                text.Append(id).Append('\n');
                foreach (var component in world.GetAll(id).OrderBy(component => component.GetType().FullName))
                    text.Append("  ").Append(TraceFormat.Of(component)).Append('\n');
            }

            return text.ToString();
        }

        public static string Of(World world)
        {
            var hash = 14695981039346656037UL;
            foreach (var symbol in Describe(world))
            {
                hash ^= symbol;
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16");
        }
    }
}
