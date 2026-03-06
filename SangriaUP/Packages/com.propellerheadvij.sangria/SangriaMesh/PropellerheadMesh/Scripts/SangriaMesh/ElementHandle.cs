namespace SangriaMesh
{
    public readonly struct ElementHandle
    {
        public readonly int Index;
        public readonly uint Generation;

        public ElementHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Index >= 0 && Generation != 0;

        public override string ToString() => $"[{Index}:{Generation}]";
    }
}
