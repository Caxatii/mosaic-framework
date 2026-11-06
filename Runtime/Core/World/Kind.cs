namespace Mosaic.Core
{
    public sealed class Kind : IModel
    {
        public Kind(int id) => Id = id;

        public int Id { get; }
    }
}
